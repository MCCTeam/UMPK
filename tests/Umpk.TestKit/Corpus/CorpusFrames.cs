using Umpk.Data.Java;
using Umpk.Protocol.Java;

namespace Umpk.TestKit.Corpus;

/// <summary>One committed corpus frame, located by protocol/capture/index and resolved to the phase, flow and packet identity the codec layer decodes it under.</summary>
/// <param name="Protocol">The capture's protocol.</param>
/// <param name="Capture">The capture file name inside <c>fixtures/corpus/{protocol}</c>.</param>
/// <param name="Frame">The frame's zero-based index within the capture.</param>
/// <param name="Phase">The phase the frame resolves under, after the post-LoginSuccess correction.</param>
/// <param name="Flow">The flow the frame travelled.</param>
/// <param name="WireId">The frame's wire id.</param>
/// <param name="Packet">The canonical packet identifier the wire id resolves to.</param>
/// <param name="BodyLength">The recorded body length, in bytes.</param>
public sealed record CorpusFrameRef(
    int Protocol,
    string Capture,
    int Frame,
    ProtocolPhase Phase,
    PacketFlow Flow,
    int WireId,
    string Packet,
    int BodyLength);

/// <summary>Finds and loads single frames out of the committed corpus, so a measurement can be taken against real recorded bytes instead of an authored payload.</summary>
public static class CorpusFrames
{
    /// <summary>One frame per corpus-covered packet family: the family's newest covered protocol, its first capture in ordinal file-name order, and the first frame in that capture that resolves to an implemented codec for the family. The order is fully determined by the committed bytes, so the same family selects the same frame on every machine and every run.</summary>
    public static IReadOnlyList<CorpusFrameRef> SelectOnePerCoveredFamily() =>
        SelectOnePerCoveredFamily(EnumerateCoveredBindings());

    /// <summary>The same selection over an already-materialised enumeration, for a caller that also wants the full list and should not pay for a second walk of the corpus.</summary>
    public static IReadOnlyList<CorpusFrameRef> SelectOnePerCoveredFamily(IEnumerable<CorpusFrameRef> resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        var best = new Dictionary<(ProtocolPhase Phase, PacketFlow Flow, string Packet), CorpusFrameRef>();
        foreach (CorpusFrameRef reference in resolved)
        {
            var key = (reference.Phase, reference.Flow, reference.Packet);
            if (!best.TryGetValue(key, out CorpusFrameRef? held) || reference.Protocol > held.Protocol)
                best[key] = reference;

        }

        return [.. best.Values
            .OrderBy(r => r.Phase)
            .ThenBy(r => r.Flow)
            .ThenBy(r => r.Packet, StringComparer.Ordinal)];
    }

    /// <summary>Every corpus frame that resolves to an implemented codec, in a deterministic order.</summary>
    public static IEnumerable<CorpusFrameRef> EnumerateCoveredBindings() => EnumerateResolved();

    /// <summary>The first frame in <paramref name="protocol"/>'s corpus that resolves to <paramref name="packet"/> in the given phase and flow, or null when the corpus carries none.</summary>
    public static CorpusFrameRef? Find(int protocol, ProtocolPhase phase, PacketFlow flow, string packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        foreach (CorpusFrameRef reference in EnumerateResolved(protocol))
            if (reference.Phase == phase && reference.Flow == flow && reference.Packet == packet)
                return reference;

        return null;
    }

    /// <summary>The same as <see cref="Find(int, ProtocolPhase, PacketFlow, string)"/>, but throwing when the corpus carries no such frame, for callers whose whole point is the frame.</summary>
    public static CorpusFrameRef Require(int protocol, ProtocolPhase phase, PacketFlow flow, string packet) =>
        Find(protocol, phase, flow, packet)
        ?? throw new InvalidOperationException(
            $"No corpus frame for {packet} ({phase}/{flow}) on protocol {protocol}.");

    /// <summary>Reads the referenced frame's body and the bound codec that decodes it. The body is the payload AFTER the wire id, which is what <see cref="BoundPacketCodec.Decode"/> takes.</summary>
    public static (BoundPacketCodec Binding, byte[] Body) Load(CorpusFrameRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        LoadedCorpus corpus = ReadCapture(reference.Protocol, reference.Capture);
        RecordedFrame frame = corpus.Frames[reference.Frame];
        ProtocolDescriptor descriptor = DescriptorFor(reference.Protocol);
        if (!descriptor.TryGetRegistry(reference.Phase, reference.Flow, out PhaseRegistry registry)
            || !registry.TryGetInbound(frame.WireId, out BoundPacketCodec binding)
            || !binding.IsImplemented)
            throw new InvalidOperationException(
                $"Corpus frame {reference.Capture}#{reference.Frame} on protocol {reference.Protocol} " +
                $"no longer resolves to an implemented codec for {reference.Packet}.");

        return (binding, frame.Body);
    }

    /// <summary>Every corpus frame that resolves to an implemented codec, in a deterministic order.</summary>
    private static IEnumerable<CorpusFrameRef> EnumerateResolved(int? onlyProtocol = null)
    {
        string root = FixturePaths.CorpusRoot;
        if (!Directory.Exists(root))
            yield break;

        string[] protocolDirs = Directory.GetDirectories(root);
        Array.Sort(protocolDirs, StringComparer.Ordinal);
        foreach (string protocolDir in protocolDirs)
        {
            if (!int.TryParse(Path.GetFileName(protocolDir), out int protocol)
                || (onlyProtocol is int wanted && protocol != wanted))
                continue;

            ProtocolDescriptor descriptor = DescriptorFor(protocol);
            bool hasConfigurationPhase =
                descriptor.TryGetRegistry(ProtocolPhase.Configuration, PacketFlow.Clientbound, out _)
                || descriptor.TryGetRegistry(ProtocolPhase.Configuration, PacketFlow.Serverbound, out _);

            string[] captures = Directory.GetFiles(protocolDir, "*.umpkcap");
            Array.Sort(captures, StringComparer.Ordinal);
            foreach (string capturePath in captures)
            {
                LoadedCorpus corpus = ReadCapture(capturePath);

                // The recorder's Connection.Phase lags on a version with no configuration phase: after login_finished it can still label Play frames Login, and 0x01/0x03 are registered in both phases there. Resolving those against Play is what a live client's phase machine does, and it is the same correction the conformance runner applies.
                bool loginTerminated = false;
                for (int i = 0; i < corpus.Frames.Count; i++)
                {
                    RecordedFrame frame = corpus.Frames[i];
                    ProtocolPhase phase = CorpusEnumMapping.ToProtocolPhase(frame.Phase);
                    PacketFlow flow = CorpusEnumMapping.ToFlow(frame.Direction);
                    if (!hasConfigurationPhase && loginTerminated && phase == ProtocolPhase.Login
                        && flow == PacketFlow.Clientbound)
                        phase = ProtocolPhase.Play;

                    if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry)
                        || !registry.TryGetInbound(frame.WireId, out BoundPacketCodec entry))
                    {
                        if (hasConfigurationPhase || phase != ProtocolPhase.Login
                            || !descriptor.TryGetRegistry(ProtocolPhase.Play, flow, out registry)
                            || !registry.TryGetInbound(frame.WireId, out entry))
                            continue;

                        phase = ProtocolPhase.Play;
                    }

                    if (!loginTerminated
                        && phase == ProtocolPhase.Login
                        && flow == PacketFlow.Clientbound
                        && entry.Type.Id.ToString() == "minecraft:login_finished")
                        loginTerminated = true;

                    if (entry.IsImplemented)
                        yield return new CorpusFrameRef(
                            protocol,
                            Path.GetFileName(capturePath),
                            i,
                            phase,
                            flow,
                            frame.WireId,
                            entry.Type.Id.ToString(),
                            frame.Body.Length);

                }
            }
        }
    }

    private static LoadedCorpus ReadCapture(int protocol, string capture) =>
        ReadCapture(Path.Combine(
            FixturePaths.CorpusRoot,
            protocol.ToString(System.Globalization.CultureInfo.InvariantCulture),
            capture));

    private static LoadedCorpus ReadCapture(string capturePath)
    {
        byte[] capture = File.ReadAllBytes(capturePath);
        string manifestPath = capturePath + ".json";
        byte[] manifest = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : [];
        return CorpusLoader.Parse(capture, manifest);
    }

    private static ProtocolDescriptor DescriptorFor(int protocol) =>
        JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version)
            ? version!.Protocol
            : throw new InvalidOperationException($"Unknown protocol {protocol} in the corpus.");
}
