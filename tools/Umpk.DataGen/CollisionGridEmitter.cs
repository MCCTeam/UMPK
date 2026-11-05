using System.Buffers.Binary;
using System.Globalization;

namespace Umpk.DataGen;

internal sealed partial class Emitter
{
    private string InternBlockCollisionGrids(VersionData version)
    {
        using MemoryStream stream = new();
        CollisionGridTable? table = CollisionGridTable.Load(version);
        CollisionGridTable? offsets = CollisionGridTable.LoadOffsets(version);
        WriteVarInt(stream, table is null ? 0 : offsets is null ? 1 : 2);
        if (table is not null)
        {
            void Number(double value)
            {
                Span<byte> bytes = stackalloc byte[8];
                BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
                stream.Write(bytes);
            }
            List<CollisionGridRow> groups = [.. table.Groups];
            Dictionary<string, int> states = new(table.States);
            if (offsets is not null)
            {
                int first = groups.Count;
                groups.AddRange(offsets.Groups);
                foreach ((string state, int group) in offsets.States)
                    states[state] = first + group;
                HashSet<int> used = [.. states.Values];
                Dictionary<int, int> remap = [];
                List<CollisionGridRow> retained = [];
                for (int index = 0; index < groups.Count; index++)
                    if (used.Contains(index))
                    {
                        remap[index] = retained.Count;
                        retained.Add(groups[index]);
                    }
                groups = retained;
                foreach (string state in states.Keys.ToArray())
                    states[state] = remap[states[state]];
            }
            WriteVarInt(stream, groups.Count);
            foreach (CollisionGridRow group in groups)
            {
                stream.WriteByte(group.Canonical ? (byte)1 : (byte)0);
                if (offsets is not null)
                    Number(group.MaxHorizontalOffset);
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
                WriteVarInt(stream, states[state.ToString(CultureInfo.InvariantCulture)]);
        }
        return InternBlob("blockcollisiongrids", version.Protocol, stream.ToArray());
    }
}
