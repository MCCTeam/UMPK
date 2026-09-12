using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The holder-or-inline sound-event reference used by the modern sound packets and the modern explosion. On the wire it is a VarInt: 0 means an inline <c>SoundEvent</c> (a resource-location string plus an optional fixed range float) follows; a value <c>n</c> means the registry id <c>n - 1</c>. Reusable by any codec that carries a <see cref="SoundEventHolder"/> (the explosion and both sound packets use it).</summary>
public static class SoundCodec
{
    /// <summary>Reads a holder-or-inline sound event.</summary>
    public static SoundEventHolder ReadHolder(ref PacketReader r)
    {
        int idPlusOne = r.ReadVarInt();
        if (idPlusOne != 0)
            return new SoundEventHolder(idPlusOne - 1, InlineName: null, FixedRange: null);

        string name = r.ReadString();
        float? fixedRange = r.ReadOptionalStruct(static (ref PacketReader sr) => sr.ReadFloat());
        return new SoundEventHolder(SoundId: -1, name, fixedRange);
    }

    /// <summary>Writes a holder-or-inline sound event.</summary>
    public static void WriteHolder(ref PacketWriter w, SoundEventHolder holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        if (holder.SoundId >= 0)
        {
            w.WriteVarInt(holder.SoundId + 1);
            return;
        }

        w.WriteVarInt(0);
        w.WriteString(holder.InlineName ?? throw new ProtocolViolationException("An inline sound event requires a name."));
        w.WriteOptionalStruct(holder.FixedRange, static (ref PacketWriter ww, float v) => ww.WriteFloat(v));
    }
}
