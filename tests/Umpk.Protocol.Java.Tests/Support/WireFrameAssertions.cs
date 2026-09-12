using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Support;

internal static class WireFrameAssertions
{
    internal static void DoesNotRoundTrip(BoundPacketCodec bound, byte[] foreign)
    {
        object decoded;
        try
        {
            decoded = bound.DecodeFrame(foreign);
        }
        catch (ProtocolViolationException)
        {
            return;
        }

        byte[] reencoded;
        try
        {
            reencoded = bound.Encode(decoded);
        }
        catch (ProtocolViolationException)
        {
            return;
        }

        Assert.NotEqual(foreign, reencoded);
    }

    internal sealed class FrameWriter
    {
        private readonly List<byte> _bytes = [];
        public void U8(int value) => _bytes.Add((byte)value);
        public void VarInt(int value)
        {
            uint v = (uint)value;
            while ((v & ~0x7Fu) != 0)
            {
                _bytes.Add((byte)((v & 0x7F) | 0x80));
                v >>= 7;
            }
            _bytes.Add((byte)v);
        }
        public void LongArray(long value)
        {
            VarInt(1);
            for (int shift = 56; shift >= 0; shift -= 8)
                _bytes.Add((byte)(value >> shift));
        }
        public void Str(string value)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            VarInt(utf8.Length);
            _bytes.AddRange(utf8);
        }
        public void Raw(byte[] value) => _bytes.AddRange(value);
        public byte[] ToArray() => [.. _bytes];
    }
}
