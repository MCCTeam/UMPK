using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

/// <summary>Reads generated exact voxel grids once per protocol. Every returned grid shares immutable pooled storage.</summary>
internal sealed class JavaBlockCollisionGrids
{
    private readonly BlockCollisionGrid[] _groups;
    private readonly int[] _states;

    internal JavaBlockCollisionGrids(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || (bytes.Length == 1 && bytes[0] == 0))
        {
            _groups = [];
            _states = [];
            return;
        }
        var reader = new PacketReader(bytes);
        if (reader.ReadVarInt() != 1)
            throw new ProtocolViolationException("Unsupported generated collision-grid schema.");
        _groups = new BlockCollisionGrid[Count(ref reader, 5)];
        if (_groups.Length == 0)
            throw new ProtocolViolationException("A measured collision-grid table must contain a shape group.");
        for (int index = 0; index < _groups.Length; index++)
        {
            byte canonical = reader.ReadByte();
            if (canonical > 1)
                throw new ProtocolViolationException("Invalid generated collision-grid identity.");
            double[] x = Coordinates(ref reader), y = Coordinates(ref reader), z = Coordinates(ref reader);
            int[] cells = new int[Count(ref reader, 1)];
            for (int cell = 0; cell < cells.Length; cell++)
                cells[cell] = reader.ReadVarInt();
            Aabb[] boxes = new Aabb[Count(ref reader, 48)];
            for (int box = 0; box < boxes.Length; box++)
            {
                double minX = reader.ReadDouble(), minY = reader.ReadDouble(), minZ = reader.ReadDouble();
                double maxX = reader.ReadDouble(), maxY = reader.ReadDouble(), maxZ = reader.ReadDouble();
                if (minX > maxX || minY > maxY || minZ > maxZ)
                    throw new ProtocolViolationException("Invalid generated collision-grid bounding box.");
                boxes[box] = new Aabb(minX, minY, minZ, maxX, maxY, maxZ);
            }
            try
            {
                _groups[index] = new BlockCollisionGrid(x, y, z, cells, boxes, canonical == 1);
            }
            catch (ArgumentException error)
            {
                throw new ProtocolViolationException("Invalid generated collision grid.", error);
            }
        }
        _states = new int[Count(ref reader, 1)];
        if (_states.Length == 0)
            throw new ProtocolViolationException("A measured collision-grid table must contain state references.");
        for (int index = 0; index < _states.Length; index++)
        {
            int group = reader.ReadVarInt();
            if ((uint)group >= (uint)_groups.Length)
                throw new ProtocolViolationException("Invalid generated collision-grid state reference.");
            _states[index] = group;
        }
        if (reader.Remaining != 0)
            throw new ProtocolViolationException("Trailing generated collision-grid bytes.");
    }

    internal BlockCollisionGrid? Get(int stateId) => (uint)stateId < (uint)_states.Length ? _groups[_states[stateId]] : null;

    private static int Count(ref PacketReader reader, int minimumBytes)
    {
        int count = reader.ReadVarInt();
        if (count < 0 || count > reader.Remaining / minimumBytes)
            throw new ProtocolViolationException("Invalid generated collision-grid count.");
        return count;
    }

    private static double[] Coordinates(ref PacketReader reader)
    {
        double[] coordinates = new double[Count(ref reader, 8)];
        for (int index = 0; index < coordinates.Length; index++)
            coordinates[index] = reader.ReadDouble();
        return coordinates;
    }
}
