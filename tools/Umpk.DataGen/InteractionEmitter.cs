using System.Buffers.Binary;
using System.Text;

namespace Umpk.DataGen;

internal sealed partial class Emitter
{
    private string InternBlockInteractions(VersionData version)
    {
        using MemoryStream stream = new();
        InteractionTable? table = InteractionTable.Load(version);
        EntityObstructionTable? obstruction = EntityObstructionTable.Load(version);
        ReplacementTable? replacement = ReplacementTable.Load(version);
        EntityGeometryTable? geometry = EntityGeometryTable.Load(version);
        int schema = geometry is not null ? 5 : replacement is not null ? 4 : obstruction?.FixedBoxes.Count > 0 ? 3 : obstruction is not null ? 2 : table is not null ? 1 : 0;
        WriteVarInt(stream, schema);
        if (schema >= 1)
        {
            void Text(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                WriteVarInt(stream, bytes.Length);
                stream.Write(bytes);
            }
            WriteVarInt(stream, table?.Blocks.Count ?? 0);
            foreach ((string block, int flags) in (table?.Blocks.AsEnumerable() ?? []).OrderBy(row => row.Key, StringComparer.Ordinal))
            {
                Text(block);
                stream.WriteByte((byte)flags);
            }
            WriteVarInt(stream, table?.Items.Count ?? 0);
            foreach ((string item, InteractionItem contract) in (table?.Items.AsEnumerable() ?? []).OrderBy(row => row.Key, StringComparer.Ordinal))
            {
                Text(item);
                stream.WriteByte(contract.Kind switch { "unknown" => 0, "pass" => 1, "simple_cube" => 2, _ => throw new DatasetException("Unsupported interaction item kind") });
                Text(contract.Block);
            }
            if (schema >= 2)
            {
                WriteVarInt(stream, obstruction?.Entities.Count ?? 0);
                foreach ((string entity, int flags) in (obstruction?.Entities.AsEnumerable() ?? []).OrderBy(row => row.Key, StringComparer.Ordinal))
                {
                    Text(entity);
                    stream.WriteByte((byte)flags);
                }
                if (schema >= 3)
                {
                    WriteVarInt(stream, obstruction?.FixedBoxes.Count ?? 0);
                    Span<byte> dimensions = stackalloc byte[8];
                    foreach (var entity in (obstruction?.FixedBoxes.AsEnumerable() ?? []).OrderBy(row => row.Key, StringComparer.Ordinal))
                    {
                        Text(entity.Key);
                        BinaryPrimitives.WriteSingleBigEndian(dimensions, entity.Value.Width);
                        BinaryPrimitives.WriteSingleBigEndian(dimensions[4..], entity.Value.Height);
                        stream.Write(dimensions);
                    }
                }
            }
            if (schema >= 4)
            {
                void Ranges(ReplacementRange[] ranges)
                {
                    WriteVarInt(stream, ranges.Length);
                    foreach (ReplacementRange range in ranges)
                    {
                        WriteVarInt(stream, range.First);
                        WriteVarInt(stream, range.Last);
                        stream.WriteByte((byte)range.Flags);
                    }
                }
                Ranges(replacement?.Ranges ?? []);
                WriteVarInt(stream, replacement?.BelowOverrides.Count ?? 0);
                foreach ((int state, ReplacementRange[] ranges) in (replacement?.BelowOverrides.AsEnumerable() ?? []).OrderBy(row => row.Key))
                {
                    WriteVarInt(stream, state);
                    Ranges(ranges);
                }
            }
            if (geometry is not null)
            {
                WriteVarInt(stream, geometry.Entities.Count);
                Span<byte> dimensions = stackalloc byte[8];
                foreach ((string name, EntityGeometryEntry entry) in geometry.Entities.OrderBy(row => row.Key, StringComparer.Ordinal))
                {
                    Text(name);
                    WriteVarInt(stream, entry.MetadataIndex);
                    WriteVarInt(stream, entry.PoseIndex);
                    stream.WriteByte(entry.Refresh switch { "recenter_on_refresh" => 0, "retain_minimum_on_growth" => 1, "retain_minimum" => 2, _ => throw new DatasetException("Unsupported entity dimensions refresh") });
                    Text(entry.ScaleAttribute);
                    WriteVarInt(stream, entry.Variants.Length);
                    foreach (EntityGeometryVariant variant in entry.Variants.OrderBy(row => row.Selector).ThenBy(row => row.Pose))
                    {
                        stream.WriteByte((byte)variant.Selector);
                        stream.WriteByte((byte)variant.Pose);
                        BinaryPrimitives.WriteSingleBigEndian(dimensions, variant.Width);
                        BinaryPrimitives.WriteSingleBigEndian(dimensions[4..], variant.Height);
                        stream.Write(dimensions);
                        stream.WriteByte((byte)((variant.Fixed ? 1 : 0) | (variant.BlocksBuilding ? 2 : 0)));
                    }
                }
            }
        }
        return InternBlob("blockinteractions", version.Protocol, stream.ToArray());
    }
}
