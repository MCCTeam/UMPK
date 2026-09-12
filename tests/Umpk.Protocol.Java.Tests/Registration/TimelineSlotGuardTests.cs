using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>A timeline step may re-type the packet an era carries, but it may not move it: the descriptor files every entry under the bound type's own phase and flow, so a step whose type sits elsewhere fills a registry the frame path never consults for that identifier. The packet then reads as unregistered rather than as misbound, which is the failure this library has the hardest time seeing.</summary>
public sealed class TimelineSlotGuardTests
{
    private sealed record PlayPacket : IPacket
    {
        public static readonly PacketType<PlayPacket> Identity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("umpk_test_play"));

        public PacketType Type => Identity;
    }

    private sealed record ConfigurationPacket : IPacket
    {
        public static readonly PacketType<ConfigurationPacket> Identity =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("umpk_test_config"));

        public PacketType Type => Identity;
    }

    [Fact]
    public void FromAs_RefusesAnWireLayoutTypedIntoAnotherPhase()
    {
        var bindings = new PacketBindings();
        PacketTimelineBuilder<PlayPacket> timeline = bindings.Packet(PlayPacket.Identity);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => timeline.FromAs(47, ConfigurationPacket.Identity, Codec<ConfigurationPacket>(static () => new ConfigurationPacket())));

        Assert.Contains("umpk_test_config", error.Message, StringComparison.Ordinal);
        Assert.Contains("Configuration", error.Message, StringComparison.Ordinal);
        Assert.Contains("Play", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The second half of the same invariant, checked where the binding table meets the dataset: the slot the timeline just filled must sit in the phase and flow the version dataset asked for.</summary>
    [Fact]
    public void Register_RefusesASlotFiledUnderAnotherPhase()
    {
        BoundPacketCodec slot = BoundPacketCodec.Create(
            7, ConfigurationPacket.Identity, Codec<ConfigurationPacket>(static () => new ConfigurationPacket()), "test", null);

        PacketRegistrar.VerifyResolvedSlot(slot, ProtocolPhase.Configuration, PacketFlow.Clientbound, ConfigurationPacket.Identity.Id);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => PacketRegistrar.VerifyResolvedSlot(slot, ProtocolPhase.Play, PacketFlow.Clientbound, ConfigurationPacket.Identity.Id));

        Assert.Contains("umpk_test_config", error.Message, StringComparison.Ordinal);
        Assert.Contains("Play", error.Message, StringComparison.Ordinal);
    }

    private static PacketCodec<T> Codec<T>(Func<T> create)
        where T : class, IPacket =>
        PacketCodec<T>.Of(
            static (ref PacketWriter writer, T value, PacketCodecContext context) => { },
            (ref PacketReader reader, PacketCodecContext context) => create());
}
