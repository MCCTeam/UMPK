import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures metadata-era native selection boxes and matching native block ray traces. The owned world adapter supplies explicit blocks, air elsewhere, and a true client-side flag. Arguments: outline-symbols.json, outlines.json, witnesses.json. */
public final class LegacyOutlineDump {
    static Class<?> block, state, worldType, positionType, vectorType;
    static Object world, origin;
    static Object gson, registry, blockRegistry;
    static Object[] positions;
    static Map<Object, Object> blocks;
    static List<String> reads;
    static Map<String, Object> bindings;
    static Map<String, Map<String, String>> overrides;
    static Method ray, outline, offset;
    static Method owner, id, fromMetadata, key;
    static Map<Object, double[]> knownPositions = new HashMap<>();
    static Constructor<?> vectorConstructor;
    static Field hitPoint, hitFace;
    static Field[] vectorFields, boxFields, boundsFields;
    static boolean mutable;

    static Class<?> cls(String name) throws Exception { return Class.forName(name); }
    static Field field(Class<?> owner, String name) throws Exception {
        Field result = owner.getDeclaredField(name);
        result.setAccessible(true);
        return result;
    }
    static Field[] fields(Class<?> owner, List<?> names) throws Exception {
        Field[] result = new Field[names.size()];
        for (int index = 0; index < result.length; index++) result[index] = field(owner, (String) names.get(index));
        return result;
    }
    static double[] coordinates(Object value, Field[] fields) throws Exception {
        double[] result = new double[fields.length];
        for (int index = 0; index < result.length; index++) {
            result[index] = fields[index].getDouble(value);
            if (!Double.isFinite(result[index])) throw new IllegalStateException("Non-finite native geometry");
        }
        return result;
    }
    static Object constant(String expression) throws Exception {
        int separator = expression.lastIndexOf('.');
        String owner = expression.substring(0, separator);
        try { return cls(owner).getField(expression.substring(separator + 1)).get(null); }
        catch (ClassNotFoundException error) { return cls(owner.replace('.', '$')).getField(expression.substring(separator + 1)).get(null); }
    }
    static Object property(Object value, String method, Object property) throws Exception {
        List<Method> candidates = new ArrayList<>();
        for (Method candidate : state.getMethods())
            if (candidate.getName().equals(method) && candidate.getParameterCount() == 1
                && candidate.getParameterTypes()[0].isInstance(property) && Comparable.class.isAssignableFrom(candidate.getReturnType()))
                candidates.add(candidate);
        if (candidates.size() != 1) throw new IllegalStateException("Ambiguous state property accessor: " + candidates);
        return candidates.get(0).invoke(value, property);
    }
    static Map<String, String> override(Object owner) {
        for (Class<?> type = owner.getClass(); type != null && type != block; type = type.getSuperclass())
            if (overrides.containsKey(type.getName())) return overrides.get(type.getName());
        return Collections.emptyMap();
    }
    static Object trace(Object owner, Object value, Object position, double[] segment) throws Exception {
        Object start = vectorConstructor.newInstance(segment[0], segment[1], segment[2]);
        Object end = vectorConstructor.newInstance(segment[3], segment[4], segment[5]);
        return mutable ? ray.invoke(owner, world, position, start, end) : ray.invoke(owner, value, world, position, start, end);
    }
    static List<double[]> geometry(Object owner, Object value, Object position) throws Exception {
        Map<String, String> special = override(owner);
        String kind = special.get("kind");
        if ("empty".equals(kind)) return Collections.emptyList();
        List<double[]> result = new ArrayList<>();
        if ("octants".equals(kind)) {
            Class<?> type = ray.getDeclaringClass();
            for (Class<?> current = owner.getClass(); current != block; current = current.getSuperclass())
                if (overrides.get(current.getName()) == special) { type = current; break; }
            Object facing = property(value, special.get("property_get"), field(type, special.get("facing_property")).get(null));
            int index = (Integer) facing.getClass().getMethod(special.get("facing_index")).invoke(facing);
            Object half = property(value, special.get("property_get"), field(type, special.get("half_property")).get(null));
            if (half == constant(special.get("top_value"))) index += 4;
            int[] excluded = ((int[][]) field(type, special.get("exclusions")).get(null))[index];
            field(type, special.get("trace_flag")).setBoolean(owner, true);
            Field octant = field(type, special.get("trace_index"));
            Method update = block.getMethod((String) bindings.get("bounds_update"), cls((String) bindings.get("getter")), positionType);
            for (int part = 0; part < 8; part++) {
                if (Arrays.binarySearch(excluded, part) >= 0) continue;
                octant.setInt(owner, part);
                update.invoke(owner, world, position);
                result.add(coordinates(owner, boundsFields));
            }
        } else if (mutable) {
            // Native overrides such as torch set selection bounds in trace itself, not in updateBounds.
            double[] positionCoordinates = positionCoordinates(position);
            trace(owner, value, position, new double[] {positionCoordinates[0] + 0.5, positionCoordinates[1] + 0.5, positionCoordinates[2] - 1,
                positionCoordinates[0] + 0.5, positionCoordinates[1] + 0.5, positionCoordinates[2] + 2});
            result.add(coordinates(owner, boundsFields));
        } else if ("boxes".equals(kind)) {
            Class<?> type = owner.getClass();
            while (!overrides.containsKey(type.getName())) type = type.getSuperclass();
            Method actual = block.getMethod(special.get("actual_state"), state, cls((String) bindings.get("getter")), positionType);
            Method boxes = type.getDeclaredMethod(special.get("boxes"), state);
            boxes.setAccessible(true);
            for (Object box : (Iterable<?>) boxes.invoke(null, actual.invoke(owner, value, world, position))) result.add(coordinates(box, boxFields));
        } else {
            Object box = outline.invoke(value, world, position);
            if (box != null) result.add(coordinates(box, boxFields));
        }
        Iterator<double[]> iterator = result.iterator();
        while (iterator.hasNext()) {
            double[] box = iterator.next();
            int inverted = 0;
            for (int axis = 0; axis < 3; axis++) if (box[axis] > box[axis + 3]) inverted++;
            // Every native plane tests the other two axes; two inverted intervals reject every plane. This occurs for an unattached 1.8 vine.
            if (inverted >= 2) iterator.remove();
            else if (inverted != 0) throw new IllegalStateException("Unsupported single inverted native selection interval");
        }
        return result;
    }
    static double[] positionCoordinates(Object position) throws Exception {
        double[] result = knownPositions.get(position);
        if (result == null) throw new IllegalStateException("Unknown measurement position");
        return result;
    }
    static List<double[]> segments() {
        List<double[]> result = new ArrayList<>();
        for (int axis = 0; axis < 3; axis++)
            for (double a : new double[] {0.125, 0.375, 0.5, 0.625, 0.875})
                for (double b : new double[] {0.125, 0.5, 0.875}) {
                    double[] start = new double[3], end = new double[3];
                    start[axis] = -1; end[axis] = 2;
                    start[(axis + 1) % 3] = end[(axis + 1) % 3] = a;
                    start[(axis + 2) % 3] = end[(axis + 2) % 3] = b;
                    result.add(new double[] {start[0], start[1], start[2], end[0], end[1], end[2]});
                    result.add(new double[] {end[0], end[1], end[2], start[0], start[1], start[2]});
                }
        result.add(new double[] {0.5, 0.5, 0.5, 2, 0.5, 0.5});
        result.add(new double[] {0.25, 0.25, 0.25, 2, 0.25, 0.25});
        result.add(new double[] {0.5, 0.5, -1, 0.5, 0.5, 0});
        result.add(new double[] {0.5, 0.5, -0.0001, 0.5, 0.5, 0.0001});
        result.add(new double[] {-1, -1, -1, 2, 2, 2});
        return result;
    }
    static Map<String, Object> shape(List<double[]> boxes) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("boxes", boxes); result.put("offset", "none");
        result.put("max_horizontal_offset", 0.0); result.put("max_vertical_offset", 0.0);
        return result;
    }
    static Map<String, Object> normalize(Object owner, Object value, List<List<double[]>> measured, Object[] positions) throws Exception {
        boolean equal = true;
        for (List<double[]> boxes : measured) {
            if (boxes.size() != measured.get(0).size()) throw new IllegalStateException("Coordinate-dependent legacy shape topology");
            for (int part = 0; part < boxes.size(); part++) equal &= Arrays.equals(boxes.get(part), measured.get(0).get(part));
        }
        if (equal) return shape(measured.get(0));
        if (offset == null) throw new IllegalStateException("Coordinate-dependent shape without a native offset method");
        List<double[]> base = new ArrayList<>();
        double[] originOffset = coordinates(offset.invoke(owner, value, world, positions[0]), vectorFields);
        for (double[] box : measured.get(0)) {
            double[] normalized = box.clone();
            for (int coordinate = 0; coordinate < 6; coordinate++) normalized[coordinate] -= originOffset[coordinate % 3];
            base.add(normalized);
        }
        boolean vertical = false;
        for (int index = 0; index < positions.length; index++) {
            double[] translation = coordinates(offset.invoke(owner, value, world, positions[index]), vectorFields);
            vertical |= translation[1] != 0;
            for (int part = 0; part < base.size(); part++)
                for (int coordinate = 0; coordinate < 6; coordinate++)
                    if (Math.abs(measured.get(index).get(part)[coordinate] - base.get(part)[coordinate] - translation[coordinate % 3]) > 1e-12)
                        throw new IllegalStateException("Legacy outline does not follow its native coordinate offset");
        }
        Map<String, Object> result = shape(base);
        result.put("offset", vertical ? "xyz" : "xz");
        result.put("max_horizontal_offset", 0.25); result.put("max_vertical_offset", vertical ? 0.2 : 0.0);
        result.put("native_origin_offset", originOffset);
        return result;
    }
    static List<Object> witness(Object hit) throws Exception {
        if (hit == null) return Collections.emptyList();
        Object face = hitFace.get(hit);
        return Arrays.asList(((Enum<?>) face).ordinal(), coordinates(hitPoint.get(hit), vectorFields));
    }
    @SuppressWarnings("unchecked")
    static void initialize(String symbolsPath) throws Exception {
        gson = cls("com.google.gson.Gson").getConstructor().newInstance();
        Map<String, Object> symbols;
        try (Reader reader = Files.newBufferedReader(Paths.get(symbolsPath))) {
            symbols = (Map<String, Object>) gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, reader, Map.class);
        }
        bindings = (Map<String, Object>) symbols.get("outline_bindings");
        overrides = (Map<String, Map<String, String>>) bindings.get("overrides");
        block = cls((String) symbols.get("block")); state = cls((String) symbols.get("state"));
        worldType = cls((String) bindings.get("world")); positionType = cls((String) bindings.get("position"));
        vectorType = cls((String) bindings.get("vector_type")); mutable = (Boolean) bindings.get("mutable_bounds");
        for (Class<?> nativeType : new Class<?>[] {block, state, worldType, positionType, vectorType})
            System.out.println("native geometry class " + nativeType.getName() + " from " + nativeType.getProtectionDomain().getCodeSource().getLocation());
        cls((String) symbols.get("bootstrap")).getMethod((String) symbols.get("boot")).invoke(null);
        Class<?> adapter = cls("LegacyWorldAdapter");
        Field unsafeField = cls("sun.misc.Unsafe").getDeclaredField("theUnsafe"); unsafeField.setAccessible(true);
        Object unsafe = unsafeField.get(null);
        world = unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, adapter);
        field(worldType, (String) adapter.getField("clientSideField").get(null)).setBoolean(world, true);
        blocks = (Map<Object, Object>) adapter.getField("blocks").get(null);
        reads = (List<String>) adapter.getField("reads").get(null);
        registry = LegacyMiningGeneric.states(block, state);
        Map<String, List<?>> registries = (Map<String, List<?>>) symbols.get("registries");
        blockRegistry = LegacyMiningGeneric.registry(registries.get("block"));
        owner = LegacyMiningGeneric.sole(state, block, 0, null); id = LegacyMiningGeneric.sole(registry.getClass(), int.class, 1, state);
        fromMetadata = LegacyMiningGeneric.sole(block, state, 1, int.class);
        key = LegacyMiningGeneric.key(blockRegistry, ((Iterable<?>) blockRegistry).iterator().next());
        for (Object value : (Iterable<?>) registry)
            if (key.invoke(blockRegistry, owner.invoke(value)).toString().equals("minecraft:air")) { adapter.getField("air").set(null, value); break; }
        if (adapter.getField("air").get(null) == null) throw new IllegalStateException("No native air state");
        List<String> arguments = (List<String>) bindings.get("ray_args");
        Class<?>[] rayTypes = new Class<?>[arguments.size()];
        for (int index = 0; index < rayTypes.length; index++) rayTypes[index] = cls(arguments.get(index));
        ray = block.getMethod((String) bindings.get("ray"), rayTypes);
        vectorConstructor = vectorType.getConstructor(double.class, double.class, double.class);
        vectorFields = fields(vectorType, (List<?>) bindings.get("vector_fields"));
        if (bindings.get("offset") != null)
            offset = block.getMethod((String) bindings.get("offset"), state, cls((String) bindings.get("getter")), positionType);
        Class<?> hit = cls((String) bindings.get("hit_type"));
        hitPoint = field(hit, (String) bindings.get("hit_point")); hitFace = field(hit, (String) bindings.get("hit_face"));
        if (mutable) boundsFields = fields(block, (List<?>) bindings.get("bounds_fields"));
        else {
            boxFields = fields(cls((String) bindings.get("box_type")), (List<?>) bindings.get("box_fields"));
            outline = state.getMethod((String) bindings.get("state_outline"), cls((String) bindings.get("getter")), positionType);
        }
        Constructor<?> positionConstructor = positionType.getConstructor(int.class, int.class, int.class);
        double[][] coordinateValues = {{0, 0, 0}, {17, 63, -31}, {-30000000, 64, 29999999}, {0, 100, 0}};
        positions = new Object[coordinateValues.length];
        for (int index = 0; index < positions.length; index++) {
            double[] coordinate = coordinateValues[index];
            positions[index] = positionConstructor.newInstance((int) coordinate[0], (int) coordinate[1], (int) coordinate[2]);
            knownPositions.put(positions[index], coordinate);
        }
        origin = positions[0];
    }

    public static void main(String[] args) throws Exception {
        initialize(args[0]);
        List<double[]> segments = segments();
        Map<Integer, Object> outlines = new TreeMap<>(), witnesses = new TreeMap<>();
        long rayCount = 0;
        for (Object registered : (Iterable<?>) registry) {
            int stateId = (Integer) id.invoke(registry, registered);
            if (outlines.containsKey(stateId)) continue;
            Object nativeBlock = owner.invoke(registered);
            Object value = fromMetadata.invoke(nativeBlock, stateId & 15);
            blocks.clear(); blocks.put(origin, value); reads.clear();
            List<double[]> boxes;
            try { boxes = geometry(nativeBlock, value, origin); }
            catch (Exception error) { throw new IllegalStateException("Selection extraction failed at state " + stateId + " " + key.invoke(blockRegistry, nativeBlock), error); }
            List<String> originReads = new ArrayList<>(reads);
            List<List<double[]>> measured = new ArrayList<>(); measured.add(boxes);
            for (int index = 1; index < positions.length; index++) {
                Object position = positions[index];
                blocks.clear(); blocks.put(position, value);
                measured.add(geometry(nativeBlock, value, position));
            }
            Map<String, Object> normalized = normalize(nativeBlock, value, measured, positions);
            blocks.clear(); blocks.put(origin, value); reads.clear(); reads.addAll(originReads);
            Map<String, Object> row = new LinkedHashMap<>();
            row.put("block", key.invoke(blockRegistry, nativeBlock).toString());
            row.put("outline", normalized); row.put("interaction", shape(Collections.emptyList()));
            String kind = override(nativeBlock).get("kind");
            row.put("trace_selection", "octants".equals(kind) || "boxes".equals(kind) ? "farthest_from_end" : "nearest_from_start");
            List<Object> hits = new ArrayList<>();
            for (double[] segment : segments) { hits.add(witness(trace(nativeBlock, value, origin, segment))); rayCount++; }
            row.put("native_reads", new ArrayList<>(new TreeSet<>(reads)));
            if (outlines.put(stateId, row) != null) throw new IllegalStateException("Duplicate native state ID");
            witnesses.put(stateId, hits);
        }
        Map<String, Object> oracle = new LinkedHashMap<>();
        oracle.put("segments", segments); oracle.put("hits", witnesses);
        Method toJson = gson.getClass().getMethod("toJson", Object.class);
        Files.write(Paths.get(args[1]), ((String) toJson.invoke(gson, outlines)).getBytes(StandardCharsets.UTF_8));
        Files.write(Paths.get(args[2]), ((String) toJson.invoke(gson, oracle)).getBytes(StandardCharsets.UTF_8));
        System.out.println("legacy native selection states=" + outlines.size() + " ray witnesses=" + rayCount + " clientSide=true");
    }
}
