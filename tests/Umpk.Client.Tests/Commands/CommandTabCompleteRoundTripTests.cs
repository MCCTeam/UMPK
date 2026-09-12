using System.Buffers;
using Umpk.Client.Commands;
using Umpk.Client.Internal;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests.Commands;

/// <summary>End-to-end server tab-complete test against a scripted <see cref="FakeJavaServer"/>: the client sends a real <c>command_suggestion</c> frame over a real <see cref="JavaConnection"/>; the fake server reads it, replies with an encoded <c>command_suggestions</c> frame; the client decodes it and the <see cref="CommandCompletionService"/> correlates the response back to the request. Proves the wire round trip, not just the in-memory correlation.</summary>
public sealed class CommandTabCompleteRoundTripTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ServerRoundTrip_RequestAndResponse_OverWire()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        JavaVersion version = JavaVersions.V1_21_5;
        ProtocolDescriptor descriptor = version.Protocol;
        int commandSuggestionWireId = OutboundWireId(descriptor, UiPackets.Serverbound.CommandSuggestion);
        int commandSuggestionsWireId = InboundWireId(descriptor, UiPackets.Clientbound.CommandSuggestions);

        await using FakeJavaServer server = FakeJavaServer.Create();

        var clientOptions = new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        };
        await using var client = new JavaConnection(server.ClientPipe, clientOptions);
        var binding = new DescriptorFrameCodecBinding(descriptor);
        client.BindCodec(binding, PacketFlow.Clientbound);
        client.SetPhase(ProtocolPhase.Play);
        client.Start();

        var completion = new CommandCompletionService(new PacketSink(client));

        // Route decoded clientbound suggestions to the completion service.
        var routed = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                InboundItem item = await client.ReceiveAsync(ct).ConfigureAwait(false);
                if (item.Packet is ClientboundCommandSuggestionsPacket p)
                {
                    completion.Complete(p);
                    return;
                }
            }
        }, ct);

        Task<CompletionResult> pending = completion.RequestAsync("/msg Ste", ct);

        // The fake server reads the serverbound command_suggestion frame and decodes its transaction id.
        InboundFrame request = await server.NextFrameAsync(ct);
        Assert.Equal(commandSuggestionWireId, request.WireId);
        var reqReader = new PacketReader(request.Payload);
        int transactionId = reqReader.ReadVarInt();
        string requestText = reqReader.ReadString();
        Assert.Equal("/msg Ste", requestText);

        // Reply with a command_suggestions frame carrying the same transaction id.
        var response = new ClientboundCommandSuggestionsPacket(
            transactionId, 5, 3, [new CommandSuggestion("Steve", Component.Text("player"))]);
        byte[] responseBody = EncodeInbound(descriptor, UiPackets.Clientbound.CommandSuggestions, response);
        await server.SendFrameAsync(commandSuggestionsWireId, responseBody, ct);

        CompletionResult result = await pending.WaitAsync(Budget, ct);
        await routed.WaitAsync(Budget, ct);

        Assert.Equal(5, result.RangeStart);
        Assert.Equal(8, result.RangeEnd);
        Assert.Contains(result.Suggestions, s => s.Text == "Steve");
    }

    private static int OutboundWireId<T>(ProtocolDescriptor descriptor, PacketType<T> type)
        where T : class, IPacket
    {
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry registry));
        Assert.True(registry.TryGetOutbound(type, out int wireId, out _));
        return wireId;
    }

    private static int InboundWireId<T>(ProtocolDescriptor descriptor, PacketType<T> type)
        where T : class, IPacket
    {
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType candidate) in registry.Packets)
            if (candidate.Id == type.Id)
                return wireId;

        throw new Xunit.Sdk.XunitException($"{type.Id} not registered inbound.");
    }

    private static byte[] EncodeInbound<T>(ProtocolDescriptor descriptor, PacketType<T> type, T packet)
        where T : class, IPacket
    {
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType candidate) in registry.Packets)
            if (candidate.Id == type.Id && registry.TryGetInbound(wireId, out BoundPacketCodec codec))
            {
                var buffer = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(buffer);
                codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
                return buffer.WrittenSpan.ToArray();
            }

        throw new Xunit.Sdk.XunitException($"No inbound codec for {type.Id}.");
    }
}
