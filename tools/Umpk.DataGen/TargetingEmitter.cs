using System.Buffers.Binary;
using System.Text;

namespace Umpk.DataGen;

internal sealed partial class Emitter
{
    private string InternBlockTargeting(VersionData version)
    {
        using MemoryStream stream = new();
        TargetingTable? targeting = TargetingTable.Load(version);
        if (targeting is null)
        {
            WriteVarInt(stream, 0);
            return InternBlob("blocktargeting", version.Protocol, stream.ToArray());
        }
        void Number(double value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
            stream.Write(bytes);
        }
        bool contextual = targeting.ContextNodes.Count != 0 || targeting.Shapes.Any(shape => shape.ClipModel.Length != 0);
        WriteVarInt(stream, contextual ? 3 : 2);
        stream.WriteByte(targeting.ClipModel switch { "aabb" => 0, "voxel_forward" => 1, "voxel" => 2, _ => throw new DatasetException("Unknown targeting clip model") });
        WriteVarInt(stream, targeting.StateCount);
        WriteVarInt(stream, targeting.Shapes.Count);
        foreach (TargetingShape shape in targeting.Shapes)
        {
            if (contextual)
                stream.WriteByte(shape.ClipModel == "aabb_list" ? (byte)3 : (byte)255);
            stream.WriteByte(shape.Offset switch { "none" => 0, "xz" => 1, "xyz" => 2, _ => throw new DatasetException("Unknown targeting offset") });
            Number(shape.Horizontal);
            Number(shape.Vertical);
            WriteVarInt(stream, shape.Boxes.Count);
            foreach (double[] box in shape.Boxes)
                foreach (double coordinate in box)
                    Number(coordinate);
        }
        WriteVarInt(stream, targeting.Ranges.Count);
        foreach (TargetingRange range in targeting.Ranges)
        {
            WriteVarInt(stream, range.First);
            WriteVarInt(stream, range.Count);
            WriteVarInt(stream, range.Outline);
            WriteVarInt(stream, range.Interaction);
            byte[] item = Encoding.UTF8.GetBytes(range.HeldItem);
            WriteVarInt(stream, item.Length);
            stream.Write(item);
            WriteVarInt(stream, range.HeldOutline);
        }
        if (contextual)
        {
            void ContextRanges(IReadOnlyList<TargetingContextRange> ranges)
            {
                WriteVarInt(stream, ranges.Count);
                foreach (TargetingContextRange range in ranges)
                {
                    WriteVarInt(stream, range.First);
                    WriteVarInt(stream, range.Count);
                    WriteVarInt(stream, range.Target);
                }
            }
            WriteVarInt(stream, targeting.ContextNodes.Count);
            foreach (TargetingContextNode node in targeting.ContextNodes)
            {
                foreach (int offset in node.Offset)
                    WriteVarInt(stream, offset);
                ContextRanges(node.Ranges);
            }
            ContextRanges(targeting.ContextRoots);
        }
        return InternBlob("blocktargeting", version.Protocol, stream.ToArray());
    }
}
