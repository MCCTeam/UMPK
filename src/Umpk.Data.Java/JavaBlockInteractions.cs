using System.Collections.Frozen;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

/// <summary>Named native contracts, independent of a server's numeric registry ordering.</summary>
internal sealed partial class JavaBlockInteractions : IBlockInteractionSource, IEntityGeometrySource
{
    private readonly FrozenDictionary<Identifier, BlockInteractionInfo> _blocks;
    private readonly FrozenDictionary<Identifier, ItemUseOnInfo> _items;
    private readonly FrozenDictionary<Identifier, bool> _entities;
    private readonly FrozenDictionary<Identifier, (float Width, float Height)> _fixedBoxes;
    private readonly ReplacementRange[] _replacement;
    private readonly FrozenDictionary<int, ReplacementRange[]> _belowOverrides;
    private readonly JavaEntityGeometry _geometry;
    private readonly int[] _bedStates;
    private readonly Direction[] _bedFacings;

    private readonly record struct ReplacementRange(int First, int Last, byte Flags);

    private JavaBlockInteractions(Dictionary<Identifier, BlockInteractionInfo> blocks, Dictionary<Identifier, ItemUseOnInfo> items, Dictionary<Identifier, bool> entities, Dictionary<Identifier, (float Width, float Height)> fixedBoxes, ReplacementRange[] replacement, Dictionary<int, ReplacementRange[]> belowOverrides, JavaEntityGeometry geometry, int[] bedStates, Direction[] bedFacings)
    {
        _blocks = blocks.ToFrozenDictionary();
        _items = items.ToFrozenDictionary();
        _entities = entities.ToFrozenDictionary();
        _fixedBoxes = fixedBoxes.ToFrozenDictionary();
        _replacement = replacement;
        _belowOverrides = belowOverrides.ToFrozenDictionary();
        _geometry = geometry;
        _bedStates = bedStates;
        _bedFacings = bedFacings;
    }

    public bool HasData => _blocks.Count != 0 || _entities.Count != 0 || _replacement.Length != 0 || _geometry.HasData || _bedStates.Length != 0;
    public bool TryGetBedFacing(int stateId, out Direction facing)
    {
        int index = Array.BinarySearch(_bedStates, stateId);
        facing = index >= 0 ? _bedFacings[index] : default;
        return index >= 0;
    }
    public bool TryGetRule(Identifier entityType, out EntityGeometryRule rule) => _geometry.TryGetRule(entityType, out rule);
    public bool TryGetDimensions(Identifier entityType, bool selector, EntityPose pose, out EntityGeometryDimensions dimensions) => _geometry.TryGetDimensions(entityType, selector, pose, out dimensions);
    public bool TryGetBlock(Identifier block, out BlockInteractionInfo info) => _blocks.TryGetValue(block, out info);
    public bool TryGetItem(Identifier item, out ItemUseOnInfo info) => _items.TryGetValue(item, out info);
    public bool IsAlwaysNonObstructingEntity(Identifier entityType) => _entities.GetValueOrDefault(entityType);
    public bool TryGetCubeReplacement(int stateId, int? belowStateId, out BlockReplacementInfo info)
    {
        info = default;
        int index = FindRange(_replacement, stateId);
        if (index < 0)
            return false;
        byte flags = _replacement[index].Flags;
        if (_belowOverrides.TryGetValue(stateId, out ReplacementRange[]? below))
        {
            if (belowStateId is not int lower || FindRange(_replacement, lower) < 0)
                return false;
            int lowerIndex = FindRange(below, lower);
            if (lowerIndex >= 0)
                flags = below[lowerIndex].Flags;
        }
        info = new BlockReplacementInfo((flags & 1) != 0, (flags & 2) != 0);
        return true;
    }
    public bool TryGetFixedEntityDimensions(Identifier entityType, out float width, out float height)
    {
        bool found = _fixedBoxes.TryGetValue(entityType, out var size);
        width = size.Width;
        height = size.Height;
        return found;
    }

    public static JavaBlockInteractions Create(ReadOnlySpan<byte> table, JavaEntityGeometry? entityGeometry = null)
    {
        Dictionary<Identifier, BlockInteractionInfo> blocks = [];
        Dictionary<Identifier, ItemUseOnInfo> items = [];
        Dictionary<Identifier, bool> entities = [];
        Dictionary<Identifier, (float Width, float Height)> fixedBoxes = [];
        ReplacementRange[] replacement = [];
        Dictionary<int, ReplacementRange[]> belowOverrides = [];
        JavaEntityGeometry geometry = entityGeometry ?? JavaEntityGeometry.Empty;
        int[] bedStates = [];
        Direction[] bedFacings = [];
        var reader = new PacketReader(table);
        int schema = reader.ReadVarInt();
        if (schema is not (0 or 1 or 2 or 3 or 4 or 5 or 6))
            throw new ProtocolViolationException("Unsupported generated block interaction schema.");
        if (schema >= 1)
        {
            int blockCount = Count(ref reader, 3);
            for (int index = 0; index < blockCount; index++)
            {
                Identifier name = Identifier.Parse(reader.ReadString());
                byte flags = reader.ReadByte();
                if (flags > 3)
                    throw new ProtocolViolationException("Unsupported generated block interaction flags.");
                blocks.Add(name, new BlockInteractionInfo((flags & 1) != 0, (flags & 2) != 0));
            }
            int itemCount = Count(ref reader, 4);
            for (int index = 0; index < itemCount; index++)
            {
                Identifier name = Identifier.Parse(reader.ReadString());
                byte kind = reader.ReadByte();
                string block = reader.ReadString();
                if (kind > 2 || (kind == 2 && block.Length == 0) || (kind == 1 && block.Length != 0))
                    throw new ProtocolViolationException("Invalid generated item use-on contract.");
                Identifier? placed = block.Length == 0 ? null : Identifier.Parse(block);
                if (placed is Identifier known && !blocks.ContainsKey(known))
                    throw new ProtocolViolationException("Generated item use names an unknown block.");
                items.Add(name, new ItemUseOnInfo((ItemUseOnKind)kind, placed));
            }
            if (schema >= 2)
            {
                int entityCount = Count(ref reader, 3);
                for (int index = 0; index < entityCount; index++)
                {
                    Identifier name = Identifier.Parse(reader.ReadString());
                    byte flags = reader.ReadByte();
                    if (flags > 1)
                        throw new ProtocolViolationException("Unsupported generated entity obstruction rule.");
                    entities.Add(name, flags == 1);
                }
            }
            if (schema >= 3)
            {
                int fixedCount = Count(ref reader, 10);
                for (int index = 0; index < fixedCount; index++)
                {
                    Identifier name = Identifier.Parse(reader.ReadString());
                    float width = reader.ReadFloat(), height = reader.ReadFloat();
                    if (!entities.TryGetValue(name, out bool nonblocking) || nonblocking || !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
                        throw new ProtocolViolationException("Invalid generated fixed entity dimensions.");
                    fixedBoxes.Add(name, (width, height));
                }
            }
            if (schema >= 4)
            {
                replacement = ReadRanges(ref reader, allowEmpty: schema >= 5);
                int belowCount = Count(ref reader, 5);
                for (int index = 0; index < belowCount; index++)
                {
                    int state = reader.ReadVarInt();
                    ReplacementRange[] below = ReadRanges(ref reader);
                    int target = FindRange(replacement, state);
                    if (target < 0 || below.Any(range => range.Flags == replacement[target].Flags || !Covers(replacement, range.First, range.Last)) || !belowOverrides.TryAdd(state, below))
                        throw new ProtocolViolationException("Invalid generated lower-block replacement rule.");
                }
            }
            if (schema == 5)
                geometry = JavaEntityGeometry.Read(ref reader);
            if (schema == 6)
            {
                int bedCount = Count(ref reader, 2);
                bedStates = new int[bedCount];
                bedFacings = new Direction[bedCount];
                int previous = -1;
                for (int index = 0; index < bedCount; index++)
                {
                    int state = reader.ReadVarInt();
                    byte facing = reader.ReadByte();
                    if (state <= previous || facing is < 2 or > 5)
                        throw new ProtocolViolationException("Invalid generated bed state or horizontal facing.");
                    bedStates[index] = state;
                    bedFacings[index] = (Direction)facing;
                    previous = state;
                }
            }
        }
        if (reader.Remaining != 0)
            throw new ProtocolViolationException("Generated block interaction table has trailing bytes.");
        return new JavaBlockInteractions(blocks, items, entities, fixedBoxes, replacement, belowOverrides, geometry, bedStates, bedFacings);
    }

    private static ReplacementRange[] ReadRanges(ref PacketReader reader, bool allowEmpty = false)
    {
        int count = Count(ref reader, 3);
        if (count == 0 && !allowEmpty)
            throw new ProtocolViolationException("Generated replacement ranges are empty.");
        ReplacementRange[] ranges = new ReplacementRange[count];
        int previous = -1;
        for (int index = 0; index < count; index++)
        {
            int first = reader.ReadVarInt(), last = reader.ReadVarInt();
            byte flags = reader.ReadByte();
            if (first < 0 || last < first || first <= previous || flags > 3 || (index > 0 && first == (long)previous + 1 && flags == ranges[index - 1].Flags))
                throw new ProtocolViolationException("Invalid generated replacement range.");
            ranges[index] = new ReplacementRange(first, last, flags);
            previous = last;
        }
        return ranges;
    }

    private static int FindRange(ReplacementRange[] ranges, int state)
    {
        int first = 0, last = ranges.Length - 1;
        while (first <= last)
        {
            int middle = first + ((last - first) >> 1);
            if (state < ranges[middle].First)
                last = middle - 1;
            else if (state > ranges[middle].Last)
                first = middle + 1;
            else
                return middle;
        }
        return -1;
    }

    private static bool Covers(ReplacementRange[] ranges, int first, int last)
    {
        int index = FindRange(ranges, first);
        if (index < 0)
            return false;
        while (last > ranges[index].Last)
        {
            if (index + 1 == ranges.Length || ranges[index + 1].First != (long)ranges[index].Last + 1)
                return false;
            index++;
        }
        return true;
    }

    private static int Count(ref PacketReader reader, int minimum)
    {
        int count = reader.ReadVarInt();
        if (count < 0 || count > reader.Remaining / minimum)
            throw new ProtocolViolationException("Generated block interaction count exceeds available rows.");
        return count;
    }
}
