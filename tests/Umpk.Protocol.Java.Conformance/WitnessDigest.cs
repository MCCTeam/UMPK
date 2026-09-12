using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Umpk.Game.Entities;
using Umpk.Game.World;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The decoded-values column of the witness pin: a total, stable rendering of what a codec actually produced from a witness payload.</summary>
/// <remarks>
/// <para>"Did not throw" is not a result, and neither is a consumed-byte count on its own: two eras can read the same number of bytes and disagree about what those bytes mean (a short container id read as a byte and as a VarInt, a field pair swapped, an angle scaled differently). The rejection clause compares these strings, so the digest has to be a function of the VALUES rather than of the shape.</para>
/// <para>It is therefore complete rather than pretty: every readable property, ordered by name so the reflection order cannot move it, with bulk payloads folded to a length and a hash so a chunk's section blob does not put a megabyte in a fixture. The rendered column is the same string clipped, with the hash of the whole appended when it is, so what the fixture shows is short and what the comparison uses is not.</para>
/// </remarks>
internal static class WitnessDigest
{
    /// <summary>How much of a digest the pin shows before it clips and appends the whole string's hash.</summary>
    private const int RenderedWidth = 96;

    private const int MaxDepth = 4;

    /// <summary>Elements rendered element-by-element before a sequence folds to a count and a hash.</summary>
    private const int MaxElements = 8;

    // The committed v1 pins used Serbian formatting in nested records' ToString methods. Freeze that existing representation so running on another locale does not rewrite evidence.
    private static readonly CultureInfo s_pinCulture = CultureInfo.GetCultureInfo("sr-Latn-RS");

    /// <summary>The complete digest, which is what a rejection clause compares.</summary>
    internal static string Canonical(object? value)
    {
        // Deep record ToString methods and composite values also format numbers. Keep their rendering stable without changing the caller's culture after this synchronous walk.
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = s_pinCulture;
            return Write(value, 0);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>The clipped form the fixture carries, still keyed to the whole by its hash.</summary>
    internal static string Render(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        return canonical.Length <= RenderedWidth
            ? canonical
            : $"{canonical[..RenderedWidth]} h={Hash(canonical)}";
    }

    /// <summary>The first field on which two digests disagree, which is what makes a rejection readable: an era that read the same byte count and a different meaning says which meaning.</summary>
    internal static string FirstDifference(string mine, string theirs)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);
        int cut = 0;
        int limit = Math.Min(mine.Length, theirs.Length);
        for (int i = 0; i < limit; i++)
        {
            if (mine[i] != theirs[i])
                break;

            if (mine[i] is ',' or '{' or '[')
                cut = i + 1;

        }

        string tail = theirs[cut..];
        int end = tail.IndexOfAny([',', '}', ']']);
        string field = end < 0 ? tail : tail[..end];
        return field.Length == 0 ? Render(theirs) : field.Trim();
    }

    /// <summary>Eight hex characters of SHA-256, enough to key a fold back to what it folded.</summary>
    internal static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..8];

    private static string Write(object? value, int depth)
    {
        switch (value)
        {
            case null:
                return "null";
            case string s:
                return Quote(s);
            case bool b:
                return b ? "true" : "false";
            case byte[] bytes:
                return $"b[{bytes.Length}:{Hash(bytes)}]";
            case ReadOnlyMemory<byte> memory:
                return $"b[{memory.Length}:{Hash(memory.Span)}]";
            case float f:
                return f.ToString("R", CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("R", CultureInfo.InvariantCulture);
            case Enum e:
                return e.ToString();
            case IFormattable formattable when value.GetType().IsPrimitive || value is Guid or decimal:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            case MetadataValue metadata:
                return WriteMetadataValue(metadata, depth);
            case ChunkColumn column:
                return WriteChunkColumn(column);
        }

        if (depth >= MaxDepth)
            return Hash(value.ToString() ?? value.GetType().Name);

        if (value is IEnumerable sequence)
            return WriteSequence(sequence, depth);

        Type type = value.GetType();

        // A type that says what it is beats reflecting over it: identifiers, keys and the geometry structs all have a canonical ToString, and their properties would print the same information wider.
        if (type.Namespace is not null && !type.Namespace.StartsWith("Umpk.Protocol.Java.Packets", StringComparison.Ordinal)
            && type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length == 0)
            return Quote(value.ToString() ?? type.Name);

        return WriteMembers(value, type, depth);
    }

    /// <summary>Entity metadata keeps its payload behind typed accessor METHODS, so reflecting over properties sees only the kind and would digest a block position and a boolean the same way. That is exactly the difference a metadata-table or block-position-layout change makes, so the kinds are unpacked here by hand. A kind with no arm fails loudly rather than digesting to nothing.</summary>
    private static string WriteMetadataValue(MetadataValue value, int depth)
    {
        object? inner = value.Kind switch
        {
            MetadataValueKind.Byte => value.AsByte(),
            MetadataValueKind.VarInt => value.AsVarInt(),
            MetadataValueKind.VarLong => value.AsVarLong(),
            MetadataValueKind.Float => value.AsFloat(),
            MetadataValueKind.String => value.AsString(),
            MetadataValueKind.Component => value.AsComponent(),
            MetadataValueKind.OptionalComponent => value.AsOptionalComponent(),
            MetadataValueKind.Slot => value.AsSlot(),
            MetadataValueKind.Boolean => value.AsBoolean(),
            MetadataValueKind.Rotations => value.AsRotations(),
            MetadataValueKind.Position => value.AsPosition(),
            MetadataValueKind.OptionalPosition => value.AsOptionalPosition(),
            MetadataValueKind.Direction => value.AsDirection(),
            MetadataValueKind.OptionalUuid => value.AsOptionalUuid(),
            MetadataValueKind.BlockState => value.AsBlockState(),
            MetadataValueKind.OptionalBlockState => value.AsOptionalBlockState(),
            MetadataValueKind.Nbt => value.AsNbt(),
            MetadataValueKind.Particle => value.AsParticle(),
            MetadataValueKind.VillagerData => value.AsVillagerData(),
            MetadataValueKind.OptionalVarInt => value.AsOptionalVarInt(),
            MetadataValueKind.Pose => value.AsPose(),
            MetadataValueKind.GlobalPosition => value.AsGlobalPosition(),
            MetadataValueKind.OptionalGlobalPosition => value.AsOptionalGlobalPosition(),
            MetadataValueKind.Quaternion => value.AsQuaternion(),
            MetadataValueKind.Vector3 => value.AsVector3(),
            MetadataValueKind.Particles => value.AsParticles(),
            MetadataValueKind.ResolvableProfile => value.AsResolvableProfile(),
            _ => throw new NotSupportedException($"The witness digest has no arm for metadata kind {value.Kind}."),
        };

        return $"md[{value.Kind}:{Write(inner, depth + 1)}]";
    }

    /// <summary>A chunk column keeps its blocks behind an indexed accessor, and the blocks are the whole point: the pre-flattening codec reads a section palette as <c>(id &lt;&lt; 4) | meta</c> and its successor reads flat state ids from the very same bytes, which no property on the column reports. The ids are sampled on a stride rather than read whole because a stride is enough to separate two readings of the same buffer and a full walk is 4,096 lookups per section.</summary>
    private static string WriteChunkColumn(ChunkColumn column)
    {
        var ids = new StringBuilder();
        int filled = 0;
        for (int section = 0; section < column.SectionCount; section++)
        {
            if (column.GetSection(section) is null)
                continue;

            filled++;
            int baseY = column.MinY + (section * 16);
            for (int y = 0; y < 16; y += 2)
                for (int z = 0; z < 16; z += 2)
                    for (int x = 0; x < 16; x += 2)
                        ids.Append(column.GetBlockStateId((column.Position.X * 16) + x, baseY + y, (column.Position.Z * 16) + z))
                            .Append(',');

        }

        return $"chunk[{column.Position},y={column.MinY}..{column.MaxY},sections={column.SectionCount}," +
            $"filled={filled},be={column.BlockEntities.Count},ids={Hash(ids.ToString())}]";
    }

    private static string WriteSequence(IEnumerable sequence, int depth)
    {
        var rendered = new List<string>();
        int count = 0;
        var overflow = new StringBuilder();
        foreach (object? element in sequence)
        {
            string text = Write(element, depth + 1);
            count++;
            if (rendered.Count < MaxElements)
                rendered.Add(text);

            else
                overflow.Append(text).Append(';');

        }

        string head = string.Join(",", rendered);
        return overflow.Length == 0
            ? $"[{count}:{head}]"
            : $"[{count}:{head},+{Hash(overflow.ToString())}]";
    }

    private static string WriteMembers(object value, Type type, int depth)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || !property.CanRead ||
                property.PropertyType.IsByRefLike || property.PropertyType == typeof(PacketType))
                continue;

            object? member;
            try
            {
                member = property.GetValue(value);
            }
            catch (TargetInvocationException ex)
            {
                // A property that throws is still a fact about the decode, and a witness must not fall over on one; record the fault type rather than the value.
                member = $"!{ex.InnerException?.GetType().Name ?? ex.GetType().Name}";
            }

            fields[property.Name] = Write(member, depth + 1);
        }

        var sb = new StringBuilder();
        sb.Append(ShortName(type)).Append('{');
        bool first = true;
        foreach ((string name, string rendered) in fields)
        {
            if (!first)
                sb.Append(',');

            sb.Append(name).Append('=').Append(rendered);
            first = false;
        }

        return sb.Append('}').ToString();
    }

    /// <summary>Trims the ceremony off a packet record's name so the column carries fields, not prefixes.</summary>
    private static string ShortName(Type type)
    {
        string name = type.Name;
        if (name.EndsWith("Packet", StringComparison.Ordinal))
            name = name[..^"Packet".Length];

        if (name.StartsWith("Clientbound", StringComparison.Ordinal))
            name = name["Clientbound".Length..];

        else if (name.StartsWith("Serverbound", StringComparison.Ordinal))
            name = name["Serverbound".Length..];

        return name;
    }

    private static string Quote(string text) =>
        text.Length <= 48
            ? $"'{text.Replace('\n', ' ').Replace('\r', ' ')}'"
            : $"'{text[..40].Replace('\n', ' ').Replace('\r', ' ')}~{Hash(text)}'";
}
