using System.Globalization;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Original 26.1–26.3 STREAM_CODEC bytes and decoded double bits, measured for all five named releases. These are codec witnesses, not native input or network acceptance.</summary>
public sealed class Interact775NativeLocationTests
{
    private static readonly string[] s_nativeHex =
    [
        "ac02000000",
        "ac02000001",
        "ac02010000",
        "ac02010001",
        "ac0200f9ff7ffffff900",
        "ac0200f9ff7ffffff901",
        "ac0201f9ff7ffffff900",
        "ac0201f9ff7ffffff901",
        "ac0200fa3f5fffaffa00",
        "ac0200fa3f5fffaffa01",
        "ac0201fa3f5fffaffa00",
        "ac0201fa3f5fffaffa01",
        "ac02002db38332cccb0100",
        "ac02002db38332cccb0101",
        "ac02012db38332cccb0100",
        "ac02012db38332cccb0101",
        "ac02000000",
        "ac02000001",
        "ac02010000",
        "ac02010001",
        "ac020001007ffefffe00",
        "ac020001007ffefffe01",
        "ac020101007ffefffe00",
        "ac020101007ffefffe01",
        "ac0200f7ff7ffe0003ffffffff0f00",
        "ac0200f7ff7ffe0003ffffffff0f01",
        "ac0201f7ff7ffe0003ffffffff0f00",
        "ac0201f7ff7ffe0003ffffffff0f01",
        "ac0200ffff0001fff9ffffffff0f00",
        "ac0200ffff0001fff9ffffffff0f01",
        "ac0201ffff0001fff9ffffffff0f00",
        "ac0201ffff0001fff9ffffffff0f01",
    ];

    private static readonly (string X, string Y, string Z)[] s_decodedBits =
    [
        ("0", "0", "0"),
        ("0", "3ff0000000000000", "0"),
        ("3fd0004001000400", "3ff5ffd7ff5ffd80", "bfe0004001000400"),
        ("4011002400900240", "bff0008002000801", "3fc0044011004410"),
        ("0", "0", "0"),
        ("3f10004001000000", "0", "0"),
        ("420ffffffff80000", "c20ffffffff80000", "0"),
        ("0", "420ffffffff80000", "c20ffffffff80000"),
    ];

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(2, 1, false)]
    [InlineData(2, 1, true)]
    [InlineData(3, 0, false)]
    [InlineData(3, 0, true)]
    [InlineData(3, 1, false)]
    [InlineData(3, 1, true)]
    [InlineData(4, 0, false)]
    [InlineData(4, 0, true)]
    [InlineData(4, 1, false)]
    [InlineData(4, 1, true)]
    [InlineData(5, 0, false)]
    [InlineData(5, 0, true)]
    [InlineData(5, 1, false)]
    [InlineData(5, 1, true)]
    [InlineData(6, 0, false)]
    [InlineData(6, 0, true)]
    [InlineData(6, 1, false)]
    [InlineData(6, 1, true)]
    [InlineData(7, 0, false)]
    [InlineData(7, 0, true)]
    [InlineData(7, 1, false)]
    [InlineData(7, 1, true)]
    public void MatchesNativeLpBytesAndDecodedBits(int vector, int hand, bool secondary)
    {
        Vec3d location = vector switch
        {
            0 => Vec3d.Zero,
            1 => new(0, 1, 0),
            2 => new(.25, 1.375, -.5),
            3 => new(4.25, -1, .125),
            4 => new(Math.ScaleB(1, -16), 0, 0),
            5 => new(3.051944088384301E-5, 0, 0),
            6 => new(1.7179869183E10, -1.7179869183E10, 0),
            7 => new(double.NaN, double.PositiveInfinity, double.NegativeInfinity),
            _ => throw new ArgumentOutOfRangeException(nameof(vector)),
        };
        byte[] expected = Convert.FromHexString(s_nativeHex[vector * 4 + hand * 2 + (secondary ? 1 : 0)]);
        var packet = new ServerboundInteractPacket(300, 0, hand, location, secondary);
        foreach (int protocol in new[] { 775, 776, 777 })
        {
            BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "interact");
            Assert.Equal(expected, bound.Encode(packet));
            var decoded = Assert.IsType<ServerboundInteractPacket>(bound.DecodeFrame(expected));
            Assert.Equal(300, decoded.EntityId);
            Assert.Equal(0, decoded.Action);
            Assert.Equal(hand, decoded.Hand);
            Assert.Equal(secondary, decoded.UsingSecondaryAction);
            Vec3d value = Assert.IsType<Vec3d>(decoded.InteractAt);
            Assert.Equal(s_decodedBits[vector].X, Bits(value.X));
            Assert.Equal(s_decodedBits[vector].Y, Bits(value.Y));
            Assert.Equal(s_decodedBits[vector].Z, Bits(value.Z));
            Assert.Equal(expected, bound.Encode(decoded));
        }

        static string Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("x", CultureInfo.InvariantCulture);
    }
}
