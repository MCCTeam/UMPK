using System.Collections.Immutable;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

/// <summary>The per-version argument-type table for decoding and encoding the property payloads of command argument nodes. It maps the wire parser id to its parser identifier (e.g. <c>brigadier:string</c>) and back, and it knows how to read/write each known parser's property payload. The id-to-name mapping is version-specific (26.2 inserts <c>team_color</c>/<c>hex_color</c> and shifts every later id); the property serialization is keyed by the stable parser name.</summary>
/// <remarks>Until generated argument-type data is available, the 770 and 776 tables are built from a curated ordered list. Unknown ids resolve to a null name and preserve their raw bytes.</remarks>
public sealed partial class ArgumentTypeRegistry
{
    private readonly ImmutableArray<string> _idToName;

    private readonly ImmutableDictionary<string, int> _nameToId;

    private ArgumentTypeRegistry(ImmutableArray<string> idToName, ImmutableDictionary<string, int> nameToId)
    {
        _idToName = idToName;
        _nameToId = nameToId;
        ShapeToken = "argtypes/" + WireShapeDigest.Of(idToName.Select(static (n, i) => $"{i}:{n}"));
    }

    /// <summary>This era's contribution to the wire shape of the command-tree codec that reads through it: the digest of the parser id ordering, which is the version fact the table carries (26.2 inserts <c>team_color</c> and <c>hex_color</c> and shifts every later id).</summary>
    internal string ShapeToken { get; }

    /// <summary>The 1.21.5 (protocol 770) argument-type table.</summary>
    public static ArgumentTypeRegistry V1_21_5 { get; } = Build(ArgumentTypeTables.V770);

    /// <summary>The 26.2 (protocol 776) argument-type table.</summary>
    public static ArgumentTypeRegistry V26_2 { get; } = Build(ArgumentTypeTables.V776);

    /// <summary>The number of parser entries in this table.</summary>
    public int Count => _idToName.Length;

    /// <summary>Resolves a wire parser id to its parser identifier, or null when the id is unknown.</summary>
    public string? NameFromId(int id) => id >= 0 && id < _idToName.Length ? _idToName[id] : null;

    /// <summary>Resolves a parser identifier to its wire id, or -1 when the name is not in this table.</summary>
    public int IdFromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _nameToId.TryGetValue(name, out int id) ? id : -1;
    }

    /// <summary>Builds a registry from an ordered parser-name list (index = wire id). Duplicate names throw: the argument-type registry is a bijection per version.</summary>
    public static ArgumentTypeRegistry Build(IReadOnlyList<string> orderedNames)
    {
        ArgumentNullException.ThrowIfNull(orderedNames);
        ImmutableArray<string> idToName = [.. orderedNames];
        ImmutableDictionary<string, int>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < idToName.Length; i++)
        {
            if (builder.ContainsKey(idToName[i]))
                throw new ArgumentException($"Duplicate parser name '{idToName[i]}' in the argument-type table.", nameof(orderedNames));

            builder.Add(idToName[i], i);
        }

        return new ArgumentTypeRegistry(idToName, builder.ToImmutable());
    }

    /// <summary>Reads a parser's property payload from the reader given its resolved name. Returns the typed properties for a known parser. For an unknown name (<paramref name="parserName"/> null) the caller must preserve raw bytes instead; this method never advances the reader in that case.</summary>
    /// <param name="reader">The packet reader positioned at the parser's property payload.</param>
    /// <param name="parserName">The string parser id (e.g. <c>minecraft:time</c>), or null for none.</param>
    /// <param name="timeHasMin">Whether the <c>minecraft:time</c> argument carries its 32-bit <c>min</c> property. That field was added at 1.19.4 (protocol 762); earlier versions carry no property bytes for <c>time</c>. Before 762 (1.14 - 1.19.3, the versions where the argument exists) the payload is empty. Reading the int unconditionally there consumes 4 spurious bytes and desynchronizes the node list (surfacing downstream as an impossible "node type 3"). The string-keyed and 759-761 codecs pass <see langword="false"/>; the 762-and-later callers default to <see langword="true"/>.</param>
    public static ArgumentParserProperties? ReadProperties(ref PacketReader reader, string? parserName, bool timeHasMin = true)
    {
        if (parserName is null)
            return null;

        return parserName switch
        {
            "brigadier:bool" => ArgumentParserProperties.Empty,
            "brigadier:string" => new StringArgumentProperties((BrigadierStringKind)reader.ReadVarInt()),
            "brigadier:integer" => ReadIntRange(ref reader),
            "brigadier:long" => ReadLongRange(ref reader),
            "brigadier:float" => ReadFloatRange(ref reader),
            "brigadier:double" => ReadDoubleRange(ref reader),
            "minecraft:entity" => ReadEntity(ref reader),
            "minecraft:score_holder" => ReadScoreHolder(ref reader),
            "minecraft:time" => timeHasMin ? new TimeArgumentProperties(reader.ReadInt()) : ArgumentParserProperties.Empty,
            "minecraft:resource"
                or "minecraft:resource_key"
                or "minecraft:resource_or_tag"
                or "minecraft:resource_or_tag_key"
                or "minecraft:resource_selector" => new RegistryArgumentProperties(Identifier.Parse(reader.ReadString())),
            _ => ArgumentParserProperties.Empty,
        };
    }

    /// <summary>Writes a parser's property payload, mirroring <see cref="ReadProperties"/>.</summary>
    /// <param name="writer">The packet writer positioned where the property payload belongs.</param>
    /// <param name="parserName">The string parser id (e.g. <c>minecraft:time</c>).</param>
    /// <param name="properties">The typed properties to serialize.</param>
    /// <param name="timeHasMin">The encode-side mirror of the <see cref="ReadProperties"/> parameter. A decoded tree from a pre-762 era carries <see cref="ArgumentParserProperties.Empty"/> for <c>minecraft:time</c>, so a round-trip needs no special case; this guard exists so a HAND-BUILT <see cref="TimeArgumentProperties"/> cannot silently emit four bytes an era's peer will not read.</param>
    public static void WriteProperties(
        ref PacketWriter writer, string parserName, ArgumentParserProperties properties, bool timeHasMin = true)
    {
        ArgumentNullException.ThrowIfNull(parserName);
        ArgumentNullException.ThrowIfNull(properties);

        if (!timeHasMin && properties is TimeArgumentProperties)
            throw new ProtocolViolationException(
                $"Parser '{parserName}' carries a time minimum, but this protocol era's minecraft:time has no min field " +
                "(it was added at protocol 762); writing it would desynchronize the node list.");

        switch (properties)
        {
            case EmptyArgumentProperties:
                break;
            case StringArgumentProperties s:
                writer.WriteVarInt((int)s.Kind);
                break;
            case IntegerArgumentProperties i:
                WriteIntRange(ref writer, i);
                break;
            case LongArgumentProperties l:
                WriteLongRange(ref writer, l);
                break;
            case FloatArgumentProperties f:
                WriteFloatRange(ref writer, f);
                break;
            case DoubleArgumentProperties d:
                WriteDoubleRange(ref writer, d);
                break;
            case EntityArgumentProperties e:
                writer.WriteByte((byte)((e.SingleTarget ? 1 : 0) | (e.PlayersOnly ? 2 : 0)));
                break;
            case ScoreHolderArgumentProperties sh:
                writer.WriteByte((byte)(sh.AllowsMultiple ? 1 : 0));
                break;
            case TimeArgumentProperties t:
                writer.WriteInt(t.Min);
                break;
            case RegistryArgumentProperties r:
                writer.WriteString(r.Registry.ToString());
                break;
            default:
                throw new ProtocolViolationException(
                    $"Unhandled argument property payload {properties.GetType().Name} for parser '{parserName}'.");
        }
    }

    // brigadier numeric range payloads (flags byte then present bounds)

    private static IntegerArgumentProperties ReadIntRange(ref PacketReader r)
    {
        byte flags = r.ReadByte();
        int min = (flags & 1) != 0 ? r.ReadInt() : int.MinValue;
        int max = (flags & 2) != 0 ? r.ReadInt() : int.MaxValue;
        return new IntegerArgumentProperties(min, max);
    }

    private static void WriteIntRange(ref PacketWriter w, IntegerArgumentProperties p)
    {
        bool hasMin = p.Min != int.MinValue;
        bool hasMax = p.Max != int.MaxValue;
        w.WriteByte((byte)((hasMin ? 1 : 0) | (hasMax ? 2 : 0)));
        if (hasMin)
            w.WriteInt(p.Min);

        if (hasMax)
            w.WriteInt(p.Max);

    }

    private static LongArgumentProperties ReadLongRange(ref PacketReader r)
    {
        byte flags = r.ReadByte();
        long min = (flags & 1) != 0 ? r.ReadLong() : long.MinValue;
        long max = (flags & 2) != 0 ? r.ReadLong() : long.MaxValue;
        return new LongArgumentProperties(min, max);
    }

    private static void WriteLongRange(ref PacketWriter w, LongArgumentProperties p)
    {
        bool hasMin = p.Min != long.MinValue;
        bool hasMax = p.Max != long.MaxValue;
        w.WriteByte((byte)((hasMin ? 1 : 0) | (hasMax ? 2 : 0)));
        if (hasMin)
            w.WriteLong(p.Min);

        if (hasMax)
            w.WriteLong(p.Max);

    }

    private static FloatArgumentProperties ReadFloatRange(ref PacketReader r)
    {
        byte flags = r.ReadByte();
        float min = (flags & 1) != 0 ? r.ReadFloat() : -float.MaxValue;
        float max = (flags & 2) != 0 ? r.ReadFloat() : float.MaxValue;
        return new FloatArgumentProperties(min, max);
    }

    private static void WriteFloatRange(ref PacketWriter w, FloatArgumentProperties p)
    {
        bool hasMin = p.Min != -float.MaxValue;
        bool hasMax = p.Max != float.MaxValue;
        w.WriteByte((byte)((hasMin ? 1 : 0) | (hasMax ? 2 : 0)));
        if (hasMin)
            w.WriteFloat(p.Min);

        if (hasMax)
            w.WriteFloat(p.Max);

    }

    private static DoubleArgumentProperties ReadDoubleRange(ref PacketReader r)
    {
        byte flags = r.ReadByte();
        double min = (flags & 1) != 0 ? r.ReadDouble() : -double.MaxValue;
        double max = (flags & 2) != 0 ? r.ReadDouble() : double.MaxValue;
        return new DoubleArgumentProperties(min, max);
    }

    private static void WriteDoubleRange(ref PacketWriter w, DoubleArgumentProperties p)
    {
        bool hasMin = p.Min != -double.MaxValue;
        bool hasMax = p.Max != double.MaxValue;
        w.WriteByte((byte)((hasMin ? 1 : 0) | (hasMax ? 2 : 0)));
        if (hasMin)
            w.WriteDouble(p.Min);

        if (hasMax)
            w.WriteDouble(p.Max);

    }

    private static EntityArgumentProperties ReadEntity(ref PacketReader r)
    {
        byte flags = r.ReadByte();
        return new EntityArgumentProperties((flags & 1) != 0, (flags & 2) != 0);
    }

    private static ScoreHolderArgumentProperties ReadScoreHolder(ref PacketReader r)
    {
        byte flags = r.ReadByte();
        return new ScoreHolderArgumentProperties((flags & 1) != 0);
    }

    /// <summary>The protocol-759/760 (1.19-1.19.2) registry.</summary>
    public static ArgumentTypeRegistry V759 { get; } = ArgumentTypeRegistry.Build(ArgumentTypeTables.V759);

    /// <summary>The protocol-761 (1.19.3) registry.</summary>
    public static ArgumentTypeRegistry V761 { get; } = ArgumentTypeRegistry.Build(ArgumentTypeTables.V761);

    /// <summary>The protocol-762/763/764 (1.19.4-1.20.2) registry.</summary>
    public static ArgumentTypeRegistry V764 { get; } = ArgumentTypeRegistry.Build(ArgumentTypeTables.V764);

    /// <summary>The protocol-765 registry.</summary>
    public static ArgumentTypeRegistry V765 { get; } = ArgumentTypeRegistry.Build(ArgumentTypeTables.V765);

    /// <summary>The protocol-766..769 (1.20.5-1.21.4) registry.</summary>
    public static ArgumentTypeRegistry V766 { get; } = ArgumentTypeRegistry.Build(ArgumentTypeTables.V766);

    /// <summary>The 1.21.6-26.1 (protocols 771-775) argument-type table.</summary>
    public static ArgumentTypeRegistry V1_21_6 { get; } = Build(ArgumentTypeTables.V771);
}
