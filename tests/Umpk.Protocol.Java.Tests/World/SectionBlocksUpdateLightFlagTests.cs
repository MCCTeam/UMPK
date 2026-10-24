using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

public sealed class SectionBlocksUpdateLightFlagTests
{
    // Native 1.17 frame 287 contains 81 foundation updates and a true light-suppression flag; capture and source hashes are retained in the external verification receipt.
    private static readonly byte[] Native755Frame = Convert.FromBase64String("///kAABgAAYBUcMowyrDLMMuwzDDMsM0wzbDONMo0yrTLNMu0zDTMtM00zbTOOMo4yrjLOMu4zDjMuM04zbjOPMo8yrzLPMu8zDzMvM08zbzOIMpgyuDLYMvgzGDM4M1gzeDOZMpkyuTLZMvkzGTM5M1kzeTOaMpoyujLaMvozGjM6M1ozejObMpsyuzLbMvszGzM7M1szezOcMpwyvDLcMvwzHDM8M1wzfDOQ==");

    private static readonly short[] NativePositions =
    [
        0x443, 0x543, 0x643, 0x743, 0x843, 0x943, 0xA43, 0xB43, 0xC43,
        0x453, 0x553, 0x653, 0x753, 0x853, 0x953, 0xA53, 0xB53, 0xC53,
        0x463, 0x563, 0x663, 0x763, 0x863, 0x963, 0xA63, 0xB63, 0xC63,
        0x473, 0x573, 0x673, 0x773, 0x873, 0x973, 0xA73, 0xB73, 0xC73,
        0x483, 0x583, 0x683, 0x783, 0x883, 0x983, 0xA83, 0xB83, 0xC83,
        0x493, 0x593, 0x693, 0x793, 0x893, 0x993, 0xA93, 0xB93, 0xC93,
        0x4A3, 0x5A3, 0x6A3, 0x7A3, 0x8A3, 0x9A3, 0xAA3, 0xBA3, 0xCA3,
        0x4B3, 0x5B3, 0x6B3, 0x7B3, 0x8B3, 0x9B3, 0xAB3, 0xBB3, 0xCB3,
        0x4C3, 0x5C3, 0x6C3, 0x7C3, 0x8C3, 0x9C3, 0xAC3, 0xBC3, 0xCC3,
    ];

    [Theory]
    [InlineData(751, false)]
    [InlineData(751, true)]
    [InlineData(753, false)]
    [InlineData(753, true)]
    [InlineData(754, false)]
    [InlineData(754, true)]
    [InlineData(755, false)]
    [InlineData(755, true)]
    [InlineData(756, false)]
    [InlineData(756, true)]
    [InlineData(757, false)]
    [InlineData(757, true)]
    [InlineData(758, false)]
    [InlineData(758, true)]
    [InlineData(759, false)]
    [InlineData(759, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    [InlineData(761, false)]
    [InlineData(761, true)]
    [InlineData(762, false)]
    [InlineData(762, true)]
    public void Bound751Through762_PreservesNativeFieldsAndFlagByte(int protocol, bool suppressLightUpdates)
    {
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:section_blocks_update");
        byte[] payload = (byte[])Native755Frame.Clone();
        // False is the controlled opposite value; true retains the exact captured native body.
        payload[8] = suppressLightUpdates ? (byte)1 : (byte)0;
        var decoded = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(codec.DecodeFrame(payload));
        AssertNativeFields(decoded);
        Assert.Equal(payload, codec.Encode(decoded));
    }

    [Fact]
    public void Bound763_OmitsTheRemovedFlagWithoutChangingUpdates()
    {
        BoundPacketCodec codec = BoundCodec.At(763, PacketFlow.Clientbound, "minecraft:section_blocks_update");
        byte[] payload = [.. Native755Frame.AsSpan(0, 8), .. Native755Frame.AsSpan(9)];
        var decoded = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(codec.DecodeFrame(payload));
        AssertNativeFields(decoded);
        Assert.Equal(payload, codec.Encode(decoded));
    }

    [Fact]
    public void Bound736_RetainsTheLegacyChunkRecordShape()
    {
        BoundPacketCodec codec = BoundCodec.At(736, PacketFlow.Clientbound, "minecraft:chunk_blocks_update");
        byte[] payload = [0xFF, 0xFF, 0xFF, 0xFD, 0, 0, 0, 11, 2, 0x0A, 0x93, 0xD2, 9, 0x0F, 0xFF, 9];
        var decoded = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(codec.DecodeFrame(payload));
        Assert.True(decoded.IsLegacy);
        Assert.Equal(0, decoded.SectionPos);
        Assert.Equal(-3, decoded.LegacyChunkX);
        Assert.Equal(11, decoded.LegacyChunkZ);
        Assert.Equal([new SectionBlockChange(0x0A93, 1234), new SectionBlockChange(0x0FFF, 9)], decoded.Changes);
        Assert.Equal(payload, codec.Encode(decoded));
    }

    private static void AssertNativeFields(ClientboundSectionBlocksUpdatePacket packet)
    {
        Assert.Equal(-30786319286266L, packet.SectionPos);
        Assert.Equal(0, packet.LegacyChunkX);
        Assert.Equal(0, packet.LegacyChunkZ);
        Assert.False(packet.IsLegacy);
        Assert.Equal(81, packet.Changes.Count);
        for (int index = 0; index < NativePositions.Length; index++)
        {
            Assert.Equal(NativePositions[index], packet.Changes[index].PackedPosition);
            Assert.Equal(1, packet.Changes[index].BlockStateId);
        }
    }
}
