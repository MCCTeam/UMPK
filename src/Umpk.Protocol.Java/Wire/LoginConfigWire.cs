using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Shared wire primitives for the login and configuration channel codecs: identifier read/write and the optional (nullable) byte-array payload used by the cookie exchange.</summary>
internal static class LoginConfigWire
{
    internal const int CookieMaxPayloadLength = 5120;
    internal static readonly WriterAction<Identifier> WriteId =
        static (ref PacketWriter w, Identifier id) => w.WriteString(id.ToString());

    internal static readonly ReaderFunc<Identifier> ReadId =
        static (ref PacketReader r) => Identifier.Parse(r.ReadString());

    // Helpers

    internal static void WriteNullableByteArray(ref PacketWriter w, byte[]? payload)
    {
        if (payload is null)
            w.WriteBool(false);

        else
        {
            if (payload.Length > CookieMaxPayloadLength)
                throw new ProtocolViolationException(
                    $"Cookie payload length {payload.Length} exceeds {CookieMaxPayloadLength} bytes.");

            w.WriteBool(true);
            w.WriteByteArray(payload);
        }
    }

    internal static byte[]? ReadNullableByteArray(ref PacketReader r) =>
        r.ReadBool() ? ReadCookieByteArray(ref r) : null;

    internal static void WriteCookieByteArray(ref PacketWriter w, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > CookieMaxPayloadLength)
            throw new ProtocolViolationException(
                $"Cookie payload length {payload.Length} exceeds {CookieMaxPayloadLength} bytes.");
        w.WriteByteArray(payload);
    }

    internal static byte[] ReadCookieByteArray(ref PacketReader r)
    {
        int length = r.ReadVarInt();
        if (length < 0 || length > CookieMaxPayloadLength)
            throw new ProtocolViolationException(
                $"Cookie payload length {length} is outside 0..{CookieMaxPayloadLength} bytes.");
        return r.ReadBytes(length).ToArray();
    }
}
