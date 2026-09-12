using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>A (phase, flow) may hold one entry per wire id and one entry per packet identity. Both were last-write-wins, which is the one duplicate-registration case that produced no diagnostic: the registration pin only renders the surviving entry, so a second binding at an occupied id disappeared from the table and from the fixture together.</summary>
public sealed class PhaseRegistryDuplicateTests
{
    private sealed record FirstPacket : IPacket
    {
        public static readonly PacketType<FirstPacket> Identity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("umpk_test_first"));

        public PacketType Type => Identity;
    }

    private sealed record SecondPacket : IPacket
    {
        public static readonly PacketType<SecondPacket> Identity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("umpk_test_second"));

        public PacketType Type => Identity;
    }

    [Fact]
    public void TwoPacketsAtOneWireId_Throw()
    {
        ProtocolDescriptorBuilder builder = Builder()
            .Register(7, FirstPacket.Identity, Codec<FirstPacket>(static () => new FirstPacket()))
            .Register(7, SecondPacket.Identity, Codec<SecondPacket>(static () => new SecondPacket()));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("umpk_test_first", error.Message, StringComparison.Ordinal);
        Assert.Contains("umpk_test_second", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnePacketTypeAtTwoWireIds_Throws()
    {
        ProtocolDescriptorBuilder builder = Builder()
            .Register(7, FirstPacket.Identity, Codec<FirstPacket>(static () => new FirstPacket()))
            .Register(8, FirstPacket.Identity, Codec<FirstPacket>(static () => new FirstPacket()));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("umpk_test_first", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A marker and an implemented packet collide the same way; only the message differs.</summary>
    [Fact]
    public void AMarkerOverAnImplementedPacket_Throws()
    {
        ProtocolDescriptorBuilder builder = Builder()
            .Register(7, FirstPacket.Identity, Codec<FirstPacket>(static () => new FirstPacket()));
        builder.RegisterMarker(7, SecondPacket.Identity);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    private static ProtocolDescriptorBuilder Builder() =>
        new(new GameVersion(GameEdition.Java, "test", 776), new ProtocolFeatures());

    private static PacketCodec<T> Codec<T>(Func<T> create)
        where T : class, IPacket =>
        PacketCodec<T>.Of(
            static (ref PacketWriter writer, T value, PacketCodecContext context) => { },
            (ref PacketReader reader, PacketCodecContext context) => create());
}
