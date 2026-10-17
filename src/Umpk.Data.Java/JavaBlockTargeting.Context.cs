using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

internal sealed partial class JavaBlockTargeting
{
    private sealed record ContextNode(BlockPos Offset, int[] Targets);

    private int ResolveContext(Umpk.Game.World.World world, BlockPos position, int index)
    {
        for (int visited = 0; visited < _contextNodes.Length; visited++)
        {
            ContextNode node = _contextNodes[index];
            int neighbor = world.GetBlockStateId(position.Offset(node.Offset.X, node.Offset.Y, node.Offset.Z));
            int target = node.Targets[(uint)neighbor < (uint)node.Targets.Length ? neighbor : 0];
            if (target >= 0)
                return target;
            index = ~target;
        }
        throw new ProtocolViolationException("Generated targeting context did not terminate.");
    }

    private static ContextNode[] ReadContextNodes(ref PacketReader reader, int states, int shapes)
    {
        ContextNode[] nodes = new ContextNode[Count(ref reader, 4)];
        for (int index = 0; index < nodes.Length; index++)
        {
            int x = reader.ReadVarInt(), y = reader.ReadVarInt(), z = reader.ReadVarInt();
            if (x is < -1 or > 1 || y is < -1 or > 1 || z is < -1 or > 1 || (x == 0 && y == 0 && z == 0))
                throw new ProtocolViolationException("Invalid generated targeting context offset.");
            int ranges = Count(ref reader, 3);
            int[] targets = new int[states];
            int next = 0;
            for (int range = 0; range < ranges; range++)
            {
                int first = reader.ReadVarInt(), count = reader.ReadVarInt(), target = reader.ReadVarInt();
                if (first != next || count <= 0 || count > states - next || (target >= 0 ? target >= shapes : ~target <= index || ~target >= nodes.Length))
                    throw new ProtocolViolationException("Invalid generated targeting context range or target.");
                targets.AsSpan(next, count).Fill(target);
                next += count;
            }
            if (next != states)
                throw new ProtocolViolationException("Incomplete generated targeting context coverage.");
            nodes[index] = new ContextNode(new BlockPos(x, y, z), targets);
        }
        return nodes;
    }

    private static int[] ReadContextRoots(ref PacketReader reader, int states, int nodes)
    {
        int ranges = Count(ref reader, 3);
        int[] roots = new int[states];
        Array.Fill(roots, -1);
        int next = 0;
        for (int range = 0; range < ranges; range++)
        {
            int first = reader.ReadVarInt(), count = reader.ReadVarInt(), root = reader.ReadVarInt();
            if (first < next || first >= states || count <= 0 || count > states - first || (uint)root >= (uint)nodes)
                throw new ProtocolViolationException("Invalid generated targeting context root.");
            roots.AsSpan(first, count).Fill(root);
            next = first + count;
        }
        return roots;
    }
}
