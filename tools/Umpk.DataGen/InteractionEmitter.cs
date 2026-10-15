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
        WriteVarInt(stream, obstruction?.FixedBoxes.Count > 0 ? 3 : obstruction is not null ? 2 : table is not null ? 1 : 0);
        if (table is not null || obstruction is not null)
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
            if (obstruction is not null)
            {
                WriteVarInt(stream, obstruction.Entities.Count);
                foreach ((string entity, int flags) in obstruction.Entities.OrderBy(row => row.Key, StringComparer.Ordinal))
                {
                    Text(entity);
                    stream.WriteByte((byte)flags);
                }
                if (obstruction.FixedBoxes.Count > 0)
                {
                    WriteVarInt(stream, obstruction.FixedBoxes.Count);
                    Span<byte> dimensions = stackalloc byte[8];
                    foreach (var entity in obstruction.FixedBoxes.OrderBy(row => row.Key, StringComparer.Ordinal))
                    {
                        Text(entity.Key);
                        BinaryPrimitives.WriteSingleBigEndian(dimensions, entity.Value.Width);
                        BinaryPrimitives.WriteSingleBigEndian(dimensions[4..], entity.Value.Height);
                        stream.Write(dimensions);
                    }
                }
            }
        }
        return InternBlob("blockinteractions", version.Protocol, stream.ToArray());
    }
}
