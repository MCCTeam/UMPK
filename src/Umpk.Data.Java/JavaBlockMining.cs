using System.Collections.Frozen;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

/// <summary>Immutable mining facts decoded from the generated artifact-backed table. Names keep lookups independent of server registry ordering.</summary>
internal sealed class JavaBlockMining : IBlockMiningSource
{
    private readonly FrozenDictionary<Identifier, BlockMiningInfo> _blocks;
    private readonly FrozenSet<Identifier> _items;
    private readonly FrozenDictionary<(Identifier Item, Identifier Block), ToolMiningInfo> _tools;
    private readonly FrozenDictionary<Identifier, ToolComponent>? _defaultTools;

    private JavaBlockMining(
        Dictionary<Identifier, BlockMiningInfo> blocks,
        HashSet<Identifier> items,
        Dictionary<(Identifier, Identifier), ToolMiningInfo> tools,
        Dictionary<Identifier, ToolComponent>? defaults = null)
    {
        _blocks = blocks.ToFrozenDictionary();
        _items = items.ToFrozenSet();
        _tools = tools.ToFrozenDictionary();
        _defaultTools = defaults?.ToFrozenDictionary();
    }

    public bool HasData => _blocks.Count != 0;

    public static JavaBlockMining Create(ReadOnlySpan<byte> table, Registry<BlockDefinition>? blockRegistry = null)
    {
        Dictionary<Identifier, BlockMiningInfo> blocks = [];
        HashSet<Identifier> items = [];
        Dictionary<(Identifier, Identifier), ToolMiningInfo> tools = [];
        var reader = new PacketReader(table);
        int schema = reader.ReadVarInt();
        if (schema == 0)
        {
            RequireEnd(ref reader);
            return new JavaBlockMining(blocks, items, tools);
        }
        if (schema is not (1 or 2))
            throw new ProtocolViolationException("Unsupported generated mining table schema.");

        int blockCount = ReadCount(ref reader, 7);
        Identifier[] blockNames = new Identifier[blockCount];
        for (int index = 0; index < blockCount; index++)
        {
            Identifier name = Identifier.Parse(reader.ReadString());
            blockNames[index] = name;
            (float value, bool flag) = ReadRow(ref reader);
            if (value < 0 && value != -1.0F)
                throw new ProtocolViolationException("Invalid generated block hardness.");
            blocks.Add(name, new BlockMiningInfo(value, flag));
        }
        int itemCount = ReadCount(ref reader, 2);
        Identifier[] itemNames = new Identifier[itemCount];
        for (int index = 0; index < itemCount; index++)
        {
            Identifier name = Identifier.Parse(reader.ReadString());
            itemNames[index] = name;
            if (!items.Add(name))
                throw new ProtocolViolationException("Duplicate generated mining item.");
        }
        int toolCount = ReadCount(ref reader, 2);
        HashSet<int> seenTools = [];
        for (int index = 0; index < toolCount; index++)
        {
            int item = reader.ReadVarInt();
            if ((uint)item >= (uint)itemCount || !seenTools.Add(item))
                throw new ProtocolViolationException("Invalid or duplicate generated mining tool index.");
            int ruleCount = ReadCount(ref reader, 6);
            for (int rule = 0; rule < ruleCount; rule++)
            {
                int block = reader.ReadVarInt();
                if ((uint)block >= (uint)blockCount)
                    throw new ProtocolViolationException("Invalid generated mining block index.");
                (float speed, bool correct) = ReadRow(ref reader);
                if (speed < 0)
                    throw new ProtocolViolationException("Invalid generated mining speed.");
                tools.Add((itemNames[item], blockNames[block]), new ToolMiningInfo(speed, correct));
            }
        }
        Dictionary<Identifier, ToolComponent>? defaults = schema == 2 ? ReadDefaults(ref reader, itemNames, blockNames, blockRegistry) : null;
        RequireEnd(ref reader);
        return new JavaBlockMining(blocks, items, tools, defaults);
    }

    public bool TryGetDefaultTool(Identifier item, out ToolComponent? tool)
    {
        tool = null;
        if (_defaultTools is null || !_items.Contains(item))
            return false;
        _defaultTools.TryGetValue(item, out tool);
        return true;
    }

    private static Dictionary<Identifier, ToolComponent> ReadDefaults(ref PacketReader reader, Identifier[] items, Identifier[] blocks, Registry<BlockDefinition>? registry)
    {
        if (registry is null)
            throw new ProtocolViolationException("Generated default tools require their block registry.");
        Dictionary<Identifier, ToolComponent> defaults = [];
        int count = ReadCount(ref reader, 8);
        for (int index = 0; index < count; index++)
        {
            int item = ReadIndex(ref reader, items.Length);
            (float speed, bool creative) = ReadRow(ref reader);
            int damage = reader.ReadVarInt();
            if (speed < 0 || damage < 0)
                throw new ProtocolViolationException("Invalid generated default tool values.");
            int ruleCount = ReadCount(ref reader, 4);
            List<NbtCompound> rules = new(ruleCount);
            for (int ruleIndex = 0; ruleIndex < ruleCount; ruleIndex++)
            {
                byte selector = reader.ReadByte();
                NbtList members;
                if (selector == 1)
                {
                    members = new NbtList(NbtTagType.String);
                    members.Add(new NbtString(Identifier.Parse(reader.ReadString()).ToString()));
                }
                else if (selector == 0)
                {
                    members = new NbtList(NbtTagType.Int);
                    int memberCount = ReadCount(ref reader, 1);
                    for (int member = 0; member < memberCount; member++)
                    {
                        Identifier block = blocks[ReadIndex(ref reader, blocks.Length)];
                        if (!registry.TryGetNetworkId(block, out int id))
                            throw new ProtocolViolationException("Generated tool names an unknown registry block.");
                        members.Add(new NbtInt(id));
                    }
                }
                else
                    throw new ProtocolViolationException("Invalid generated default tool selector.");
                NbtCompound rule = new();
                rule.Put("blocks", members);
                byte hasSpeed = reader.ReadByte();
                if (hasSpeed > 1)
                    throw new ProtocolViolationException("Invalid generated optional tool speed.");
                if (hasSpeed == 1)
                {
                    (float ruleSpeed, bool reserved) = ReadRow(ref reader);
                    if (ruleSpeed <= 0 || reserved)
                        throw new ProtocolViolationException("Invalid generated tool rule speed.");
                    rule.PutFloat("speed", ruleSpeed);
                }
                byte correct = reader.ReadByte();
                if (correct > 2)
                    throw new ProtocolViolationException("Invalid generated optional tool harvest flag.");
                if (correct != 0)
                    rule.PutBool("correct_for_drops", correct == 2);
                rules.Add(rule);
            }
            defaults.Add(items[item], new ToolComponent(speed, damage, rules) { CanDestroyBlocksInCreative = creative });
        }
        return defaults;
    }

    private static int ReadIndex(ref PacketReader reader, int count)
    {
        int index = reader.ReadVarInt();
        if ((uint)index >= (uint)count)
            throw new ProtocolViolationException("Invalid generated default tool index.");
        return index;
    }

    public bool TryGetBlock(Identifier block, out BlockMiningInfo info) => _blocks.TryGetValue(block, out info);

    public bool TryGetTool(Identifier item, Identifier block, out ToolMiningInfo info)
    {
        info = default;
        if (!_items.Contains(item) || !_blocks.ContainsKey(block))
            return false;
        if (!_tools.TryGetValue((item, block), out info))
            info = new ToolMiningInfo(1.0F, false);
        return true;
    }

    private static int ReadCount(ref PacketReader reader, int minimumRowBytes)
    {
        int count = reader.ReadVarInt();
        if (count < 0 || count > reader.Remaining / minimumRowBytes)
            throw new ProtocolViolationException("Generated mining table count exceeds its available rows.");
        return count;
    }

    private static (float Value, bool Flag) ReadRow(ref PacketReader reader)
    {
        float value = reader.ReadFloat();
        byte flag = reader.ReadByte();
        if (!float.IsFinite(value) || flag > 1)
            throw new ProtocolViolationException("Invalid generated mining table row.");
        return (value, flag != 0);
    }

    private static void RequireEnd(ref PacketReader reader)
    {
        if (reader.Remaining != 0)
            throw new ProtocolViolationException("Generated mining table has trailing bytes.");
    }
}
