namespace Umpk.Game.Items.Components;

/// <summary>The piercing attack behavior (<c>minecraft:piercing_weapon</c>), introduced in 1.21.11.</summary>
/// <param name="DealsKnockback">Whether a successful piercing hit can knock the target back.</param>
/// <param name="Dismounts">Whether a successful piercing hit can dismount its target.</param>
/// <param name="Sound">The optional attack sound holder.</param>
/// <param name="HitSound">The optional successful-hit sound holder.</param>
public sealed record PiercingWeaponComponent(bool DealsKnockback = true, bool Dismounts = false, SoundEventRef? Sound = null, SoundEventRef? HitSound = null)
{
    /// <inheritdoc/>
    public bool Equals(PiercingWeaponComponent? other) => other is not null
        && DealsKnockback == other.DealsKnockback && Dismounts == other.Dismounts
        && SameSound(Sound, other.Sound) && SameSound(HitSound, other.HitSound);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(DealsKnockback, Dismounts, SoundHash(Sound), SoundHash(HitSound));

    private static bool SameSound(SoundEventRef? left, SoundEventRef? right) => ReferenceEquals(left, right)
        || left is not null && right is not null && left.RegistryId == right.RegistryId && left.SoundId == right.SoundId
        && FloatBits(left.FixedRange) == FloatBits(right.FixedRange);

    private static int SoundHash(SoundEventRef? sound) => sound is null ? 0 : HashCode.Combine(sound.RegistryId, sound.SoundId, FloatBits(sound.FixedRange));

    // Java's record equality distinguishes signed zero and canonicalizes NaNs inside the optional sound range.
    private static int? FloatBits(float? value) => value is not { } number ? null : float.IsNaN(number) ? 0x7FC00000 : BitConverter.SingleToInt32Bits(number);
}
