using System.Collections.Frozen;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

internal sealed partial class JavaBlockInteractions
{
    private readonly GeometryTables _geometry;

    private sealed record GeometryTables(FrozenDictionary<Identifier, EntityGeometryRule> Rules, FrozenDictionary<(Identifier Type, bool Selector, EntityPose Pose), EntityGeometryDimensions> Dimensions)
    {
        public static GeometryTables Empty { get; } = new(FrozenDictionary<Identifier, EntityGeometryRule>.Empty, FrozenDictionary<(Identifier, bool, EntityPose), EntityGeometryDimensions>.Empty);
    }

    public bool TryGetRule(Identifier entityType, out EntityGeometryRule rule) => _geometry.Rules.TryGetValue(entityType, out rule);

    public bool TryGetDimensions(Identifier entityType, bool selector, EntityPose pose, out EntityGeometryDimensions dimensions) => _geometry.Dimensions.TryGetValue((entityType, selector, pose), out dimensions);

    private static GeometryTables ReadGeometry(ref PacketReader reader)
    {
        Dictionary<Identifier, EntityGeometryRule> rules = [];
        Dictionary<(Identifier, bool, EntityPose), EntityGeometryDimensions> dimensions = [];
        int count = Count(ref reader, 8);
        for (int index = 0; index < count; index++)
        {
            Identifier name = Identifier.Parse(reader.ReadString());
            int metadata = reader.ReadVarInt(), poseIndex = reader.ReadVarInt();
            byte refresh = reader.ReadByte();
            string scale = reader.ReadString();
            if (metadata is < 0 or > 254 || poseIndex is < -1 or > 254 || metadata == poseIndex || refresh > 2 || scale is not ("" or "minecraft:scale") || !rules.TryAdd(name, new EntityGeometryRule(metadata, poseIndex, (EntityDimensionsRefresh)refresh, scale.Length == 0 ? null : Identifier.Parse(scale))))
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
            if (!poses.Contains(EntityPose.Standing) || !seen.SetEquals(poses.SelectMany(pose => new[] { (false, pose), (true, pose) })) || (poseIndex == -1 && poses.Count != 1))
                throw new ProtocolViolationException("Generated entity geometry has incomplete selector or pose coverage.");
        }
        return new GeometryTables(rules.ToFrozenDictionary(), dimensions.ToFrozenDictionary());
    }
}
