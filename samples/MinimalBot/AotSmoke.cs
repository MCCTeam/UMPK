using System.Buffers;
using System.Globalization;
using Umpk;
using Umpk.Auth;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

/// <summary>Exercises packet registration, codec discovery, and a complete session from the published native-AOT binary.</summary>
/// <remarks>Descriptor and codec registrations are data-driven, so decoding frames from both ends of the supported protocol range verifies that native trimming retained those registrations.</remarks>
internal static class AotSmoke
{
    /// <summary>Runs the smoke and returns the process exit code.</summary>
    /// <param name="args">The endpoint of the harness's fake server, the protocol to play it at, and one or more <c>protocol/phase/wireId/hexBody</c> frames to decode first.</param>
    /// <param name="ct">Cancels the session leg.</param>
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 3
            || !ServerEndpoint.TryParse(args[0], out ServerEndpoint endpoint)
            || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sessionProtocol))
        {
            Console.Error.WriteLine(
                "Usage: MinimalBot --aot-smoke <host:port> <sessionProtocol> <protocol>/<phase>/<wireId>/<hexBody>...");
            return 2;
        }

        foreach (string frame in args[2..])
            if (!Decode(frame))
                return 3;

        return await JoinAsync(endpoint, sessionProtocol, ct);
    }

    private static bool Decode(string frame)
    {
        string[] parts = frame.Split('/');
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int protocol)
            || !Enum.TryParse(parts[1], out ProtocolPhase phase)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wireId))
        {
            Console.Error.WriteLine($"Malformed frame argument \"{frame}\".");
            return false;
        }

        if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion version))
        {
            Console.Error.WriteLine($"No version data for protocol {protocol}.");
            return false;
        }

        if (!version.Protocol.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry)
            || !registry.TryGetInbound(wireId, out BoundPacketCodec codec)
            || !codec.IsImplemented)
        {
            Console.Error.WriteLine($"Protocol {protocol} binds no implemented codec to {phase} clientbound {wireId}.");
            return false;
        }

        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        byte[] payload = Convert.FromHexString(parts[3]);
        object packet = codec.Decode(payload, context);
        var encoded = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(encoded);
        codec.Encode(ref writer, packet, context);
        if (!encoded.WrittenSpan.SequenceEqual(payload))
        {
            Console.Error.WriteLine($"Protocol {protocol} packet {codec.DatasetIdentifier} did not round-trip byte-exactly.");
            return false;
        }

        Console.WriteLine(
            $"decoded protocol={protocol} wire={wireId} packet={codec.DatasetIdentifier} type={packet.GetType().Name}");
        return true;
    }

    private static async Task<int> JoinAsync(ServerEndpoint endpoint, int protocol, CancellationToken ct)
    {
        if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion version))
        {
            Console.Error.WriteLine($"No version data for protocol {protocol}.");
            return 1;
        }

        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(OfflineIdentity.ComputeProfile("AotSmoke"))
            .UseStaticRegistries(JavaGameData.Registries(protocol))
            .Build();

        await client.ConnectAsync(endpoint, ct);
        await client.Actions.Chat.SendChatAsync("aot-smoke", ct);
        Console.WriteLine($"joined {endpoint} on {version.Version.Name}.");
        await client.DisconnectAsync(CancellationToken.None);
        return 0;
    }
}
