using System.Collections.Frozen;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

internal sealed class JavaEntityGeometry : IEntityGeometrySource
{
    private readonly FrozenDictionary<Identifier, EntityGeometryRule> _rules;
    private readonly FrozenDictionary<(Identifier Type, bool Selector, EntityPose Pose), EntityGeometryDimensions> _dimensions;

    private JavaEntityGeometry(FrozenDictionary<Identifier, EntityGeometryRule> rules, FrozenDictionary<(Identifier, bool, EntityPose), EntityGeometryDimensions> dimensions)
    {
        _rules = rules;
        _dimensions = dimensions;
    }

    internal static JavaEntityGeometry Empty { get; } = new(FrozenDictionary<Identifier, EntityGeometryRule>.Empty, FrozenDictionary<(Identifier, bool, EntityPose), EntityGeometryDimensions>.Empty);
    internal bool HasData => _rules.Count != 0;

    public bool TryGetRule(Identifier entityType, out EntityGeometryRule rule) => _rules.TryGetValue(entityType, out rule);

    public bool TryGetDimensions(Identifier entityType, bool selector, EntityPose pose, out EntityGeometryDimensions dimensions) => _dimensions.TryGetValue((entityType, selector, pose), out dimensions);

    internal static JavaEntityGeometry Create(ReadOnlySpan<byte> table)
    {
        var reader = new PacketReader(table);
        int schema = reader.ReadVarInt();
        if (schema is not (1 or 2))
            throw new ProtocolViolationException("Unsupported generated entity geometry schema.");
        JavaEntityGeometry geometry = Read(ref reader, schema == 2);
        if (reader.Remaining != 0)
            throw new ProtocolViolationException("Generated entity geometry table has trailing bytes.");
        return geometry;
    }

    internal static JavaEntityGeometry Read(ref PacketReader reader, bool hasTickPolicy = false)
    {
        Dictionary<Identifier, EntityGeometryRule> rules = [];
        Dictionary<(Identifier, bool, EntityPose), EntityGeometryDimensions> dimensions = [];
        int count = Count(ref reader, hasTickPolicy ? 11 : 8);
        for (int index = 0; index < count; index++)
        {
            Identifier name = Identifier.Parse(reader.ReadString());
            int metadata = reader.ReadVarInt(), poseIndex = reader.ReadVarInt();
            byte refresh = reader.ReadByte();
            byte selectorKind = hasTickPolicy ? reader.ReadByte() : metadata < 0 ? (byte)0 : (byte)1;
            byte timing = hasTickPolicy ? reader.ReadByte() : (byte)0;
            int loadedRadius = hasTickPolicy ? reader.ReadVarInt() : 0;
            string scale = reader.ReadString();
            if (metadata is < -1 or > 254 || poseIndex is < -1 or > 254 || (metadata >= 0 && metadata == poseIndex) || refresh > 2 || selectorKind > 2 || (selectorKind == 0) != (metadata == -1) || timing > 1 || loadedRadius is < 0 or > 32 || (timing == 0 && loadedRadius != 0) || scale is not ("" or "minecraft:scale"))
                throw new ProtocolViolationException("Invalid generated entity geometry rule.");
            var rule = new EntityGeometryRule(metadata, poseIndex, (EntityDimensionsRefresh)refresh, scale.Length == 0 ? null : Identifier.Parse(scale))
            {
                SelectorKind = (EntityGeometrySelectorKind)selectorKind,
                RefreshTiming = (EntityGeometryRefreshTiming)timing,
                RequiredLoadedRadius = loadedRadius,
            };
            if (!rules.TryAdd(name, rule))
                throw new ProtocolViolationException("Invalid generated entity geometry rule.");
            int variants = Count(ref reader, 11);
            HashSet<(bool Selector, EntityPose Pose)> seen = [];
            for (int variant = 0; variant < variants; variant++)
            {
                byte selector = reader.ReadByte(), pose = reader.ReadByte();
                float width = reader.ReadFloat(), height = reader.ReadFloat();
                byte flags = reader.ReadByte();
                if (selector > 1 || pose > 17 || flags > 3 || !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0 || !seen.Add((selector == 1, (EntityPose)pose)))
                    throw new ProtocolViolationException("Invalid generated entity geometry dimensions.");
                dimensions.Add((name, selector == 1, (EntityPose)pose), new EntityGeometryDimensions(width, height, (flags & 1) != 0, (flags & 2) != 0));
            }
            HashSet<EntityPose> poses = [.. seen.Select(key => key.Pose)];
            bool complete = metadata == -1
                ? seen.SetEquals(poses.Select(pose => (false, pose)))
                : seen.SetEquals(poses.SelectMany(pose => new[] { (false, pose), (true, pose) }));
            if (!poses.Contains(EntityPose.Standing) || !complete || (poseIndex == -1 && poses.Count != 1))
                throw new ProtocolViolationException("Generated entity geometry has incomplete selector or pose coverage.");
        }
        return new JavaEntityGeometry(rules.ToFrozenDictionary(), dimensions.ToFrozenDictionary());
    }

    private static int Count(ref PacketReader reader, int minimum)
    {
        int count = reader.ReadVarInt();
        if (count < 0 || count > reader.Remaining / minimum)
            throw new ProtocolViolationException("Generated entity geometry count exceeds available rows.");
        return count;
    }
}
