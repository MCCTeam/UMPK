using System.Globalization;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit.Corpus;

namespace Umpk.Client.Tests;

/// <summary>Pulls a recorded <c>update_advancements</c> frame out of the committed corpus and decodes it through the protocol's real descriptor and static registries, so an applier test can drive the same bytes a live 1.17 session received rather than a hand-built packet.</summary>
internal static class AdvancementCorpus
{
    /// <summary>Decodes the biggest recorded advancements frame in a capture (the populated tree).</summary>
    public static object DecodeLargestFrame(int protocol, string scenario, JavaVersion version)
    {
        if (!version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry))
            throw new InvalidOperationException($"protocol {protocol} has no clientbound play registry");

        string path = Path.Combine(
            CorpusRoot(), protocol.ToString(CultureInfo.InvariantCulture), $"{scenario}.umpkcap");
        LoadedCorpus corpus = CorpusLoader.LoadFileAsync(path).GetAwaiter().GetResult();

        RecordedFrame? best = null;
        BoundPacketCodec? bestCodec = null;
        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (frame.Direction != CorpusDirection.Clientbound ||
                !registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) ||
                codec.Type.Id != Identifier.Minecraft("update_advancements"))
                continue;

            if (best is null || frame.Body.Length > best.Body.Length)
            {
                best = frame;
                bestCodec = codec;
            }
        }

        if (best is null || bestCodec is null)
            throw new InvalidOperationException($"no update_advancements frame in {path}");

        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        return bestCodec.Decode(best.Body, context);
    }

    private static string CorpusRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "fixtures", "corpus");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("corpus fixtures not found");
    }
}
