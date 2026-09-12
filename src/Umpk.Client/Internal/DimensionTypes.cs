using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Turns the server's own <c>minecraft:dimension_type</c> data into a <see cref="Registry{T}"/> of <see cref="DimensionTypeDefinition"/>, plus the vanilla built-in table used when the server sends nothing (or sends an entry with its element elided).</summary>
/// <remarks>
/// <para>Three wire shapes carry the same three fields, so all three land here:</para>
/// <list type="bullet">
/// <item>1.20.2+ (764+): configuration-phase <c>registry_data</c>, one packet per registry, entries in
/// network-id order (<see cref="FromPackedEntries"/>).</item>
/// <item>1.16-1.20.1: the whole registry set as one named-root NBT blob inside JoinGame,
/// each registry a <c>{type, value:[{name,id,element}]}</c> compound (<see cref="FromJoinGameRegistry"/>).</item>
/// <item>1.16.2-1.18.2: the CURRENT dimension's type inline in JoinGame and Respawn as a
/// bare datapack compound (<see cref="TryReadElement"/> directly).</item>
/// </list>
/// <para>The fields read are datapack keys: <c>min_y</c> (int), <c>height</c> (int), <c>has_skylight</c> (bool). <c>height</c> is what sizes a column's section stack; <c>logical_height</c> is NOT (it is 128 in the nether, whose type spans 256).</para>
/// <para>From 1.20.5 a server may answer a known-packs handshake by sending an entry with NO element at all (<see cref="PackedRegistryEntry.Data"/> null), meaning "you already have this one from a pack you told me you know". The id-to-identifier mapping is still the server's, and is exactly what a spawn-info dimension-type id has to resolve through, so such an entry keeps its id and falls back to the vanilla built-in bounds for that identifier. An entry with no element AND no built-in is dropped rather than invented.</para>
/// </remarks>
internal static class DimensionTypes
{
    private const string MinYKey = "min_y";
    private const string HeightKey = "height";
    private const string HasSkylightKey = "has_skylight";

    /// <summary>The first protocol whose vanilla OVERWORLD is -64..320 rather than 0..256: 1.18, which is where the world-height extension landed. 1.17.1's built-in overworld is still <c>min_y = 0, height = 256</c>, so a client on 47-756 that assumed the modern bounds would place every overworld column 64 blocks low the moment columns started taking the world's floor.</summary>
    private const int FirstDeepOverworldProtocol = 757;

    /// <summary>Builds a dimension-type registry from one configuration-phase <c>registry_data</c> packet's entries. Network ids are the entry order, which is how vanilla assigns them. Returns null when nothing usable was present.</summary>
    public static Registry<DimensionTypeDefinition>? FromPackedEntries(
        IReadOnlyList<PackedRegistryEntry> entries, int protocol)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType, entries.Count);
        for (int id = 0; id < entries.Count; id++)
        {
            PackedRegistryEntry entry = entries[id];
            if (TryReadElement(entry.Data, out DimensionTypeDefinition definition)
                || TryVanillaDefinition(entry.Id, protocol, out definition))
                builder.Add(id, entry.Id, definition);

        }

        return builder.Count == 0 ? null : builder.Build();
    }

    /// <summary>Builds a dimension-type registry from the 1.16-1.20.1 JoinGame registry blob. Returns null when the blob carries no usable <c>minecraft:dimension_type</c> section.</summary>
    public static Registry<DimensionTypeDefinition>? FromJoinGameRegistry(NbtTag? blob, int protocol)
    {
        if (blob is not NbtCompound root)
            return null;

        NbtCompound? section = root.GetCompound(RegistryIds.DimensionType.ToString());
        NbtList? values = section?.GetList("value");
        if (values is null)
            return null;

        var builder = new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType, values.Count);
        foreach (NbtTag item in values)
        {
            if (item is not NbtCompound record
                || !Identifier.TryParse(record.GetString("name"), out Identifier name)
                || !record.ContainsKey("id"))
                continue;

            if (TryReadElement(record.GetCompound("element"), out DimensionTypeDefinition definition)
                || TryVanillaDefinition(name, protocol, out definition))
                builder.Add(record.GetInt("id"), name, definition);

        }

        return builder.Count == 0 ? null : builder.Build();
    }

    /// <summary>Reads one dimension-type datapack compound. Returns false when the tag is not a compound or does not carry both <c>min_y</c> and <c>height</c>, so a missing element is never mistaken for a zero-floor dimension.</summary>
    public static bool TryReadElement(NbtTag? tag, out DimensionTypeDefinition definition)
    {
        definition = null!;
        if (tag is not NbtCompound element
            || !element.TryGet(MinYKey, out NbtNumeric? minY)
            || !element.TryGet(HeightKey, out NbtNumeric? height))
            return false;

        int heightValue = height.AsInt;
        if (heightValue < 16 || heightValue % 16 != 0 || minY.AsInt % 16 != 0)
        {
            // Vanilla's own codec rejects these (guard y behavior), and a column sized from them would be nonsense. Refusing sends the caller to the built-in table instead.
            return false;
        }

        definition = new DimensionTypeDefinition(minY.AsInt, heightValue, element.GetBool(HasSkylightKey));
        return true;
    }

    /// <summary>The vanilla built-in bounds for a dimension identifier on a protocol, or false when the identifier is not one of the three built-ins (a datapack dimension, which must come from the server or not at all).</summary>
    /// <remarks>Built-in values: overworld <c>(-64, 384, skylight)</c>, nether <c>(0, 256, no skylight)</c>, end <c>(0, 256, no skylight)</c>. Below 1.18 the overworld is <c>(0, 256)</c>.</remarks>
    public static bool TryVanillaDefinition(Identifier name, int protocol, out DimensionTypeDefinition definition)
    {
        switch (name.ToString())
        {
            case "minecraft:the_nether":
                definition = new DimensionTypeDefinition(0, 256, HasSkylight: false);
                return true;
            case "minecraft:the_end":
                definition = new DimensionTypeDefinition(0, 256, HasSkylight: false);
                return true;
            case "minecraft:overworld":
                definition = protocol >= FirstDeepOverworldProtocol
                    ? new DimensionTypeDefinition(-64, 384, HasSkylight: true)
                    : new DimensionTypeDefinition(0, 256, HasSkylight: true);
                return true;
            default:
                definition = null!;
                return false;
        }
    }

    /// <summary>The bounds to fall back on for a world NAME when no dimension type is available at all: the built-in for that name, or the era's overworld for an unknown (datapack) world.</summary>
    public static DimensionTypeDefinition FallbackFor(string dimensionName, int protocol)
    {
        if (Identifier.TryParse(dimensionName, out Identifier parsed)
            && TryVanillaDefinition(parsed, protocol, out DimensionTypeDefinition known))
            return known;

        TryVanillaDefinition(Identifier.Minecraft("overworld"), protocol, out DimensionTypeDefinition overworld);
        return overworld;
    }
}
