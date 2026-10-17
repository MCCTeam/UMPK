import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Extracts exact default block collision grids from an unchanged vanilla jar. Compile with MiningDump and PushDump. Arguments: mappings-or-none, output.json. This uses EmptyBlockGetter and BlockPos.ZERO; block-entity, neighbor and entity-context variants require separate measurements. */
public final class CollisionGridDump {
    private static Method method(String owner, String name, String args) throws Exception {
        return MiningDump.method(owner, name, args);
    }

    private static Object field(String owner, String name) throws Exception {
        return MiningDump.field(owner, name);
    }

    private static void initialize(String mappings) throws Exception {
        PushDump.loader = CollisionGridDump.class.getClassLoader();
        if (!mappings.equals("none"))
            PushDump.parse(Paths.get(mappings));
        Method detect = MiningDump.optional("net.minecraft.SharedConstants", "tryDetectVersion", "");
        if (detect != null)
            detect.invoke(null);
        method("net.minecraft.server.Bootstrap", "bootStrap", "").invoke(null);
        MiningDump.identifierClass = MiningDump.clsOrNull("net.minecraft.resources.Identifier");
        if (MiningDump.identifierClass == null)
            MiningDump.identifierClass = MiningDump.cls("net.minecraft.resources.ResourceLocation");
        MiningDump.initializeComponents();
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 2)
            throw new IllegalArgumentException("Usage: CollisionGridDump <mappings-or-none> <output.json>");
        Path output = Paths.get(args[1]);
        if (Files.exists(output))
            throw new FileAlreadyExistsException(output.toString());
        initialize(args[0]);

        Object empty = field("net.minecraft.world.level.EmptyBlockGetter", "INSTANCE");
        Object origin = field("net.minecraft.core.BlockPos", "ZERO");
        Object full = method("net.minecraft.world.phys.shapes.Shapes", "block", "").invoke(null);
        Method collision = PushDump.stateMethod("getCollisionShape", "net.minecraft.world.level.BlockGetter,net.minecraft.core.BlockPos");
        Method boxes = method("net.minecraft.world.phys.shapes.VoxelShape", "toAabbs", "");
        Method coordinates = method("net.minecraft.world.phys.shapes.VoxelShape", "getCoords", "net.minecraft.core.Direction$Axis");
        Field discrete = PushDump.field("net.minecraft.world.phys.shapes.VoxelShape", "shape");
        Method filled = method("net.minecraft.world.phys.shapes.DiscreteVoxelShape", "isFullWide", "int,int,int");
        Method idOf = method("net.minecraft.world.level.block.Block", "getId", "net.minecraft.world.level.block.state.BlockState");
        Field[] bounds = new Field[6];
        String[] names = {"minX", "minY", "minZ", "maxX", "maxY", "maxZ"};
        for (int index = 0; index < names.length; index++)
            bounds[index] = PushDump.field("net.minecraft.world.phys.AABB", names[index]);

        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Method json = gson.getClass().getMethod("toJson", Object.class);
        Map<Integer, Integer> states = new TreeMap<>();
        Map<String, Integer> signatures = new LinkedHashMap<>();
        List<Object> groups = new ArrayList<>();
        for (Object state : (Iterable<?>)field("net.minecraft.world.level.block.Block", "BLOCK_STATE_REGISTRY")) {
            int id = (int)idOf.invoke(null, state);
            try {
                Object shape = collision.invoke(state, empty, origin);
                List<Object> aabbs = new ArrayList<>();
                for (Object box : (List<?>)boxes.invoke(shape)) {
                    List<Double> values = new ArrayList<>();
                    for (Field bound : bounds)
                        values.add(finite(bound.getDouble(box)));
                    aabbs.add(values);
                }
                List<List<Double>> planes = new ArrayList<>();
                for (String axis : new String[]{"X", "Y", "Z"}) {
                    List<Double> values = new ArrayList<>();
                    Object nativeCoordinates = coordinates.invoke(shape, field("net.minecraft.core.Direction$Axis", axis));
                    for (Object value : (Iterable<?>)nativeCoordinates)
                        values.add(finite(((Number)value).doubleValue()));
                    planes.add(values);
                }
                int nx = planes.get(0).size() - 1;
                int ny = planes.get(1).size() - 1;
                int nz = planes.get(2).size() - 1;
                Object voxel = discrete.get(shape);
                List<Integer> cells = new ArrayList<>();
                for (int x = 0; x < nx; x++)
                    for (int y = 0; y < ny; y++)
                        for (int z = 0; z < nz; z++)
                            if ((boolean)filled.invoke(voxel, x, y, z))
                                cells.add((x * ny + y) * nz + z);

                Map<String, Object> grid = new LinkedHashMap<>();
                grid.put("canonical_full_block", shape == full);
                grid.put("planes", planes);
                grid.put("cells", cells);
                grid.put("boxes", aabbs);
                String signature = (String)json.invoke(gson, grid);
                Integer group = signatures.get(signature);
                if (group == null) {
                    group = groups.size();
                    signatures.put(signature, group);
                    groups.add(grid);
                }
                if (states.put(id, group) != null)
                    throw new IllegalStateException("Duplicate native block-state id: " + id);
            } catch (InvocationTargetException error) {
                throw new IllegalStateException("Cannot extract native collision grid for state " + id, error.getCause());
            }
        }
        if (states.isEmpty())
            throw new IllegalStateException("The native block-state registry is empty");

        Map<String, Object> result = new LinkedHashMap<>();
        result.put("schema", 1);
        result.put("scope", "Native default collision shapes with EmptyBlockGetter and BlockPos.ZERO; no dynamic block-entity, neighbor or entity-context variants");
        result.put("cell_order", "(x * y_size + y) * z_size + z");
        result.put("states", states);
        result.put("groups", groups);
        Path temporary = output.resolveSibling(output.getFileName() + ".partial");
        Files.writeString(temporary, (String)json.invoke(gson, result) + "\n", StandardCharsets.UTF_8, StandardOpenOption.CREATE_NEW);
        Files.move(temporary, output);
        System.out.println("collision grids: states=" + states.size() + " groups=" + groups.size());
    }

    private static double finite(double value) {
        if (!Double.isFinite(value))
            throw new IllegalStateException("The native collision grid contains a non-finite coordinate: " + value);
        return value;
    }
}
