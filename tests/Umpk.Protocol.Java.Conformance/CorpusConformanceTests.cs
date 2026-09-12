using System.Text;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The packet corpus conformance suite: decode/re-encode byte-identity and exact-consumption over every committed corpus frame, plus the registration-honesty checks. Runs hermetically over committed fixtures whose expected bytes are independent of the codec under test.</summary>
public sealed class CorpusConformanceTests
{
    private readonly ITestOutputHelper _output;

    public CorpusConformanceTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Captures()
    {
        IReadOnlyList<string> paths = CorpusLoader.DiscoverCaptures(FixturePaths.CorpusRoot);
        if (paths.Count == 0)
        {
            // Sentinel row so the theory is never empty (xunit fails empty theories). The test skips.
            yield return [string.Empty];
            yield break;
        }

        foreach (string path in paths)
            yield return [path];

    }

    [Theory]
    [MemberData(nameof(Captures))]
    public async Task Corpus_EveryFrame_DecodesReEncodesOrPreserves(string capturePath)
    {
        if (capturePath.Length == 0)
        {
            _output.WriteLine("No corpora present; conformance decode/re-encode leg is a no-op until fixtures land.");
            return;
        }

        LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capturePath);
        Assert.True(JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version),
            $"Unknown protocol {corpus.Protocol} in {capturePath}.");
        ProtocolDescriptor descriptor = version!.Protocol;

        // Decode and re-encode against the capture protocol's real static registries, not an empty registry view. A live session always has registries, so an empty view puts every registry-resolving codec (item stacks above all) into a degraded mode that the client never runs in: a recorded frame carrying a real item id then fails the suite for a reason no session would ever hit. Observed on the 755 play-idle advancement tree, whose DisplayInfo icons carry real item ids.
        var codecContext = new PacketCodecContext(JavaGameData.Registries(corpus.Protocol), IConnectionCodecState.Empty);

        int byteIdentical = 0;
        int markerVerbatim = 0;
        int decodeThrew = 0;
        var failures = new List<string>();

        // Post-LoginSuccess phase-lag latch (see ConformanceRunner.CheckFrame). Pre-1.20.2 protocols have no configuration phase, so once a clientbound login_finished is observed every following clientbound frame is really Play even if the recorder still labels it Login. Frames within a capture are in wire order, so a forward latch faithfully mirrors the live phase machine.
        bool loginTerminated = false;

        foreach (RecordedFrame frame in corpus.Frames)
        {
            try
            {
                ConformanceRunner.FrameResult result = ConformanceRunner.CheckFrame(descriptor, frame, loginTerminated, codecContext);
                if (!loginTerminated && IsClientboundLoginFinished(descriptor, frame))
                    loginTerminated = true;

                switch (result.Outcome)
                {
                    case ConformanceRunner.Outcome.ByteIdentical:
                        byteIdentical++;
                        break;
                    case ConformanceRunner.Outcome.MarkerVerbatim:
                        markerVerbatim++;
                        break;
                    case ConformanceRunner.Outcome.DecodeFailed:
                        // An implemented codec that throws while decoding a real recorded frame, or throws while re-encoding it, is a hard fidelity failure, not a reported residual. The corpora currently measure zero of these, so this flip changes no result today; it closes the hole where a decode/re-encode throw in play phase was latched into a report line that gated nothing.
                        decodeThrew++;
                        failures.Add(result.Detail!);
                        break;
                    case ConformanceRunner.Outcome.ReEncodeMismatch:
                        // A codec that decoded but re-encoded different bytes is a real fidelity bug: fail.
                        failures.Add(result.Detail!);
                        break;
                    default:
                        failures.Add($"Unexpected outcome {result.Outcome}.");
                        break;
                }
            }
            catch (ConformanceViolation ex)
            {
                failures.Add(ex.Message);
            }
        }

        _output.WriteLine(
            $"{Path.GetFileName(capturePath)}: {corpus.Frames.Count} frames, " +
            $"{byteIdentical} byte-identical, {markerVerbatim} marker-verbatim, " +
            $"{decodeThrew} decode/re-encode throws (now hard failures), {failures.Count} hard failures.");

        if (failures.Count > 0)
        {
            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" conformance failures in ").Append(capturePath).Append(':').Append('\n');
            foreach (string f in failures.Take(20))
                sb.Append("  ").Append(f).Append('\n');

            Assert.Fail(sb.ToString());
        }
    }

    /// <summary>The exercised-coverage ratchet baseline: per protocol, the number of implemented codecs that at least one committed corpus frame currently exercises. The intended gate is stronger ("the suite fails on unexercised registrations"), but every committed corpus is clientbound-only and thin (headless-recorder limitation, FrameRecorder.cs), so no serverbound implemented codec can be exercised yet and full-fail mode would fail every protocol for reasons the corpus breadth cannot fix. Until serverbound-recording corpora plus synthetic fixtures land, this suite enforces a ratchet instead of the full fail: coverage may not DECREASE below these pinned counts (a real regression, e.g. a corpus shrinking or a codec silently un-registering), while the remaining gap to full coverage is reported, not failed. Update these numbers upward, never downward, when a corpus grows or a synthetic fixture is added; flip to full-fail once corpus breadth makes every implemented codec exercisable.</summary>
    private static readonly IReadOnlyDictionary<int, int> ExercisedCoverageRatchet = new Dictionary<int, int>
    {
        [47] = 8,
        // Pre-flattening baselines (1.9-1.12.2), pinned from the 107/109/315/335/340 chunk-join corpora (lean, recorded against vanilla offline 1.9/1.9.2/1.11/1.12/1.12.2 servers on port 25614). One corpus per codec-key group (V1_9/V1_9_2/V1_9_4/V1_12/V1_12_2).
        [107] = 12,
        [109] = 11,
        [315] = 12,
        [335] = 12,
        [340] = 12,
        // These protocols retain three frames per phase, flow, and wire id rather than one, so their baselines are higher than the adjacent 107/109 entries.
        [108] = 33,
        [110] = 30,
        [210] = 26,
        [316] = 22,
        [338] = 33,
        // 1.13.x baselines pinned from the 393/401/404 corpora (lean chunk-join per protocol, recorded against vanilla offline 1.13/1.13.1/1.13.2 servers on port 25613).
        [393] = 13,
        [401] = 13,
        [404] = 13,
        // Flattening-era baselines pinned from the 477-578 corpora (login-config + chunk-join + play-idle per protocol, recorded against vanilla offline 1.14-1.15.2 servers on port 25605).
        [477] = 30,
        [480] = 29,
        [485] = 29,
        [490] = 27,
        [498] = 27,
        [573] = 30,
        [575] = 30,
        [578] = 28,
        // Netty-modern baselines (1.16-1.18.2), pinned from the 735-758 corpora (login-config + chunk-join + play-idle per protocol, recorded against vanilla offline servers on port 25604).
        [735] = 23,
        [736] = 23,
        [751] = 23,
        [753] = 23,
        [754] = 23,
        [755] = 23,
        [756] = 25,
        [757] = 24,
        [758] = 24,
        // Signing-era baselines pinned from the 759-763 corpora (login-config + chunk-join + play-idle per protocol, recorded against vanilla offline 1.19/1.19.2/1.19.3/1.19.4/1.20.1 servers on port 25603). Real exercised counts once every 759-763 era codec (JoinGame with the first-packet registry blob, set_entity_data JSON metadata, section_blocks_update, level_particles, player_position, chunk, set_time, teleport_entity, update_attributes, server_data) round-trips its corpus frames. player_chat is implemented but not exercised live (offline servers do not send signed chat; it has synthetic round-trip + verifier tests). container_set_slot and container_set_content are markers for 759-763 because their state-id plus legacy-NBT stack form is not structurally decoded. The corpus count excludes them.
        [759] = 34,
        // Protocols 760-763 use the same container marker treatment as 759.
        [760] = 26,
        [761] = 26,
        [762] = 28,
        [763] = 26,
        // Protocol 764-767 baselines pinned from the 764-767 corpora (login-config + chunk-join + play-idle per protocol, plus the synthetic start-configuration re-entry on 764), recorded against vanilla offline servers. These are the real exercised counts the ExercisedCoverage test prints once every 764-767 codec (set_time, teleport_entity, player_position, update_attributes, server_data) round-trips its corpus frames.
        [764] = 31,
        [765] = 30,
        [766] = 39,
        [767] = 39,
        // Modern-tail baselines pinned from the 768-775 corpora (login-config + chunk-join + play-idle per protocol, recorded against vanilla offline servers on 2026-07-10). 768/769 each stepped down by one when update_advancements deliberately un-registered there: its icon stacks carry the 1.21.2/1.21.4 component-id orderings no current table models, and decoding them through the 1.21.5 table faulted live sessions (see UiBindings). The corpus frames still relay verbatim through the marker, they just no longer count as exercised codecs.
        [768] = 47,
        [769] = 51,
        [770] = 46,
        [771] = 49,
        [772] = 49,
        [773] = 48,
        // 774/775 each stepped down by one for the same reason as 768/769: their own component-id orderings are unmodeled, so update_advancements deliberately un-registered there (see UiBindings) and its corpus frames relay verbatim through the marker.
        [774] = 48,
        [775] = 48,
        [776] = 42,
    };

    [Fact]
    public async Task ExercisedCoverage_DoesNotRegress_Ratchet()
    {
        // For each corpus protocol: which registered (implemented) packets appear in at least one corpus frame. Coverage is ratcheted (must not drop below the pinned baseline); the unexercised remainder is reported, not failed, until corpus breadth makes full-fail mode achievable.
        IReadOnlyList<string> captures = CorpusLoader.DiscoverCaptures(FixturePaths.CorpusRoot);
        if (captures.Count == 0)
        {
            _output.WriteLine("No corpora present; exercised-coverage ratchet skipped.");
            return;
        }

        // Gather observed (protocol, phase, flow, wireId) across all corpora.
        var observed = new HashSet<(int Protocol, ProtocolPhase Phase, PacketFlow Flow, int WireId)>();
        var protocols = new HashSet<int>();
        foreach (string path in captures)
        {
            LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(path);
            protocols.Add(corpus.Protocol);
            foreach (RecordedFrame frame in corpus.Frames)
                observed.Add((corpus.Protocol,
                    CorpusEnumMapping.ToProtocolPhase(frame.Phase),
                    CorpusEnumMapping.ToFlow(frame.Direction),
                    frame.WireId));

        }

        foreach (int protocol in protocols.Order())
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
            ProtocolDescriptor descriptor = version!.Protocol;
            int implemented = 0;
            int exercised = 0;
            var unexercised = new List<string>();

            foreach ((ProtocolPhase phase, PacketFlow flow) in AllPhaseFlows())
            {
                if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                    continue;

                foreach ((int wireId, PacketType type) in registry.Packets)
                {
                    if (!registry.TryGetInbound(wireId, out BoundPacketCodec codec) &&
                        !registry.TryGetOutbound(type, out _, out codec!))
                        continue;

                    if (!codec.IsImplemented)
                        continue;

                    implemented++;
                    if (observed.Contains((protocol, phase, flow, wireId)))
                        exercised++;

                    else
                        unexercised.Add($"{phase}/{flow} 0x{wireId:X2} {type.Id}");

                }
            }

            _output.WriteLine(
                $"protocol {protocol}: {implemented} implemented codecs, {exercised} exercised by corpora, " +
                $"{unexercised.Count} unexercised.");
            foreach (string u in unexercised.Take(40))
                _output.WriteLine("  unexercised: " + u);

            // Ratchet: coverage may not regress below the pinned baseline. A drop means a corpus shrank or an implemented codec stopped being exercised, both real regressions worth failing on. The gap above the baseline is reported (the WriteLine above), not failed, until corpus breadth grows.
            Assert.True(
                ExercisedCoverageRatchet.TryGetValue(protocol, out int baseline),
                $"No exercised-coverage baseline pinned for protocol {protocol}; add it to ExercisedCoverageRatchet.");
            Assert.True(
                exercised >= baseline,
                $"Exercised-codec coverage for protocol {protocol} regressed: {exercised} exercised, " +
                $"baseline {baseline}. A corpus shrank or a codec un-registered. If this drop is intentional, " +
                $"update the pinned baseline (downward updates need a written justification).");
        }
    }

    // True when the frame is the clientbound login_finished (LoginSuccess) that terminates the login flow, resolved through the descriptor so no wire-id constant is hard-coded per protocol.
    private static bool IsClientboundLoginFinished(ProtocolDescriptor descriptor, RecordedFrame frame)
    {
        if (CorpusEnumMapping.ToProtocolPhase(frame.Phase) != ProtocolPhase.Login ||
            CorpusEnumMapping.ToFlow(frame.Direction) != PacketFlow.Clientbound)
            return false;

        return descriptor.TryGetRegistry(ProtocolPhase.Login, PacketFlow.Clientbound, out PhaseRegistry registry) &&
            registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) &&
            codec.Type.Id == LoginPackets.Clientbound.LoginFinished.Id;
    }

    private static IEnumerable<(ProtocolPhase, PacketFlow)> AllPhaseFlows()
    {
        foreach (ProtocolPhase phase in new[]
                 {
                     ProtocolPhase.Handshake, ProtocolPhase.Status, ProtocolPhase.Login,
                     ProtocolPhase.Configuration, ProtocolPhase.Play,
                 })
        {
            yield return (phase, PacketFlow.Clientbound);
            yield return (phase, PacketFlow.Serverbound);
        }
    }
}
