using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class ItemComponentCodecs
{
    /// <summary><c>minecraft:piercing_weapon</c> from 1.21.11: two booleans followed by two optional sound holders.</summary>
    public static ItemComponentCodec PiercingWeapon { get; } = new PiercingWeaponCodecImpl();

    private static readonly Identifier PiercingSoundRegistry = Identifier.Minecraft("sound_event");

    private static void ValidatePiercingSound(SoundEventRef sound, PacketCodecContext context)
    {
        if (sound.RegistryId is { } id)
        {
            if (id < 0 || id == int.MaxValue || sound.SoundId is not null || sound.FixedRange is not null)
                throw new ProtocolViolationException("A sound holder requires an encodable registry id or an inline sound.");
            if (context.Registries.TryGetRegistry(PiercingSoundRegistry, out IRegistry registry) && !registry.ContainsNetworkId(id))
                throw new ProtocolViolationException($"Sound holder {id} is outside the sound-event registry.");
        }
        else if (sound.SoundId is null)
            throw new ProtocolViolationException("An inline sound holder requires its sound identifier.");
    }

    private static int HashPiercingSound(in HashOps ops, SoundEventRef sound, PacketCodecContext context)
    {
        ValidatePiercingSound(sound, context);
        if (sound.RegistryId is { } id)
        {
            if (!context.Registries.TryGetRegistry(PiercingSoundRegistry, out IRegistry registry) || !registry.TryGetKey(id, out Identifier name))
                throw new ProtocolViolationException($"Sound holder {id} requires its registry name for component hashing.");
            return ops.String(name.ToString());
        }
        List<(int Key, int Value)> fields = [(ops.String("sound_id"), ops.String(sound.SoundId!.Value.ToString()))];
        if (sound.FixedRange is { } range)
            fields.Add((ops.String("range"), ops.Float(range)));
        return ops.Map(fields);
    }

    private sealed class PiercingWeaponCodecImpl() : ItemComponentCodec(DataComponents.PiercingWeapon)
    {
        public override object Decode(ref PacketReader reader, PacketCodecContext context)
        {
            bool knockback = reader.ReadBool();
            bool dismounts = reader.ReadBool();
            SoundEventRef? sound = reader.ReadBool() ? ReadSoundEvent(ref reader) : null;
            SoundEventRef? hitSound = reader.ReadBool() ? ReadSoundEvent(ref reader) : null;
            if (sound is not null)
                ValidatePiercingSound(sound, context);
            if (hitSound is not null)
                ValidatePiercingSound(hitSound, context);
            return new PiercingWeaponComponent(knockback, dismounts, sound, hitSound);
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var piercing = (PiercingWeaponComponent)value;
            if (piercing.Sound is not null)
                ValidatePiercingSound(piercing.Sound, context);
            if (piercing.HitSound is not null)
                ValidatePiercingSound(piercing.HitSound, context);
            writer.WriteBool(piercing.DealsKnockback);
            writer.WriteBool(piercing.Dismounts);
            writer.WriteOptional(piercing.Sound, WriteSoundEvent);
            writer.WriteOptional(piercing.HitSound, WriteSoundEvent);
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var piercing = (PiercingWeaponComponent)value;
            List<(int Key, int Value)> fields = [];
            if (!piercing.DealsKnockback)
                fields.Add((ops.String("deals_knockback"), ops.Boolean(false)));
            if (piercing.Dismounts)
                fields.Add((ops.String("dismounts"), ops.Boolean(true)));
            if (piercing.Sound is not null)
                fields.Add((ops.String("sound"), HashPiercingSound(ops, piercing.Sound, context)));
            if (piercing.HitSound is not null)
                fields.Add((ops.String("hit_sound"), HashPiercingSound(ops, piercing.HitSound, context)));
            return ops.Map(fields);
        }
    }
}
