using Umpk.Game.Entities;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>
/// Wire serializers for the entity-metadata value shapes that carry structured payloads beyond the scalar/holder set: the particle serializers and the 26.x <c>RESOLVABLE_PROFILE</c> serializer.
/// <para><see cref="ReadParticles"/> and <see cref="WriteParticles"/> are wired on every era whose particle option-shape table exists, from 1.21.2 upward. Structured decoding is required because a raw tail would swallow every later metadata entry. <see cref="ReadResolvableProfile"/> stays structural-only for now (see <see cref="ModernMetadataTable.NeedsRawTail"/>).</para>
/// </summary>
/// <remarks>
/// Version 26.2 uses these wire shapes:
/// <list type="bullet">
/// <item>Particles: VarInt count, then each particle.</item>
/// <item>Resolvable profile: resolved-or-partial profile, then a player-skin patch.</item>
/// <item>Game profile: UUID, player name, and a property list.</item>
/// <item>Each property carries name, value, and an optional signature.</item>
/// <item>Resource texture: one identifier and a slim-model bool.</item>
/// </list>
/// </remarks>
internal static class MetadataExtraCodecs
{
    /// <summary>Reads the <c>PARTICLES</c> value: a VarInt count then each particle through the era particle codec. Each decoded particle is boxed into the opaque list <see cref="MetadataValue.Particles"/> carries.</summary>
    internal static MetadataValue ReadParticles(
        ref PacketReader r,
        IReadOnlyDictionary<int, ParticleOptionShape> shapes,
        ItemComponentTable icons,
        PacketCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(context);
        int count = r.ReadVarInt();
        if (count < 0 || count > r.Remaining + 1)
            throw new ProtocolViolationException($"Particle-list count {count} is implausible for {r.Remaining} remaining bytes.");

        var particles = new object[count];
        for (int i = 0; i < count; i++)
            particles[i] = ParticleCodec.ReadModern(ref r, shapes, icons, context);

        return MetadataValue.Particles(particles);
    }

    /// <summary>Writes a <c>PARTICLES</c> value mirroring <see cref="ReadParticles"/>.</summary>
    internal static void WriteParticles(ref PacketWriter w, MetadataValue value)
    {
        IReadOnlyList<object> particles = value.AsParticles();
        w.WriteVarInt(particles.Count);
        foreach (object particle in particles)
            ParticleCodec.WriteModern(ref w, (ParticleData)particle);

    }

    /// <summary>Reads the <c>RESOLVABLE_PROFILE</c> value: an <c>either</c> (bool true = resolved game profile, false = partial), then a player-skin patch.</summary>
    internal static MetadataValue ReadResolvableProfile(ref PacketReader r)
    {
        bool resolved = r.ReadBool();
        Guid? id;
        string? name;
        IReadOnlyList<Umpk.Game.Entities.ProfileProperty> properties;
        if (resolved)
        {
            // GAME_PROFILE = UUID, PLAYER_NAME, PropertyMap.
            id = r.ReadUuid();
            name = r.ReadString();
            properties = ReadPropertyMap(ref r);
        }
        else
        {
            // Partial = optional PLAYER_NAME, optional UUID, PropertyMap.
            name = r.ReadBool() ? r.ReadString() : null;
            id = r.ReadBool() ? r.ReadUuid() : null;
            properties = ReadPropertyMap(ref r);
        }

        ResolvableSkinPatch skin = ReadSkinPatch(ref r);
        return MetadataValue.ResolvableProfile(new ResolvableProfile(resolved, id, name, properties, skin));
    }

    /// <summary>Writes a <c>RESOLVABLE_PROFILE</c> value mirroring <see cref="ReadResolvableProfile"/>.</summary>
    internal static void WriteResolvableProfile(ref PacketWriter w, MetadataValue value)
    {
        ResolvableProfile profile = value.AsResolvableProfile();
        w.WriteBool(profile.IsResolved);
        if (profile.IsResolved)
        {
            w.WriteUuid(profile.Id ?? throw new ProtocolViolationException("A resolved profile requires a UUID."));
            w.WriteString(profile.Name ?? throw new ProtocolViolationException("A resolved profile requires a name."));
            WritePropertyMap(ref w, profile.Properties);
        }
        else
        {
            w.WriteBool(profile.Name is not null);
            if (profile.Name is not null)
                w.WriteString(profile.Name);

            w.WriteBool(profile.Id.HasValue);
            if (profile.Id is { } id)
                w.WriteUuid(id);

            WritePropertyMap(ref w, profile.Properties);
        }

        WriteSkinPatch(ref w, profile.Skin);
    }

    private static IReadOnlyList<Umpk.Game.Entities.ProfileProperty> ReadPropertyMap(ref PacketReader r)
    {
        int count = r.ReadVarInt();
        if (count < 0 || count > r.Remaining + 1)
            throw new ProtocolViolationException($"Property-map count {count} is implausible for {r.Remaining} remaining bytes.");

        var properties = new Umpk.Game.Entities.ProfileProperty[count];
        for (int i = 0; i < count; i++)
        {
            string name = r.ReadString();
            string val = r.ReadString();
            string? signature = r.ReadBool() ? r.ReadString() : null;
            properties[i] = new Umpk.Game.Entities.ProfileProperty(name, val, signature);
        }

        return properties;
    }

    private static void WritePropertyMap(ref PacketWriter w, IReadOnlyList<Umpk.Game.Entities.ProfileProperty> properties)
    {
        w.WriteVarInt(properties.Count);
        foreach (Umpk.Game.Entities.ProfileProperty property in properties)
        {
            w.WriteString(property.Name);
            w.WriteString(property.Value);
            w.WriteBool(property.Signature is not null);
            if (property.Signature is not null)
                w.WriteString(property.Signature);

        }
    }

    private static ResolvableSkinPatch ReadSkinPatch(ref PacketReader r)
    {
        Identifier? body = r.ReadBool() ? Identifier.Parse(r.ReadString()) : null;
        Identifier? cape = r.ReadBool() ? Identifier.Parse(r.ReadString()) : null;
        Identifier? elytra = r.ReadBool() ? Identifier.Parse(r.ReadString()) : null;
        bool? slim = r.ReadBool() ? r.ReadBool() : null;
        return new ResolvableSkinPatch(body, cape, elytra, slim);
    }

    private static void WriteSkinPatch(ref PacketWriter w, ResolvableSkinPatch skin)
    {
        WriteOptionalIdentifier(ref w, skin.Body);
        WriteOptionalIdentifier(ref w, skin.Cape);
        WriteOptionalIdentifier(ref w, skin.Elytra);
        w.WriteBool(skin.SlimModel.HasValue);
        if (skin.SlimModel is { } slim)
            w.WriteBool(slim);

    }

    private static void WriteOptionalIdentifier(ref PacketWriter w, Identifier? id)
    {
        w.WriteBool(id.HasValue);
        if (id is { } present)
            w.WriteString(present.ToString());

    }
}
