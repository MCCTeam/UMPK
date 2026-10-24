using System.Collections.Frozen;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

public static partial class JavaGameData
{
    private static FrozenDictionary<Identifier, IReadOnlyDictionary<DataComponentType, object>> ReadItemAttackDefaults(int protocol)
    {
        Dictionary<Identifier, IReadOnlyDictionary<DataComponentType, object>> result = [];
        ReadOnlySpan<byte> bytes = ItemAttackDefaults(protocol);
        if (bytes.IsEmpty)
            return result.ToFrozenDictionary();
        var reader = new PacketReader(bytes);
        if (reader.ReadVarInt() != 1)
            throw new ProtocolViolationException("Unsupported generated item attack table schema.");
        int count = reader.ReadVarInt();
        if (count < 0 || count > reader.Remaining)
            throw new ProtocolViolationException("Invalid generated item attack count.");
        for (int index = 0; index < count; index++)
        {
            Identifier item = Identifier.Parse(reader.ReadString());
            Dictionary<DataComponentType, object> components = [];
            if (reader.ReadBool())
            {
                bool knockback = reader.ReadBool();
                bool dismounts = reader.ReadBool();
                SoundEventRef? sound = ReadAttackSound(ref reader);
                SoundEventRef? hitSound = ReadAttackSound(ref reader);
                components.Add(DataComponents.PiercingWeapon, new PiercingWeaponComponent(knockback, dismounts, sound, hitSound));
            }
            if (reader.ReadBool())
                components.Add(DataComponents.MinimumAttackCharge, new MinimumAttackChargeComponent(reader.ReadFloat()));
            result.Add(item, components.ToFrozenDictionary());
        }
        if (reader.Remaining != 0)
            throw new ProtocolViolationException("Trailing bytes in generated item attack defaults.");
        return result.ToFrozenDictionary();
    }

    private static SoundEventRef? ReadAttackSound(ref PacketReader reader)
    {
        if (!reader.ReadBool())
            return null;
        int id = reader.ReadVarInt();
        if (id < 0 || id == int.MaxValue)
            throw new ProtocolViolationException("Invalid generated attack sound holder.");
        return new SoundEventRef(id);
    }
}
