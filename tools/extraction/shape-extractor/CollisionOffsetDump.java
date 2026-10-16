import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Extracts original native coordinate delegates for every colliding position-offset state. Compile with MiningDump and PushDump. Arguments: mappings-or-none, output.json. */
public final class CollisionOffsetDump {
    private static final String SHAPE = "net.minecraft.world.phys.shapes.VoxelShape";
    private static final String POSITION = "net.minecraft.core.BlockPos";
    private static Method coordinates, boxes, filled, collision, offset;
    private static Field discrete, delegate, amount;
    private static Field[] vector, bounds;
    private static Object empty, full, context;
    private static boolean needsWorld;

    private static Method method(String owner, String name, String args) throws Exception {
        return MiningDump.method(owner, name, args);
    }

    private static Map<String, Object> grid(Object shape) throws Exception {
        List<List<Double>> planes = new ArrayList<>();
        for (String axis : new String[]{"X", "Y", "Z"}) {
            List<Double> values = new ArrayList<>();
            for (Object value : (Iterable<?>)coordinates.invoke(shape, MiningDump.field("net.minecraft.core.Direction$Axis", axis)))
                values.add(((Number)value).doubleValue());
            planes.add(values);
        }
        int nx = planes.get(0).size() - 1, ny = planes.get(1).size() - 1, nz = planes.get(2).size() - 1;
        List<Integer> cells = new ArrayList<>();
        Object voxel = discrete.get(shape);
        for (int x = 0; x < nx; x++)
            for (int y = 0; y < ny; y++)
                for (int z = 0; z < nz; z++)
                    if ((boolean)filled.invoke(voxel, x, y, z))
                        cells.add((x * ny + y) * nz + z);
        List<Object> aabbs = new ArrayList<>();
        for (Object box : (List<?>)boxes.invoke(shape)) {
            List<Double> values = new ArrayList<>();
            for (Field bound : bounds)
                values.add(bound.getDouble(box));
            aabbs.add(values);
        }
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("canonical_full_block", shape == full);
        result.put("planes", planes);
        result.put("cells", cells);
        result.put("boxes", aabbs);
        return result;
    }

    private static double[] displacement(Object state, Object position) throws Exception {
        Object value = needsWorld ? offset.invoke(state, empty, position) : offset.invoke(state, position);
        double[] result = new double[3];
        for (int axis = 0; axis < 3; axis++) {
            result[axis] = vector[axis].getDouble(value);
            if (!Double.isFinite(result[axis]))
                throw new IllegalStateException("Native offset is not finite");
        }
        if (result[1] != 0)
            throw new IllegalStateException("A colliding vertical offset requires a separate native model");
        return result;
    }

    private static Object originalDelegateShape(Object shape, double[] shift) throws Exception {
        Object[] lists = new Object[3];
        int index = 0;
        for (String axis : new String[]{"X", "Y", "Z"}) {
            Object list = coordinates.invoke(shape, MiningDump.field("net.minecraft.core.Direction$Axis", axis));
            if (!MiningDump.cls("net.minecraft.world.phys.shapes.OffsetDoubleList").isInstance(list)
                || Double.doubleToLongBits(amount.getDouble(list)) != Double.doubleToLongBits(shift[index]))
                throw new IllegalStateException("Native collision does not expose the expected position-offset delegate on " + axis);
            lists[index++] = delegate.get(list);
        }
        Class<?> list = Class.forName("it.unimi.dsi.fastutil.doubles.DoubleList");
        Constructor<?> constructor = MiningDump.cls("net.minecraft.world.phys.shapes.ArrayVoxelShape").getDeclaredConstructor(
            MiningDump.cls("net.minecraft.world.phys.shapes.DiscreteVoxelShape"), list, list, list);
        constructor.setAccessible(true);
        return constructor.newInstance(discrete.get(shape), lists[0], lists[1], lists[2]);
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 2)
            throw new IllegalArgumentException("Usage: CollisionOffsetDump <mappings-or-none> <output.json>");
        Path output = Paths.get(args[1]);
        if (Files.exists(output))
            throw new FileAlreadyExistsException(output.toString());
        PushDump.loader = CollisionOffsetDump.class.getClassLoader();
        if (!args[0].equals("none"))
            PushDump.parse(Paths.get(args[0]));
        Method detect = MiningDump.optional("net.minecraft.SharedConstants", "tryDetectVersion", "");
        if (detect != null)
            detect.invoke(null);
        method("net.minecraft.server.Bootstrap", "bootStrap", "").invoke(null);
        MiningDump.initializeComponents();
        empty = MiningDump.field("net.minecraft.world.level.EmptyBlockGetter", "INSTANCE");
        full = method("net.minecraft.world.phys.shapes.Shapes", "block", "").invoke(null);
        coordinates = method(SHAPE, "getCoords", "net.minecraft.core.Direction$Axis");
        boxes = method(SHAPE, "toAabbs", "");
        filled = method("net.minecraft.world.phys.shapes.DiscreteVoxelShape", "isFullWide", "int,int,int");
        discrete = PushDump.field(SHAPE, "shape");
        delegate = PushDump.field("net.minecraft.world.phys.shapes.OffsetDoubleList", "delegate");
        amount = PushDump.field("net.minecraft.world.phys.shapes.OffsetDoubleList", "offset");
        vector = new Field[3];
        int axis = 0;
        for (String name : new String[]{"x", "y", "z"})
            vector[axis++] = PushDump.field("net.minecraft.world.phys.Vec3", name);
        bounds = new Field[6];
        int bound = 0;
        for (String name : new String[]{"minX", "minY", "minZ", "maxX", "maxY", "maxZ"})
            bounds[bound++] = PushDump.field("net.minecraft.world.phys.AABB", name);
        offset = PushDump.stateMethod("getOffset", POSITION);
        if (offset == null) {
            offset = PushDump.stateMethod("getOffset", "net.minecraft.world.level.BlockGetter," + POSITION);
            needsWorld = true;
        }
        if (offset == null)
            throw new IllegalStateException("Native position-offset accessor is absent");
        context = method("net.minecraft.world.phys.shapes.CollisionContext", "empty", "").invoke(null);
        collision = PushDump.stateMethod("getCollisionShape", "net.minecraft.world.level.BlockGetter," + POSITION + ",net.minecraft.world.phys.shapes.CollisionContext");
        Method getBlock = PushDump.stateMethod("getBlock", "");
        Method horizontal = MiningDump.optional("net.minecraft.world.level.block.state.BlockBehaviour", "getMaxHorizontalOffset", "");
        if (horizontal == null)
            horizontal = MiningDump.optional("net.minecraft.world.level.block.Block", "getMaxHorizontalOffset", "");
        Method isEmpty = method(SHAPE, "isEmpty", "");
        Method move = method(SHAPE, "move", "double,double,double");
        Constructor<?> position = MiningDump.cls(POSITION).getConstructor(int.class, int.class, int.class);
        int[][] positions = {{0,0,0},{0,64,0},{17,63,-31},{-30000000,64,29999999},{10000000,64,-10000000},{600,100,601},{-12345,63,67890}};
        Object origin = position.newInstance(0, 0, 0);
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Method json = gson.getClass().getMethod("toJson", Object.class);
        Map<Integer, Integer> states = new TreeMap<>();
        List<Object> groups = new ArrayList<>();
        Map<String, Integer> signatures = new LinkedHashMap<>();
        int stateCount = 0;
        for (Object state : (Iterable<?>)MiningDump.field("net.minecraft.world.level.block.Block", "BLOCK_STATE_REGISTRY")) {
            stateCount++;
            Object shift = needsWorld ? offset.invoke(state, empty, origin) : offset.invoke(state, origin);
            if (vector[0].getDouble(shift) == 0 && vector[1].getDouble(shift) == 0 && vector[2].getDouble(shift) == 0)
                continue;
            Object shape = collision.invoke(state, empty, origin, context);
            if ((boolean)isEmpty.invoke(shape))
                continue;
            double[] delta = displacement(state, origin);
            Object base = originalDelegateShape(shape, delta);
            // The older Block.getOffset formula is bounded by (nibble / 15F - 0.5) * 0.5 and has no max-offset accessor: its exact horizontal limit is 0.25.
            double limit = horizontal == null ? 0.25 : ((Number)horizontal.invoke(getBlock.invoke(state))).doubleValue();
            if (!Double.isFinite(limit) || limit <= 0)
                throw new IllegalStateException("Native horizontal offset limit must be positive and finite");
            for (int[] at : positions) {
                Object where = position.newInstance(at[0], at[1], at[2]);
                double[] current = displacement(state, where);
                Object measured = collision.invoke(state, empty, where, context);
                Object rebuilt = move.invoke(base, current[0], current[1], current[2]);
                if (!json.invoke(gson, grid(measured)).equals(json.invoke(gson, grid(rebuilt))))
                    throw new IllegalStateException("Original delegates do not reconstruct native collision at " + Arrays.toString(at));
            }
            Map<String, Object> row = grid(base);
            row.put("max_horizontal_offset", limit);
            String signature = (String)json.invoke(gson, row);
            Integer group = signatures.get(signature);
            if (group == null) {
                group = groups.size();
                signatures.put(signature, group);
                groups.add(row);
            }
            int id = (Integer)method("net.minecraft.world.level.block.Block", "getId", "net.minecraft.world.level.block.state.BlockState").invoke(null, state);
            if (states.put(id, group) != null)
                throw new IllegalStateException("Duplicate native block-state id");
        }
        if (states.isEmpty())
            throw new IllegalStateException("No colliding offset states were measured");
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("schema", 1);
        result.put("scope", "Original native collision coordinate delegates with measured horizontal offset limits; rebuilt against native shapes at seven positions; no dynamic block-entity or entity-context variants");
        result.put("cell_order", "(x * y_size + y) * z_size + z");
        result.put("state_count", stateCount);
        result.put("states", states);
        result.put("groups", groups);
        Path temporary = output.resolveSibling(output.getFileName() + ".partial");
        Files.writeString(temporary, (String)json.invoke(gson, result) + "\n", StandardCharsets.UTF_8, StandardOpenOption.CREATE_NEW);
        Files.move(temporary, output);
        System.out.println("collision offsets: registry=" + stateCount + " states=" + states.size() + " groups=" + groups.size());
    }
}
