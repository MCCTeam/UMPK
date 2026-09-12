using System.Buffers;
using Umpk.Game.Entities;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>The 1.8 equipment slot numbering, and the byte/VarInt boundary of <c>set_held_slot</c>.</summary>
/// <remarks>Both mismatches are invisible to ordinary round trips: a symmetric wrong cast agrees with itself, and the held-slot codecs produce the same ONE byte for any slot a real game uses, so even the codec-identity pin's byte-count probe reads 1 on both sides of the boundary. Only asserting the mapped VALUE, and asserting the wire form at a value the two encodings disagree about, can see either.</remarks>
public sealed class EquipmentAndHeldSlotCodecTests
{
    private const string SetEquipment = "minecraft:set_equipment";
    private const string SetHeldSlot = "minecraft:set_held_slot";

    /// <summary>Protocol 47's slot short uses the 1.8 numbering rather than modern slot ordinals.</summary>
    /// <remarks>The mapping is 0 main hand, 1 feet, 2 legs, 3 chest, and 4 head. The modern ordinals insert the off-hand at 1, so a straight cast shifted every armour piece by one: a boot arrived as an off-hand item, a helmet as a chestplate.</remarks>
    [Theory]
    [InlineData(0, EquipmentSlot.MainHand)]
    [InlineData(1, EquipmentSlot.Feet)]
    [InlineData(2, EquipmentSlot.Legs)]
    [InlineData(3, EquipmentSlot.Chest)]
    [InlineData(4, EquipmentSlot.Head)]
    public void LegacyEquipmentSlotNumber_MapsToTheRightModernSlot(int wireSlot, EquipmentSlot expected)
    {
        // entity id 7, slot short, then an empty legacy stack (a short of -1).
        byte[] frame = [7, 0x00, (byte)wireSlot, 0xFF, 0xFF];

        var packet = (ClientboundSetEquipmentPacket)BoundCodec
            .At(47, PacketFlow.Clientbound, SetEquipment)
            .Decode(frame, PacketCodecContext.Registryless);

        Assert.Equal(expected, packet.LegacySlot);

        // The inverse has to agree, or a proxy re-emitting the packet would shift it back.
        Assert.Equal(frame, Encode(47, SetEquipment, packet));
    }

    /// <summary>1.8 never carried an off-hand, so there is no number to send for one.</summary>
    [Fact]
    public void LegacyEquipment_RejectsSlotsProtocol47DoesNotHave()
    {
        var packet = new ClientboundSetEquipmentPacket(7, EquipmentSlot.OffHand, EntityItemSlot.Empty, null);
        Assert.Throws<ProtocolViolationException>(() => Encode(47, SetEquipment, packet));
    }

    /// <summary><c>set_held_slot</c> is a raw byte through 768 and a VarInt from 769.</summary>
    /// <remarks>
    /// The VarInt codec starts at 1.21.4. The 1.21.2 and 1.21.3 frames are one-byte frames, and 1.21.4, 1.21.5 and 1.21.6 are encoded as a VarInt. So the boundary is 769, not 768.
    /// <para>Slot 200 is the discriminating value: a byte encodes it in one, a VarInt in two. Every value a real hotbar uses (0-8) is one byte either way, which is exactly why this sat unnoticed and why the codec-identity pin's byte-count probe could not see the rebinding either.</para>
    /// </remarks>
    [Theory]
    [InlineData(768, 1)]
    [InlineData(769, 2)]
    [InlineData(770, 2)]
    [InlineData(776, 2)]
    public void HeldSlot_IsAByteThroughSevenSixtyEightAndAVarIntFromSevenSixtyNine(int protocol, int expectedLength)
    {
        var packet = new ClientboundSetHeldSlotPacket(200);
        byte[] frame = Encode(protocol, SetHeldSlot, packet);
        Assert.Equal(expectedLength, frame.Length);

        var decoded = (ClientboundSetHeldSlotPacket)BoundCodec
            .At(protocol, PacketFlow.Clientbound, SetHeldSlot)
            .Decode(frame, PacketCodecContext.Registryless);
        Assert.Equal(200, decoded.Slot);
    }

    /// <summary>The 1.8 codec runs unchanged over the whole 47-768 span, so both ends agree.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(767)]
    [InlineData(768)]
    public void HeldSlot_ByteFormSpansTheWholeLegacyBand(int protocol)
    {
        byte[] frame = Encode(protocol, SetHeldSlot, new ClientboundSetHeldSlotPacket(200));
        Assert.Single(frame);
        Assert.Equal(200, frame[0]);
    }

    private static byte[] Encode(int protocol, string identifier, IPacket packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        BoundCodec.At(protocol, PacketFlow.Clientbound, identifier)
            .Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }
}
