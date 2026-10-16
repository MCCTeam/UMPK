using System.Buffers.Binary;
using System.Globalization;

namespace Umpk.DataGen;

internal sealed partial class Emitter
{
    private string InternBlockCollisionGrids(VersionData version)
    {
        using MemoryStream stream = new();
        CollisionGridTable? table = CollisionGridTable.Load(version);
        WriteVarInt(stream, table is null ? 0 : 1);
        if (table is not null)
        {
            void Number(double value)
            {
                Span<byte> bytes = stackalloc byte[8];
                BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
                stream.Write(bytes);
            }
            WriteVarInt(stream, table.Groups.Count);
            foreach (CollisionGridRow group in table.Groups)
            {
                stream.WriteByte(group.Canonical ? (byte)1 : (byte)0);
                foreach (double[] axis in group.Planes)
                {
                    WriteVarInt(stream, axis.Length);
                    foreach (double coordinate in axis)
                        Number(coordinate);
                }
                WriteVarInt(stream, group.Cells.Length);
                foreach (int cell in group.Cells)
                    WriteVarInt(stream, cell);
                WriteVarInt(stream, group.Boxes.Length);
                foreach (double[] box in group.Boxes)
                    foreach (double coordinate in box)
                        Number(coordinate);
            }
            WriteVarInt(stream, table.StateCount);
            for (int state = 0; state < table.StateCount; state++)
                WriteVarInt(stream, table.States[state.ToString(CultureInfo.InvariantCulture)]);
        }
        return InternBlob("blockcollisiongrids", version.Protocol, stream.ToArray());
    }
}
