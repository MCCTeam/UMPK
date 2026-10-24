using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

/// <summary>Artifact-measured outline geometry. Offsets use vanilla's unchecked seed arithmetic and float division before double coordinate arithmetic.</summary>
internal sealed partial class JavaBlockTargeting
{
    private sealed record Shape(Aabb[] Boxes, byte OffsetKind, double Horizontal, double Vertical, byte ClipModel)
    {
        public Aabb[] AtOrigin { get; } = Translate(Boxes, Offset(OffsetKind, Horizontal, Vertical, BlockPos.Zero));
        public Vec3d At(BlockPos position) => Offset(OffsetKind, Horizontal, Vertical, position);

        private static Aabb[] Translate(Aabb[] boxes, Vec3d offset)
            => offset == Vec3d.Zero ? boxes : [.. boxes.Select(box => box.Move(offset.X, offset.Y, offset.Z))];
    }

    private readonly record struct Reference(int Outline, int Interaction, Identifier? HeldItem, int HeldOutline);
    private readonly Shape[] _shapes;
    private readonly Reference[] _states;
    private readonly BlockRayClipModel _clipModel;
    private readonly ContextNode[] _contextNodes;
    private readonly int[] _contextRoots;

    private JavaBlockTargeting(Shape[] shapes, Reference[] states, BlockRayClipModel clipModel = BlockRayClipModel.VoxelOppositeInside, ContextNode[]? nodes = null, int[]? roots = null)
    {
        _shapes = shapes;
        _states = states;
        _clipModel = clipModel;
        _contextNodes = nodes ?? [];
        _contextRoots = roots ?? [];
    }

    public bool HasData => _states.Length != 0;

    public static JavaBlockTargeting Create(ReadOnlySpan<byte> bytes)
    {
        var reader = new PacketReader(bytes);
        int schema = reader.ReadVarInt();
        if (schema == 0 && reader.Remaining == 0)
            return new JavaBlockTargeting([], []);
        if (schema is not (1 or 2 or 3))
            throw new ProtocolViolationException("Unsupported generated targeting schema.");
        var clipModel = schema >= 2 ? (BlockRayClipModel)reader.ReadByte() : BlockRayClipModel.VoxelOppositeInside;
        if ((uint)clipModel > 2)
            throw new ProtocolViolationException("Unsupported generated targeting clip model.");
        int stateCount = reader.ReadVarInt();
        if (stateCount <= 0 || stateCount > 0x1000000)
            throw new ProtocolViolationException("Invalid generated targeting state count.");
        int shapeCount = Count(ref reader, schema >= 3 ? 19 : 18);
        Shape[] shapes = new Shape[shapeCount];
        for (int index = 0; index < shapes.Length; index++)
        {
            byte model = schema >= 3 ? reader.ReadByte() : (byte)255;
            if (model is not (3 or 255))
                throw new ProtocolViolationException("Invalid generated per-shape clip model.");
            byte offset = reader.ReadByte();
            double horizontal = reader.ReadDouble(), vertical = reader.ReadDouble();
            if (offset > 2 || !double.IsFinite(horizontal) || !double.IsFinite(vertical) || horizontal < 0 || vertical < 0)
                throw new ProtocolViolationException("Invalid generated targeting offset.");
            Aabb[] boxes = new Aabb[Count(ref reader, 48)];
            for (int box = 0; box < boxes.Length; box++)
            {
                double minX = reader.ReadDouble(), minY = reader.ReadDouble(), minZ = reader.ReadDouble();
                double maxX = reader.ReadDouble(), maxY = reader.ReadDouble(), maxZ = reader.ReadDouble();
                if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(minZ) || !double.IsFinite(maxX) || !double.IsFinite(maxY) || !double.IsFinite(maxZ) || minX > maxX || minY > maxY || minZ > maxZ)
                    throw new ProtocolViolationException("Invalid generated targeting box.");
                boxes[box] = new Aabb(minX, minY, minZ, maxX, maxY, maxZ);
            }
            shapes[index] = new Shape(boxes, offset, horizontal, vertical, model);
        }
        int ranges = Count(ref reader, 6);
        Reference[] states = new Reference[stateCount];
        int next = 0;
        for (int range = 0; range < ranges; range++)
        {
            int first = reader.ReadVarInt(), count = reader.ReadVarInt();
            int outline = Index(ref reader, shapes.Length), interaction = Index(ref reader, shapes.Length);
            string item = reader.ReadString();
            int held = Index(ref reader, shapes.Length);
            if (first != next || count <= 0 || count > states.Length - next || (item.Length == 0 && held != outline))
                throw new ProtocolViolationException("Generated targeting ranges do not cover consecutive states.");
            var reference = new Reference(outline, interaction, item.Length == 0 ? null : Identifier.Parse(item), held);
            states.AsSpan(next, count).Fill(reference);
            next += count;
        }
        ContextNode[] nodes = schema >= 3 ? ReadContextNodes(ref reader, stateCount, shapes.Length) : [];
        int[] roots = schema >= 3 ? ReadContextRoots(ref reader, stateCount, nodes.Length) : [];
        if (next != states.Length || reader.Remaining != 0)
            throw new ProtocolViolationException("Generated targeting table is incomplete or has trailing bytes.");
        return new JavaBlockTargeting(shapes, states, clipModel, nodes, roots);
    }

    public ReadOnlySpan<Aabb> AtOrigin(int state)
        => (uint)state < (uint)_states.Length ? _shapes[_states[state].Outline].AtOrigin : [];

    public bool TryGet(int state, BlockPos position, Identifier item, Umpk.Game.World.World world, out BlockTargetingShape result)
    {
        result = default;
        if ((uint)state >= (uint)_states.Length)
            return false;
        Reference reference = _states[state];
        int selected = reference.HeldItem is { } required && required == item ? reference.HeldOutline : reference.Outline;
        if (_contextRoots.Length != 0 && _contextRoots[state] >= 0)
            selected = ResolveContext(world, position, _contextRoots[state]);
        Shape outline = _shapes[selected];
        Shape interaction = _shapes[reference.Interaction];
        result = new BlockTargetingShape(outline.Boxes, outline.At(position), interaction.Boxes, interaction.At(position)) { ClipModel = outline.ClipModel == 255 ? _clipModel : (BlockRayClipModel)outline.ClipModel };
        return true;
    }

    private static Vec3d Offset(byte kind, double horizontal, double vertical, BlockPos position)
    {
        if (kind == 0)
            return Vec3d.Zero;
        long seed = unchecked((long)(position.X * 3129871) ^ (long)position.Z * 116129781L);
        seed = unchecked(seed * seed * 42317861L + seed * 11L) >> 16;
        double x = Math.Clamp(((float)(seed & 15L) / 15.0F - 0.5) * 0.5, -horizontal, horizontal);
        double z = Math.Clamp(((float)((seed >> 8) & 15L) / 15.0F - 0.5) * 0.5, -horizontal, horizontal);
        double y = kind == 2 ? ((float)((seed >> 4) & 15L) / 15.0F - 1.0) * vertical : 0;
        return new Vec3d(x, y, z);
    }

    private static int Count(ref PacketReader reader, int minimum)
    {
        int count = reader.ReadVarInt();
        if (count < 0 || count > reader.Remaining / minimum)
            throw new ProtocolViolationException("Generated targeting count exceeds its available rows.");
        return count;
    }

    private static int Index(ref PacketReader reader, int count)
    {
        int index = reader.ReadVarInt();
        if ((uint)index >= (uint)count)
            throw new ProtocolViolationException("Generated targeting shape index is out of range.");
        return index;
    }
}
