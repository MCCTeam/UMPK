namespace Umpk.Protocol.Java.Codecs;

/// <summary>The modern entity-metadata serializer kinds, one per value shape a protocol can carry. Holder-based variant serializers (cat/cow/wolf/painting/...) are all VarInt registry ids and collapse to <see cref="VarIntHolder"/>. Serializers with no protocol-side codec yet (such as some <see cref="Particle"/> eras) trigger raw-tail capture; see <see cref="EntityMetadataList.RawTail"/>.</summary>
internal enum ModernMetadataSerializer
{
    /// <summary>An unknown serializer id (out of the era's table range).</summary>
    Unknown,
    Byte,
    Int,
    Long,
    Float,
    String,
    Component,
    OptionalComponent,

    /// <summary>An item stack decoded by the era-specific item strategy.</summary>
    ItemStack,
    Boolean,
    Rotations,
    BlockPos,
    OptionalBlockPos,
    Direction,

    /// <summary>An optional entity reference (UUID-based); decodes as an optional UUID.</summary>
    OptionalUuid,
    BlockState,
    OptionalBlockState,
    CompoundTag,

    /// <summary>A single particle: raw-tailed on the live metadata path; see <see cref="MetadataExtraCodecs"/>.</summary>
    Particle,

    /// <summary>A list of particles: raw-tailed on the live metadata path; see <see cref="MetadataExtraCodecs"/>.</summary>
    Particles,

    /// <summary>A resolvable player profile: raw-tailed on the live metadata path; see <see cref="MetadataExtraCodecs"/>.</summary>
    ResolvableProfile,
    VillagerData,
    OptionalUnsignedInt,
    Pose,

    /// <summary>Any Holder-based variant serializer; a plain VarInt registry id.</summary>
    VarIntHolder,
    OptionalGlobalPos,
    Vector3,
    Quaternion,
}

/// <summary>A per-era ordered serializer table mapping the wire serializer id to its <see cref="ModernMetadataSerializer"/> kind. Version-specific ordering is resolved once at codec construction, never on the hot path.</summary>
internal sealed partial class ModernMetadataTable
{
    private readonly ModernMetadataSerializer[] _byId;

    private ModernMetadataTable(ModernMetadataSerializer[] byId)
    {
        _byId = byId;
        ShapeToken = "metadata/" + WireShapeDigest.Of(byId.Select(static (s, i) => $"{i}:{s}"));
    }

    /// <summary>This era's contribution to the wire shape of any codec that reads a metadata list through it. The digest is over the serializer ORDER, which is the whole of what the table decides: an id resolved against a neighbouring era's order reads the wrong width and desynchronizes the rest of the list.</summary>
    internal string ShapeToken { get; }

    /// <summary>Maps a wire serializer id to its kind (<see cref="ModernMetadataSerializer.Unknown"/> if out of range).</summary>
    public ModernMetadataSerializer Resolve(int serializerId) =>
        serializerId >= 0 && serializerId < _byId.Length ? _byId[serializerId] : ModernMetadataSerializer.Unknown;

    /// <summary>Maps a kind back to its wire serializer id for this era.</summary>
    /// <exception cref="ProtocolViolationException">The kind is not present in this era's table.</exception>
    public int SerializerId(ModernMetadataSerializer serializer)
    {
        for (int i = 0; i < _byId.Length; i++)
            if (_byId[i] == serializer)
                return i;

        throw new ProtocolViolationException($"Serializer {serializer} is not present in this metadata era table.");
    }

    /// <summary>Whether the resolved serializer is raw-tailed on the live entity-metadata path.</summary>
    /// <remarks>A raw tail is expensive: it stops the decode dead and swallows the whole REST of the list, so one unmodeled field costs every field behind it as well. Particles are no longer among them wherever the era supplies a particle option-shape table (<paramref name="particlesDecodable"/>), which is 1.21.2 upward, the same span in which the particle-list serializer exists. Below that there is no shape table to decode against and they stay captured. The item-codec seam Item stacks are decoded by an era-specific strategy supplied by the binding. Resolvable profiles and unknown ids remain raw-tailed, which preserves unmodeled serializers frame-exactly.</remarks>
    /// <param name="serializer">The resolved serializer kind.</param>
    /// <param name="particlesDecodable">Whether this decode was given the era's particle option shapes.</param>
    /// <returns>True when the value must be captured verbatim.</returns>
    public static bool NeedsRawTail(ModernMetadataSerializer serializer, bool particlesDecodable) =>
        serializer switch
        {
            ModernMetadataSerializer.Particle or ModernMetadataSerializer.Particles => !particlesDecodable,
            ModernMetadataSerializer.ResolvableProfile
                or ModernMetadataSerializer.Unknown => true,
            _ => false,
        };
}
