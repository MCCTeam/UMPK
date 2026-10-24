import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures unchanged native collision grids. For 1.13 only, the owned reader supplies air, its native fluid state, null block entities and the original reader's constant defaults. */
public final class LegacyCollisionGridDump {
    static Map<String,Object> binding;
    static Object gson;
    static String name(String key) { return (String)binding.get(key); }
    static Class<?> type(String key) throws Exception { return Class.forName(name(key)); }
    static Map<String,Object> map(Object value) { return (Map<String,Object>)value; }
    static Method method(Class<?> owner, String name, Class<?>... args) throws Exception {
        Method result = owner.getDeclaredMethod(name, args);
        result.setAccessible(true);
        return result;
    }
    static Field field(Class<?> owner, String name) throws Exception {
        Field result = owner.getDeclaredField(name);
        result.setAccessible(true);
        return result;
    }
    static double finite(double value) {
        if (!Double.isFinite(value)) throw new IllegalStateException("Non-finite native plane or bound");
        return value;
    }
    public static void main(String[] args) throws Exception {
        if (args.length != 3) throw new IllegalArgumentException("bindings.json original-class-origin output.json");
        Path output = Paths.get(args[2]);
        if (Files.exists(output)) throw new FileAlreadyExistsException(output.toString());
        gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        try (Reader input = Files.newBufferedReader(Paths.get(args[0]))) {
            binding = map(gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, input, Map.class));
        }
        type("bootstrap").getMethod(name("boot")).invoke(null);
        Class<?> state = type("state"), reader = type("reader"), position = type("position"), shapeType = type("shape");
        Map<String,Object> emptyBinding = map(binding.get("empty"));
        Object empty;
        if (emptyBinding.containsKey("type")) {
            empty = Class.forName((String)emptyBinding.get("type")).getField((String)emptyBinding.get("field")).get(null);
        } else {
            Object airBlock = type("blocks").getField(name("air")).get(null);
            Object air = type("block").getMethod(name("default_state")).invoke(airBlock);
            Class<?> fluidType = Class.forName((String)emptyBinding.get("fluid_type"));
            Object fluid = state.getMethod((String)emptyBinding.get("fluid_getter")).invoke(air);
            Map<String,Object> constants = map(emptyBinding.get("constants"));
            empty = Proxy.newProxyInstance(reader.getClassLoader(), new Class<?>[]{reader}, (proxy, invoked, parameters) -> {
                if (invoked.getDeclaringClass() == Object.class) {
                    if (invoked.getName().equals("toString")) return "OwnedEmptyBlockReader";
                    if (invoked.getName().equals("hashCode")) return System.identityHashCode(proxy);
                    if (invoked.getName().equals("equals")) return proxy == parameters[0];
                }
                if (invoked.getParameterCount() == 1 && invoked.getParameterTypes()[0] == position) {
                    if (invoked.getReturnType() == state) return air;
                    if (invoked.getReturnType() == fluidType) return fluid;
                    if (!invoked.getReturnType().isPrimitive()) return null;
                }
                if (invoked.getParameterCount() == 0 && constants.containsKey(invoked.getName())) return ((Number)constants.get(invoked.getName())).intValue();
                throw new UnsupportedOperationException("Unbound owned-reader method: " + invoked);
            });
        }
        Object origin = position.getConstructor(int.class, int.class, int.class).newInstance(0, 0, 0);
        Object full = type("shapes").getMethod(name("full")).invoke(null);
        Method collision = state.getMethod(name("collision"), reader, position);
        Method boxes = shapeType.getMethod(name("boxes"));
        Method coordinates = method(shapeType, name("coordinates"), type("axis"));
        Field discrete = field(shapeType, name("discrete_field"));
        Method filled = type("discrete").getMethod(name("filled"), int.class, int.class, int.class);
        Method idOf = type("block").getMethod(name("state_id"), state);
        Field[] bounds = new Field[6];
        List<?> boundNames = (List<?>)binding.get("bounds");
        for (int index = 0; index < 6; index++) bounds[index] = type("box").getField((String)boundNames.get(index));
        Object[] axes = new Object[3];
        List<?> axisNames = (List<?>)binding.get("axes");
        for (int index = 0; index < 3; index++) axes[index] = type("axis").getField((String)axisNames.get(index)).get(null);
        Method json = gson.getClass().getMethod("toJson", Object.class);
        Map<Integer,Integer> states = new TreeMap<>();
        Map<String,Integer> signatures = new LinkedHashMap<>();
        List<Object> groups = new ArrayList<>();
        for (Object nativeState : (Iterable<?>)type("block").getField(name("registry_field")).get(null)) {
            int id = (Integer)idOf.invoke(null, nativeState);
            try {
                Object shape = collision.invoke(nativeState, empty, origin);
                List<Object> aabbs = new ArrayList<>();
                for (Object box : (List<?>)boxes.invoke(shape)) {
                    List<Double> values = new ArrayList<>();
                    for (Field bound : bounds) values.add(finite(bound.getDouble(box)));
                    aabbs.add(values);
                }
                List<List<Double>> planes = new ArrayList<>();
                for (Object axis : axes) {
                    List<Double> values = new ArrayList<>();
                    for (Object value : (Iterable<?>)coordinates.invoke(shape, axis)) values.add(finite(((Number)value).doubleValue()));
                    planes.add(values);
                }
                int nx = planes.get(0).size() - 1, ny = planes.get(1).size() - 1, nz = planes.get(2).size() - 1;
                Object voxel = discrete.get(shape);
                List<Integer> cells = new ArrayList<>();
                for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++)
                    if ((Boolean)filled.invoke(voxel, x, y, z)) cells.add((x * ny + y) * nz + z);
                Map<String,Object> grid = new LinkedHashMap<>();
                grid.put("canonical_full_block", shape == full); grid.put("planes", planes); grid.put("cells", cells); grid.put("boxes", aabbs);
                String signature = (String)json.invoke(gson, grid);
                Integer group = signatures.get(signature);
                if (group == null) { group = groups.size(); signatures.put(signature, group); groups.add(grid); }
                if (states.put(id, group) != null) throw new IllegalStateException("Duplicate native state id " + id);
            } catch (InvocationTargetException error) {
                throw new IllegalStateException("Cannot measure original collision grid for state " + id, error.getCause());
            }
        }
        if (states.isEmpty()) throw new IllegalStateException("Empty native state registry");
        Map<String,String> origins = new TreeMap<>();
        Path expectedOrigin = Paths.get(args[1]).toRealPath();
        for (String file : map(binding.get("sources_sha256")).keySet()) {
            String className = file.substring(0, file.length() - 5);
            Path actual = Paths.get(Class.forName(className).getProtectionDomain().getCodeSource().getLocation().toURI()).toRealPath();
            if (!actual.equals(expectedOrigin)) throw new IllegalStateException("Unexpected original class origin: " + className);
            origins.put(className, actual.toString());
        }
        Map<String,Object> result = new LinkedHashMap<>();
        result.put("schema", 1); result.put("scope", emptyBinding.containsKey("type") ? "Original EmptyBlockGetter, origin position and native default collision grids; no neighbor, block-entity or actor variants" : "Owned air-only BlockGetter with native air/fluid states, null block entities and native reader constant defaults; original default collision grids at origin, no neighbor or actor variants");
        result.put("cell_order", "(x * y_size + y) * z_size + z"); result.put("states", states); result.put("groups", groups); result.put("origins", origins);
        Files.write(output, ((String)json.invoke(gson, result)).getBytes(StandardCharsets.UTF_8), StandardOpenOption.CREATE_NEW);
        System.out.println("Native collision grids: " + states.size() + " states, " + groups.size() + " groups");
    }
}
