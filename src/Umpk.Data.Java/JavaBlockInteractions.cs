using System.Collections.Frozen;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

/// <summary>Named native contracts, independent of a server's numeric registry ordering.</summary>
internal sealed class JavaBlockInteractions : IBlockInteractionSource
{
    private readonly FrozenDictionary<Identifier, BlockInteractionInfo> _blocks;
    private readonly FrozenDictionary<Identifier, ItemUseOnInfo> _items;
    private readonly FrozenDictionary<Identifier, bool> _entities;
    private readonly FrozenDictionary<Identifier, (float Width, float Height)> _fixedBoxes;

    private JavaBlockInteractions(Dictionary<Identifier, BlockInteractionInfo> blocks, Dictionary<Identifier, ItemUseOnInfo> items, Dictionary<Identifier, bool> entities, Dictionary<Identifier, (float Width, float Height)> fixedBoxes)
    {
        _blocks = blocks.ToFrozenDictionary();
        _items = items.ToFrozenDictionary();
        _entities = entities.ToFrozenDictionary();
        _fixedBoxes = fixedBoxes.ToFrozenDictionary();
    }

    public bool HasData => _blocks.Count != 0 || _entities.Count != 0;
    public bool TryGetBlock(Identifier block, out BlockInteractionInfo info) => _blocks.TryGetValue(block, out info);
    public bool TryGetItem(Identifier item, out ItemUseOnInfo info) => _items.TryGetValue(item, out info);
    public bool IsAlwaysNonObstructingEntity(Identifier entityType) => _entities.GetValueOrDefault(entityType);
    public bool TryGetFixedEntityDimensions(Identifier entityType, out float width, out float height)
    {
        bool found = _fixedBoxes.TryGetValue(entityType, out var size);
        width = size.Width;
        height = size.Height;
        return found;
    }

    public static JavaBlockInteractions Create(ReadOnlySpan<byte> table)
    {
        Dictionary<Identifier, BlockInteractionInfo> blocks = [];
        Dictionary<Identifier, ItemUseOnInfo> items = [];
        Dictionary<Identifier, bool> entities = [];
        Dictionary<Identifier, (float Width, float Height)> fixedBoxes = [];
        var reader = new PacketReader(table);
        int schema = reader.ReadVarInt();
        if (schema is not (0 or 1 or 2 or 3))
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
            if (schema == 3)
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
        }
        if (reader.Remaining != 0)
            throw new ProtocolViolationException("Generated block interaction table has trailing bytes.");
        return new JavaBlockInteractions(blocks, items, entities, fixedBoxes);
    }

    private static int Count(ref PacketReader reader, int minimum)
    {
        int count = reader.ReadVarInt();
        if (count < 0 || count > reader.Remaining / minimum)
            throw new ProtocolViolationException("Generated block interaction count exceeds available rows.");
        return count;
    }
}
