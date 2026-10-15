import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures native client player boxes after pose synchronization, explicit refresh, and position reapplication. Arguments: mappings-or-none, unused-classpath, output.json. */
public final class EntityBoxTransitionDump {
    static final String ENTITY = "net.minecraft.world.entity.Entity", POSE = "net.minecraft.world.entity.Pose";
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static List<Object> size(Object entity, Object pose, Method dimensions) throws Exception {
        Object value = dimensions.invoke(entity, pose);
        return Arrays.asList(EntityPlacementProbe.dimension(value, "width"), EntityPlacementProbe.dimension(value, "height"), EntityPlacementProbe.dimension(value, "fixed"));
    }
    public static void main(String[] args) throws Exception {
        EntityPlacementProbe.initialize(args[0]);
        Object world = EntityPlacementProbe.world(), entity = EntityPlacementProbe.player(world);
        Object[] poses = MiningDump.cls(POSE).getEnumConstants();
        Method setPose = method(ENTITY, "setPose", POSE), refresh = method(ENTITY, "refreshDimensions", ""), setPos = method(ENTITY, "setPos", "double,double,double");
        Method dimensions = method(ENTITY, "getDimensions", POSE);
        List<String> names = new ArrayList<>();
        for (Object pose : poses) names.add(((Enum<?>)pose).name());
        double[][] positions = {{1.5, 64.0, 1.5}, {-17.25, -64.0, -31.75}, {29999999.5, 128.0, -29999999.5}};
        Map<String,String> owners = new LinkedHashMap<>();
        Map<String,Method> nativeMethods = new LinkedHashMap<>();
        nativeMethods.put("setPose", setPose); nativeMethods.put("refreshDimensions", refresh); nativeMethods.put("setPos", setPos); nativeMethods.put("getDimensions", dimensions);
        nativeMethods.put("onSyncedDataUpdated", method(ENTITY, "onSyncedDataUpdated", "net.minecraft.network.syncher.EntityDataAccessor"));
        for (Map.Entry<String,Method> entry : nativeMethods.entrySet())
            owners.put(entry.getKey(), EntityPlacementProbe.named(EntityPlacementProbe.implementation(entity.getClass(), entry.getValue()).getDeclaringClass()));
        List<Object> rows = new ArrayList<>();
        for (int source = 0; source < poses.length; source++) for (int target = 0; target < poses.length; target++) for (int index = 0; index < positions.length; index++) {
            double[] position = positions[index];
            setPose.invoke(entity, poses[source]);
            refresh.invoke(entity);
            setPos.invoke(entity, position[0], position[1], position[2]);
            Map<String,Object> row = new LinkedHashMap<>();
            row.put("from", source); row.put("to", target); row.put("position", index);
            row.put("from_size", size(entity, poses[source], dimensions)); row.put("to_size", size(entity, poses[target], dimensions));
            row.put("before", EntityPlacementGeometry.box(entity));
            setPose.invoke(entity, poses[target]);
            row.put("after_sync", EntityPlacementGeometry.box(entity));
            refresh.invoke(entity);
            row.put("after_refresh", EntityPlacementGeometry.box(entity));
            setPos.invoke(entity, position[0], position[1], position[2]);
            row.put("after_position", EntityPlacementGeometry.box(entity));
            rows.add(row);
        }
        Map<String,Object> result = new LinkedHashMap<>(); result.put("poses", names); result.put("positions", positions); result.put("rows", rows); result.put("method_owners", owners);
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, result)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native client entity-box transitions=" + rows.size());
    }
}
