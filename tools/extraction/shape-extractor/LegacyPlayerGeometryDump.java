import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Calls original 1.14 player pose, dimension, refresh and position methods. Owned survival subtype and empty world inputs; no constructors for the world and no complete ticks. */
public final class LegacyPlayerGeometryDump {
    static Map<String,Object> bindings;
    static Field width, height, fixed;
    static Method dimensions;
    static Map<String,Object> spec(String name) { return LegacyRemoteGeneric.map(bindings.get(name)); }
    static Method method(String name, Class<?>... arguments) throws Exception { return LegacyRemoteGeneric.method(spec(name), arguments); }
    static List<Object> size(Object actor, Object pose) throws Exception {
        Object value = dimensions.invoke(actor, pose);
        return Arrays.asList(width.get(value), height.get(value), fixed.get(value));
    }
    static Object box(Object actor) throws Exception { return LegacyRemoteGeneric.state(actor).get("box"); }
    static Class<?> implementation(Class<?> type, Method method) throws Exception {
        for (; type != null; type = type.getSuperclass()) {
            try { type.getDeclaredMethod(method.getName(), method.getParameterTypes()); return type; }
            catch (NoSuchMethodException ignored) { }
        }
        throw new IllegalStateException("Missing native method implementation");
    }
    static boolean isDimensions(Class<?> type) {
        int floats = 0, booleans = 0;
        for (Field field : type.getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers())) continue;
            if (field.getType() == float.class) floats++;
            else if (field.getType() == boolean.class) booleans++;
        }
        return floats == 2 && booleans == 1;
    }
    public static void main(String[] args) throws Exception {
        LegacyRemoteGeneric.gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        try (Reader reader = Files.newBufferedReader(Paths.get(args[0]))) {
            bindings = (Map<String,Object>)LegacyRemoteGeneric.gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(LegacyRemoteGeneric.gson, reader, Map.class);
        }
        LegacyRemoteGeneric.bindings = bindings;
        LegacyDimensionsDump.b = bindings;
        Class.forName((String)bindings.get("bootstrap")).getMethod((String)bindings.get("boot")).invoke(null);
        Object world = LegacyDimensionsDump.world();
        Class<?> profileType = Class.forName("com.mojang.authlib.GameProfile");
        Object profile = profileType.getConstructor(UUID.class, String.class).newInstance(new UUID(0, 1), "PoseTransitionProbe");
        Object actor = Class.forName("LegacyRemotePlayerAdapter").getConstructor(Class.forName((String)bindings.get("world")), profileType).newInstance(world, profile);
        Map<String,Object> contract = LegacyRemoteGeneric.map(bindings.get("geometry_contract"));
        Field building = LegacyRemoteGeneric.field(Class.forName((String)contract.get("entity")), (String)contract.get("blocks_building_field"));
        boolean constructorBuilding = building.getBoolean(actor);
        Class<?> player = actor.getClass().getSuperclass().getSuperclass().getSuperclass();
        Class<?> entity = LegacyDimensionsDump.entity();
        Class<?> poseType = null;
        for (Method candidate : player.getDeclaredMethods()) {
            if (candidate.getReturnType() == float.class && candidate.getParameterCount() == 2 && candidate.getParameterTypes()[0].isEnum()) {
                if (poseType != null) throw new IllegalStateException("Ambiguous native pose type");
                poseType = candidate.getParameterTypes()[0];
            }
        }
        if (poseType == null) throw new IllegalStateException("Missing native pose type");
        Method setPose = null;
        for (Method candidate : entity.getDeclaredMethods()) {
            if (!Arrays.equals(candidate.getParameterTypes(), new Class<?>[]{poseType})) continue;
            if (candidate.getReturnType() == void.class) {
                if (setPose != null) throw new IllegalStateException("Ambiguous native pose setter");
                setPose = candidate;
            } else if (isDimensions(candidate.getReturnType())) {
                if (dimensions != null) throw new IllegalStateException("Ambiguous native dimension getter");
                dimensions = candidate;
            }
        }
        if (setPose == null || dimensions == null) throw new IllegalStateException("Missing native pose methods");
        setPose.setAccessible(true); dimensions.setAccessible(true);
        Object[] poses = poseType.getEnumConstants();
        Object standing = dimensions.invoke(actor, poses[0]);
        for (Field field : standing.getClass().getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers())) continue;
            field.setAccessible(true);
            if (field.getType() == float.class && field.getFloat(standing) == 0.6F) width = field;
            else if (field.getType() == float.class && field.getFloat(standing) == 1.8F) height = field;
            else if (field.getType() == boolean.class) fixed = field;
        }
        if (width == null || height == null || fixed == null) throw new IllegalStateException("Unverified native dimension field layout");
        Field poseField = entity.getDeclaredField((String)contract.get("pose_accessor_field")); poseField.setAccessible(true);
        Object poseAccessor = poseField.get(null);
        int poseIndex = (Integer)poseAccessor.getClass().getMethod((String)contract.get("accessor_id")).invoke(poseAccessor);
        Method callback = entity.getMethod((String)contract.get("metadata_callback"), poseAccessor.getClass());
        Method boxGetter = method("box_getter");
        Method refresh = method("dimension_refresh"), position = method("position_setter", double.class, double.class, double.class);
        Map<String,Object> measuredDimensions = new LinkedHashMap<>();
        List<Object> snapshots = new ArrayList<>();
        for (Object pose : poses) {
            setPose.invoke(actor, pose); refresh.invoke(actor); position.invoke(actor, 1.5, 64.0, 1.5);
            String name = ((Enum<?>)pose).name(); List<Object> measured = size(actor, pose);
            measuredDimensions.put(name, measured);
            snapshots.add(Arrays.asList(name, measured.get(0), measured.get(1), measured.get(2), box(actor)));
        }
        double[][] points = {{1.5, 64, 1.5}, {-17.25, -64, -31.75}, {29999999.5, 128, -29999999.5}};
        List<Object> rows = new ArrayList<>();
        for (int from = 0; from < poses.length; from++) for (int to = 0; to < poses.length; to++) for (int index = 0; index < points.length; index++) {
            double[] point = points[index];
            setPose.invoke(actor, poses[from]); refresh.invoke(actor); position.invoke(actor, point[0], point[1], point[2]);
            Map<String,Object> row = new LinkedHashMap<>();
            row.put("from", from); row.put("to", to); row.put("position", index);
            row.put("from_size", size(actor, poses[from])); row.put("to_size", size(actor, poses[to])); row.put("before", box(actor));
            setPose.invoke(actor, poses[to]); row.put("after_sync", box(actor));
            refresh.invoke(actor); row.put("after_refresh", box(actor));
            position.invoke(actor, point[0], point[1], point[2]); row.put("after_position", box(actor));
            rows.add(row);
        }
        Map<String,String> owners = new LinkedHashMap<>();
        owners.put("setPose", implementation(actor.getClass(), setPose).getName()); owners.put("getDimensions", implementation(actor.getClass(), dimensions).getName());
        owners.put("refreshDimensions", implementation(actor.getClass(), refresh).getName()); owners.put("setPos", implementation(actor.getClass(), position).getName());
        owners.put("onSyncedDataUpdated", implementation(actor.getClass(), callback).getName());
        owners.put("getBoundingBox", implementation(actor.getClass(), boxGetter).getName());
        Map<String,String> origins = new LinkedHashMap<>();
        Path client = Paths.get(args[1]).toRealPath();
        for (String sourceName : LegacyRemoteGeneric.map(contract.get("sources_sha256")).keySet()) {
            String name = sourceName.substring(0, sourceName.length() - 5);
            Path origin = Paths.get(Class.forName(name).getProtectionDomain().getCodeSource().getLocation().toURI()).toRealPath();
            if (!origin.equals(client)) throw new IllegalStateException("Wrong native class origin: " + name);
            origins.put(name, origin.toString());
        }
        List<String> names = new ArrayList<>(); for (Object pose : poses) names.add(((Enum<?>)pose).name());
        Map<String,Object> output = new LinkedHashMap<>();
        output.put("scope", "Original RemotePlayer constructor in an owned survival subtype; original pose synchronization, dimension getter, refresh and position methods. Empty world adapter; no world constructor, packet handler or complete player/entity/world tick.");
        Map<String,String> methods = new LinkedHashMap<>();
        methods.put("setPose", setPose.getName()); methods.put("getDimensions", dimensions.getName()); methods.put("refreshDimensions", refresh.getName()); methods.put("setPos", position.getName()); methods.put("getBoundingBox", boxGetter.getName()); methods.put("onSyncedDataUpdated", callback.getName());
        output.put("method_names", methods); output.put("pose_type", poseType.getName()); output.put("dimensions_type", standing.getClass().getName());
        output.put("actor", actor.getClass().getName()); output.put("constructor_blocks_building", constructorBuilding); output.put("blocks_building", building.getBoolean(actor));
        output.put("pose_index", poseIndex); output.put("pose_accessor_field", poseField.getName());
        output.put("dimensions", measuredDimensions); output.put("snapshots", snapshots);
        output.put("poses", names); output.put("positions", points); output.put("rows", rows); output.put("method_owners", owners); output.put("origins", origins);
        Files.write(Paths.get(args[2]), ((String)LegacyRemoteGeneric.gson.getClass().getMethod("toJson", Object.class).invoke(LegacyRemoteGeneric.gson, output)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native legacy pose transitions=" + rows.size());
    }
}
