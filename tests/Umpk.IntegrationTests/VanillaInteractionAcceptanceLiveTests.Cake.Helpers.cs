using System.Buffers;
using System.Globalization;
using Umpk.Client.Actions;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    private sealed record CakeNativeScene(string Name, Hand Hand, GameMode Mode, bool FullFood, string? Item, int Bites, string ExpectedResultFromNativeSource, bool Eats, int PacketSwings, string ExpectedSwingFromNativeSource);

    private static CakeNativeScene[] NativeCakeScenes(int protocol) => protocol switch
    {
        47 =>
        [
            new("hungry-empty", Hand.Main, GameMode.Survival, false, null, 0, "Success", true, 1, "Packet"),
            new("full-empty", Hand.Main, GameMode.Survival, true, null, 0, "Success", false, 1, "Packet"),
            new("hungry-default-stick", Hand.Main, GameMode.Survival, false, "stick", 0, "Success", true, 1, "Packet"),
            new("full-default-stick", Hand.Main, GameMode.Survival, true, "stick", 0, "Success", false, 1, "Packet"),
            new("hungry-final-bite", Hand.Main, GameMode.Survival, false, null, 6, "Success", true, 1, "Packet"),
            new("invulnerable-hungry-empty", Hand.Main, GameMode.Creative, false, null, 0, "Success", false, 1, "Packet"),
            new("invulnerable-full-empty", Hand.Main, GameMode.Creative, true, null, 0, "Success", false, 1, "Packet"),
        ],
        315 =>
        [
            new("hungry-empty", Hand.Main, GameMode.Survival, false, null, 0, "Success", true, 1, "Packet"),
            new("full-empty", Hand.Main, GameMode.Survival, true, null, 0, "Success", false, 1, "Packet"),
            new("hungry-default-stick", Hand.Main, GameMode.Survival, false, "stick", 0, "Success", true, 1, "Packet"),
            new("full-default-stick", Hand.Main, GameMode.Survival, true, "stick", 0, "Pass", false, 0, "None"),
            new("hungry-final-bite", Hand.Main, GameMode.Survival, false, null, 6, "Success", true, 1, "Packet"),
            new("invulnerable-hungry-empty", Hand.Main, GameMode.Creative, false, null, 0, "Success", false, 1, "Packet"),
            new("invulnerable-full-empty", Hand.Main, GameMode.Creative, true, null, 0, "Success", false, 1, "Packet"),
        ],
        573 =>
        [
            new("hungry-empty", Hand.Main, GameMode.Survival, false, null, 0, "Success", true, 1, "Packet"),
            new("full-empty", Hand.Main, GameMode.Survival, true, null, 0, "Consume", false, 0, "None"),
            new("hungry-default-stick", Hand.Main, GameMode.Survival, false, "stick", 0, "Success", true, 1, "Packet"),
            new("full-default-stick", Hand.Main, GameMode.Survival, true, "stick", 0, "Pass", false, 0, "None"),
            new("hungry-final-bite", Hand.Main, GameMode.Survival, false, null, 6, "Success", true, 1, "Packet"),
            new("invulnerable-hungry-empty", Hand.Main, GameMode.Creative, false, null, 0, "Success", true, 1, "Packet"),
            new("invulnerable-full-empty", Hand.Main, GameMode.Creative, true, null, 0, "Success", true, 1, "Packet"),
        ],
        765 =>
        [
            new("hungry-empty", Hand.Main, GameMode.Survival, false, null, 0, "Success", true, 1, "Packet"),
            new("full-empty", Hand.Main, GameMode.Survival, true, null, 0, "Consume", false, 0, "None"),
            new("hungry-default-stick", Hand.Main, GameMode.Survival, false, "stick", 0, "Success", true, 1, "Packet"),
            new("full-default-stick", Hand.Main, GameMode.Survival, true, "stick", 0, "Pass", false, 0, "None"),
            new("hungry-final-bite", Hand.Main, GameMode.Survival, false, null, 6, "Success", true, 1, "Packet"),
            new("invulnerable-hungry-empty", Hand.Main, GameMode.Creative, false, null, 0, "Success", true, 1, "Packet"),
            new("invulnerable-full-empty", Hand.Main, GameMode.Creative, true, null, 0, "Success", true, 1, "Packet"),
            new("hungry-offhand-empty", Hand.Off, GameMode.Survival, false, null, 0, "Success", true, 1, "Packet"),
        ],
        766 =>
        [
            new("hungry-empty", Hand.Main, GameMode.Survival, false, null, 0, "Success", true, 1, "Packet"),
            new("full-empty", Hand.Main, GameMode.Survival, true, null, 0, "Consume", false, 0, "None"),
            new("hungry-default-stick", Hand.Main, GameMode.Survival, false, "stick", 0, "Success", true, 1, "Packet"),
            new("full-default-stick", Hand.Main, GameMode.Survival, true, "stick", 0, "Pass", false, 0, "None"),
            new("hungry-final-bite", Hand.Main, GameMode.Survival, false, null, 6, "Success", true, 1, "Packet"),
            new("invulnerable-hungry-empty", Hand.Main, GameMode.Creative, false, null, 0, "Success", true, 1, "Packet"),
            new("invulnerable-full-empty", Hand.Main, GameMode.Creative, true, null, 0, "Success", true, 1, "Packet"),
            new("hungry-offhand-empty", Hand.Off, GameMode.Survival, false, null, 0, "Pass", false, 0, "None"),
        ],
        777 =>
        [
            new("hungry-empty", Hand.Main, GameMode.Survival, false, null, 0, "Success", true, 0, "Local"),
            new("full-empty", Hand.Main, GameMode.Survival, true, null, 0, "Consume", false, 0, "None"),
            new("hungry-default-stick", Hand.Main, GameMode.Survival, false, "stick", 0, "Success", true, 0, "Local"),
            new("full-default-stick", Hand.Main, GameMode.Survival, true, "stick", 0, "Pass", false, 0, "None"),
            new("hungry-final-bite", Hand.Main, GameMode.Survival, false, null, 6, "Success", true, 0, "Local"),
            new("invulnerable-hungry-empty", Hand.Main, GameMode.Creative, false, null, 0, "Success", true, 0, "Local"),
            new("invulnerable-full-empty", Hand.Main, GameMode.Creative, true, null, 0, "Success", true, 0, "Local"),
            new("hungry-offhand-empty", Hand.Off, GameMode.Survival, false, null, 0, "Pass", false, 0, "None"),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    private static (int Food, int SaturationBits) ExpectedNativeCakeNutrition(int food, float saturation, bool eats)
    {
        Assert.InRange(food, 0, 20);
        Assert.True(float.IsFinite(saturation) && saturation >= 0 && saturation <= food);
        if (!eats)
            return (food, BitConverter.SingleToInt32Bits(saturation));
        int nextFood = Math.Min(food + 2, 20);
        float nextSaturation = Math.Min(saturation + 2 * .1F * 2.0F, nextFood);
        return (nextFood, BitConverter.SingleToInt32Bits(nextSaturation));
    }

    private static string NativeCakeNutritionPredicate(int protocol, int food, float saturation)
    {
        Assert.InRange(food, 0, 20);
        Assert.True(float.IsFinite(saturation) && saturation >= 0 && saturation <= food);
        string data = "{foodLevel:" + food.ToString(CultureInfo.InvariantCulture) + ",foodSaturationLevel:" + saturation.ToString("R", CultureInfo.InvariantCulture) + "f}";
        return protocol < 393 ? $"testfor @a[name={Username}] {data}" : $"execute if entity @a[name={Username},nbt={data}]";
    }

    private static async Task<(CapturedFrame Witness, int? Sequence)> NativeCakeProcessingWitnessAsync(Capture capture, int mark, int protocol, BlockPos target, Direction face, int expectedTarget, int expectedNeighbor, int previousSequence, CancellationToken ct)
    {
        await WaitAsync(() => capture.Since(mark).Any(frame => frame.Flow == PacketFlow.Serverbound && frame.Packet is ServerboundLegacyBlockPlacePacket or ServerboundUseItemOnPacket), "one native Cake use", ct);
        CapturedFrame use = Assert.Single(capture.Since(mark), frame => frame.Flow == PacketFlow.Serverbound && frame.Packet is ServerboundLegacyBlockPlacePacket or ServerboundUseItemOnPacket);
        if (protocol >= 759)
        {
            int sequence = Assert.IsType<ServerboundUseItemOnPacket>(use.Packet).Sequence;
            Assert.True(sequence > previousSequence);
            await WaitAsync(() => capture.Since(mark).Any(frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == sequence), "native Cake use acknowledgement", ct);
            CapturedFrame ack = Assert.Single(capture.Since(mark), frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockChangedAckPacket packet && packet.Sequence == sequence);
            Assert.True(ack.Milliseconds >= use.Milliseconds);
            return (ack, sequence);
        }
        await WaitAsync(() => Replies().Length > 0, "native clicked and adjacent Cake replies", ct);
        (CapturedFrame first, CapturedFrame second) = Assert.Single(Replies());
        Assert.True(first.Milliseconds >= use.Milliseconds && second.Milliseconds >= first.Milliseconds);
        return (second, null);

        (CapturedFrame First, CapturedFrame Second)[] Replies()
        {
            CapturedFrame[] inbound = capture.Since(mark).Where(frame => frame.Flow == PacketFlow.Clientbound && frame.Milliseconds >= use.Milliseconds).ToArray();
            BlockPos neighbor = target.Offset(face);
            return Enumerable.Range(1, Math.Max(0, inbound.Length - 1))
                .Where(index => inbound[index - 1].Packet is ClientboundBlockUpdatePacket first && first.Position == target && first.BlockStateId == expectedTarget && inbound[index].Packet is ClientboundBlockUpdatePacket second && second.Position == neighbor && second.BlockStateId == expectedNeighbor)
                .Select(index => (inbound[index - 1], inbound[index])).ToArray();
        }
    }

    private static int NativeCakeHealthAndAbilitiesRoundTrips(Capture capture, JavaVersion version, Umpk.Game.Registries.RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        int health = 0;
        int abilities = 0;
        foreach (CapturedFrame frame in capture.Frames)
        {
            if (frame.Flow != PacketFlow.Clientbound || frame.Packet is not (ClientboundSetHealthPacket or ClientboundPlayerAbilitiesPacket))
                continue;
            PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            Assert.Equal(frame.Packet, decoded);
            var bytes = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(bytes);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(frame.Payload, bytes.WrittenSpan.ToArray());
            health += decoded is ClientboundSetHealthPacket ? 1 : 0;
            abilities += decoded is ClientboundPlayerAbilitiesPacket ? 1 : 0;
        }
        Assert.True(health > 0 && abilities > 0, "Native Cake acceptance requires actual health and abilities packets.");
        return health + abilities;
    }
}
