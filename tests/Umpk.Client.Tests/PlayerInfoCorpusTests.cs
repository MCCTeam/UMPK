using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Client.Tests;

/// <summary>End-to-end proof, over real recorded server traffic, that the legacy-era tab list is populated. Every committed corpus whose protocol is at or below 760 (the last protocol carrying the single <c>minecraft:player_info</c> packet) is replayed through the real descriptor binding into the applier chain; any capture that carries a legacy add-player frame must end with a non-empty <see cref="TabList"/>. On protocol 47 the packet decoded but no applier consumed it, and on 107-760 it was a registration marker that never decoded at all, so the tab list stayed empty for the whole band.</summary>
public sealed class PlayerInfoCorpusTests
{
    /// <summary>The last protocol whose tab list rides the single legacy player_info packet (1.19.2).</summary>
    private const int LastLegacyProtocol = 760;

    /// <summary>The first protocol of the 1.19 signing sub-era, whose add-player payload gained a profile key.</summary>
    private const int FirstSigningEraProtocol = 759;

    private readonly ITestOutputHelper _output;

    public PlayerInfoCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task LegacyWireLayoutCorpora_PopulateTheTabList()
    {
        string? root = FindCorpusRoot();
        Assert.False(root is null, "corpus root not found");

        var protocolsWithAdds = new SortedSet<int>();
        var failures = new List<string>();
        int capturesInspected = 0;

        foreach (string path in CorpusLoader.DiscoverCaptures(root!))
        {
            LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(path);
            if (corpus.Protocol > LastLegacyProtocol)
                continue;

            capturesInspected++;
            (int adds, int peakEntries, string? name) = await ReplayAsync(corpus);
            if (adds == 0)
                continue;

            protocolsWithAdds.Add(corpus.Protocol);
            _output.WriteLine(
                $"protocol {corpus.Protocol} {Path.GetFileName(path)}: {adds} legacy add frames, " +
                $"peak tab-list entries {peakEntries}, first name '{name}'.");

            if (peakEntries == 0)
                failures.Add(
                    $"{path} (protocol {corpus.Protocol}) carried {adds} legacy add-player frames but the " +
                    "tab list stayed empty.");

        }

        Assert.True(capturesInspected > 0, "no legacy-era corpora were inspected");
        Assert.Empty(failures);

        // Guard against a vacuous pass: the band must actually be exercised, on both sub-eras. The 1.8-1.18.2 codec and the 1.19/1.19.2 profile-key codec are different members, so both need a live witness.
        Assert.True(
            protocolsWithAdds.Any(static p => p < FirstSigningEraProtocol),
            "no pre-1.19 corpus carried a legacy add-player frame; the 47-758 leg is unexercised");
        Assert.True(
            protocolsWithAdds.Any(static p => p >= FirstSigningEraProtocol),
            "no 1.19/1.19.2 corpus carried a legacy add-player frame; the 759/760 profile-key leg is unexercised");
    }

    /// <summary>Replays one capture's effective play-clientbound frames and reports how many legacy add-player frames it carried, the peak tab-list size reached, and the first player name that landed in the tab list (entries can be added and removed again within a capture, so the peak is the signal).</summary>
    private static async Task<(int Adds, int PeakEntries, string? FirstName)> ReplayAsync(LoadedCorpus corpus)
    {
        Assert.True(JavaVersions.TryGetByProtocol(corpus.Protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;
        var harness = new ApplierHarness(version);

        int adds = 0;
        int peak = 0;
        string? firstName = null;
        bool loginTerminated = false;

        foreach (RecordedFrame frame in corpus.Frames)
        {
            if (!IsEffectivePlayClientbound(descriptor, frame, ref loginTerminated))
                continue;

            if (!descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry) ||
                !registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec) ||
                !codec.IsImplemented)
                continue;

            object packet;
            try
            {
                packet = codec.Decode(frame.Body, PacketCodecContext.Registryless);
            }
            catch
            {
                continue;
            }

            if (packet is ClientboundLegacyPlayerListItemPacket
                { Action: LegacyPlayerListAction.AddPlayer, Entries.Count: > 0 })
                adds++;

            await harness.ApplyAsync(packet);

            if (harness.State.TabList.Count > peak)
            {
                peak = harness.State.TabList.Count;
                firstName ??= harness.State.TabList.Entries
                    .Select(static e => e.Profile.Name)
                    .FirstOrDefault(static n => !string.IsNullOrEmpty(n));
            }
        }

        return (adds, peak, firstName);
    }

    // Pre-config phase-lag latch, mirroring CorpusReplayTests. Pre-1.20.2 versions have no configuration phase and no terminal-packet gate, so once a clientbound login_finished is observed the recorder can still label the first burst of Play frames with the lagging Login phase. That window is exactly where the join-time player_info frames live on the legacy band, so treating them as Play is load-bearing here.
    private static bool IsEffectivePlayClientbound(
        ProtocolDescriptor descriptor, RecordedFrame frame, ref bool loginTerminated)
    {
        if (frame.Direction != CorpusDirection.Clientbound)
            return false;

        ProtocolPhase phase = CorpusEnumMapping.ToProtocolPhase(frame.Phase);
        if (phase == ProtocolPhase.Play)
            return true;

        if (phase != ProtocolPhase.Login)
            return false;

        if (!loginTerminated &&
            descriptor.TryGetRegistry(ProtocolPhase.Login, PacketFlow.Clientbound, out PhaseRegistry loginRegistry) &&
            loginRegistry.TryGetInbound(frame.WireId, out BoundPacketCodec loginCodec) &&
            loginCodec.Type.Id == LoginPackets.Clientbound.LoginFinished.Id)
        {
            loginTerminated = true;
            return false;
        }

        bool hasConfig = descriptor.TryGetRegistry(ProtocolPhase.Configuration, PacketFlow.Clientbound, out _) ||
            descriptor.TryGetRegistry(ProtocolPhase.Configuration, PacketFlow.Serverbound, out _);
        return loginTerminated && !hasConfig &&
            descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play) &&
            play.TryGetInbound(frame.WireId, out _);
    }

    private static string? FindCorpusRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "fixtures", "corpus");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
