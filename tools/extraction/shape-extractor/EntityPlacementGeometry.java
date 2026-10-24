import java.lang.reflect.*;
import java.util.*;

/** Measures native pose boxes and metadata-driven geometry changes. Runs only on owned probe entities and restores each changed metadata value. */
public final class EntityPlacementGeometry {
    static Class<?> cls(String name) throws Exception { return MiningDump.cls(name); }
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static final String ENTITY = "net.minecraft.world.entity.Entity", POSE = "net.minecraft.world.entity.Pose", DATA = "net.minecraft.network.syncher.SynchedEntityData", ACCESSOR = "net.minecraft.network.syncher.EntityDataAccessor";

    static int index(Object accessor) throws Exception {
        Method id = MiningDump.optional(ACCESSOR, "id", "");
        if (id == null) id = method(ACCESSOR, "getId", "");
        return (Integer)id.invoke(accessor);
    }
    static Map<Integer, Object[]> metadata(Object entity) throws Exception {
        Object data = method(ENTITY, "getEntityData", "").invoke(entity);
        Object storage = PushDump.field(DATA, "itemsById").get(data);
        Collection<?> entries = storage instanceof Map ? ((Map<?, ?>)storage).values() : Arrays.asList((Object[])storage);
        Map<Integer, Object[]> result = new TreeMap<>();
        for (Object entry : entries) {
            if (entry == null) continue;
            Object accessor = method(DATA + "$DataItem", "getAccessor", "").invoke(entry);
            result.put(index(accessor), new Object[] {accessor, method(DATA + "$DataItem", "getValue", "").invoke(entry)});
        }
        return result;
    }
    static List<Double> box(Object entity) throws Exception {
        Object box = method(ENTITY, "getBoundingBox", "").invoke(entity);
        List<Double> coordinates = new ArrayList<>();
        for (String name : new String[] {"minX", "minY", "minZ", "maxX", "maxY", "maxZ"}) coordinates.add(PushDump.field("net.minecraft.world.phys.AABB", name).getDouble(box));
        return coordinates;
    }
    static Map<String, Object> observe(Object entity) throws Exception {
        Method setPose = method(ENTITY, "setPose", POSE), size = method(ENTITY, "getDimensions", POSE), position = method(ENTITY, "setPos", "double,double,double");
        Method refresh = method(ENTITY, "refreshDimensions", "");
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("blocks_building", PushDump.field(ENTITY, "blocksBuilding").getBoolean(entity));
        List<Object> poses = new ArrayList<>();
        for (Object pose : cls(POSE).getEnumConstants()) {
            setPose.invoke(entity, pose);
            refresh.invoke(entity);
            position.invoke(entity, 1.5, 64.0, 1.5);
            Object dimensions = size.invoke(entity, pose);
            List<Object> row = new ArrayList<>(Arrays.asList(((Enum<?>)pose).name(), EntityPlacementProbe.dimension(dimensions, "width"), EntityPlacementProbe.dimension(dimensions, "height"), EntityPlacementProbe.dimension(dimensions, "fixed"), box(entity)));
            poses.add(row);
        }
        result.put("poses", poses);
        setPose.invoke(entity, cls(POSE).getEnumConstants()[0]);
        refresh.invoke(entity);
        return result;
    }
    static void set(Object entity, Object accessor, Object value) throws Exception {
        Object data = method(ENTITY, "getEntityData", "").invoke(entity);
        method(DATA, "set", ACCESSOR + ",java.lang.Object").invoke(data, accessor, value);
    }
    public static Map<String, Object> measure(Object entity) throws Exception {
        Map<String, Object> result = new LinkedHashMap<>();
        Map<Integer, Object[]> fields = metadata(entity);
        result.put("pose_index", index(MiningDump.field(ENTITY, "DATA_POSE")));
        List<Object> defaults = new ArrayList<>();
        for (Map.Entry<Integer, Object[]> entry : fields.entrySet()) {
            Object value = entry.getValue()[1];
            if (value instanceof Boolean || value instanceof Number) defaults.add(Arrays.asList(entry.getKey(), value.getClass().getSimpleName(), value));
        }
        result.put("metadata_defaults", defaults);
        Map<String, Object> baseline = observe(entity);
        result.put("default", baseline);
        List<Object> selectors = new ArrayList<>();
        for (Map.Entry<Integer, Object[]> field : fields.entrySet()) {
            Object accessor = field.getValue()[0], original = field.getValue()[1];
            if (!(original instanceof Boolean)) continue;
            Map<String, Object> changed;
            try {
                set(entity, accessor, !(Boolean)original);
                changed = observe(entity);
            } finally { set(entity, accessor, original); }
            if (!baseline.equals(changed)) {
                Map<String, Object> selector = new LinkedHashMap<>();
                selector.put("index", field.getKey()); selector.put("kind", "boolean"); selector.put("default", original); selector.put("changed", changed);
                selectors.add(selector);
            }
        }
        Class<?> armorStand = cls("net.minecraft.world.entity.decoration.ArmorStand");
        if (armorStand.isInstance(entity)) {
            Object accessor = MiningDump.field("net.minecraft.world.entity.decoration.ArmorStand", "DATA_CLIENT_FLAGS");
            Object original = fields.get(index(accessor))[1];
            List<Map<String, Object>> values = new ArrayList<>();
            try {
                for (int flags = 0; flags <= 255; flags++) { set(entity, accessor, (byte)flags); values.add(observe(entity)); }
            } finally { set(entity, accessor, original); }
            int mask = 0;
            for (int bit = 0; bit < 8; bit++)
                for (int value = 0; value < 256; value++) if (!values.get(value).equals(values.get(value ^ (1 << bit)))) mask |= 1 << bit;
            Map<Integer, Object> cases = new TreeMap<>();
            for (int value = 0; value < 256; value++) {
                Object previous = cases.put(value & mask, values.get(value));
                if (previous != null && !previous.equals(values.get(value))) throw new IllegalStateException("Byte selector reduction loses native geometry");
            }
            Map<String, Object> selector = new LinkedHashMap<>();
            selector.put("index", index(accessor)); selector.put("kind", "byte"); selector.put("default", original); selector.put("mask", mask); selector.put("cases", cases); selectors.add(selector);
        }
        result.put("selectors", selectors);
        return result;
    }
}
