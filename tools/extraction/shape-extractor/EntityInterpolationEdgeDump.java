import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Adds native default-length, rotation, dimension-callback and sustained-stream witnesses. Reuses the unchanged native-controller probe and its explicit tick boundary. */
public final class EntityInterpolationEdgeDump {
    static final String ENTITY = EntityInterpolationDump.ENTITY, HANDLER = EntityInterpolationDump.HANDLER;
    static Map<String,Object> target(Object entity, double x, double y, double z, float yaw, float pitch, int length) throws Exception {
        EntityInterpolationDump.target(entity,x,y,z,yaw,pitch,length);
        Map<String,Object> op = new LinkedHashMap<>(); op.put("kind","target"); op.put("position",Arrays.asList(x,y,z)); op.put("yaw",yaw); op.put("pitch",pitch); return op;
    }
    static Map<String,Object> rotation(Object entity, float yaw, float pitch) throws Exception {
        EntityInterpolationDump.method(ENTITY,"setRot","float,float").invoke(entity,yaw,pitch);
        Map<String,Object> op = new LinkedHashMap<>(); op.put("kind","rotation"); op.put("yaw",yaw); op.put("pitch",pitch); return op;
    }
    static Map<String,Object> pose(Object entity, String name) throws Exception {
        String type = "net.minecraft.world.entity.Pose";
        Object pose = Enum.valueOf((Class)MiningDump.cls(type),name);
        EntityInterpolationDump.method(ENTITY,"setPose",type).invoke(entity,pose);
        Object size = EntityInterpolationDump.method(ENTITY,"getDimensions",type).invoke(entity,pose);
        Map<String,Object> op = new LinkedHashMap<>(); op.put("kind","dimensions"); op.put("native_pose",name); op.put("width",EntityPlacementProbe.dimension(size,"width")); op.put("height",EntityPlacementProbe.dimension(size,"height")); return op;
    }
    public static void main(String[] args) throws Exception {
        EntityPlacementProbe.initialize(args[0]);
        Object world = EntityPlacementProbe.world();
        boolean stepped = MiningDump.clsOrNull(EntityInterpolationDump.PATH) != null;
        boolean partial = MiningDump.optional(ENTITY,"moveOrInterpolateTo","float,float") != null;
        List<Object> cases = new ArrayList<>();
        String profile = null;
        for (String name : new String[] {"native_default","fractional_rotation","external_rotation","pose_during_interpolation","burst"}) {
            EntityInterpolationDump.collisionClear = true; EntityInterpolationDump.relativeTickSpeed = 1.0F;
            Object entity = EntityPlacementProbe.player(world), interpolation = EntityInterpolationDump.handler(entity);
            int length = interpolation == null ? 3 : PushDump.field(stepped ? "net.minecraft.world.entity.AbstractInterpolationHandler" : HANDLER,"interpolationSteps").getInt(interpolation);
            profile = interpolation == null ? "living_inline" : stepped ? "living_steps" : "living_handler";
            EntityInterpolationDump.method(ENTITY,"setPos","double,double,double").invoke(entity,1.5,64.0,1.5);
            EntityInterpolationDump.method(ENTITY,"setRot","float,float").invoke(entity,name.equals("fractional_rotation") ? 167.123456F : 0.0F,0.0F);
            Object dimensions = EntityInterpolationDump.method(ENTITY,"getDimensions","net.minecraft.world.entity.Pose").invoke(entity,MiningDump.cls("net.minecraft.world.entity.Pose").getEnumConstants()[0]);
            Map<String,Object> result = new LinkedHashMap<>(); result.put("name",name); result.put("length",length); result.put("length_input",interpolation == null ? "legacy_argument_three" : "unchanged_native_controller_default");
            result.put("width",EntityPlacementProbe.dimension(dimensions,"width")); result.put("height",EntityPlacementProbe.dimension(dimensions,"height")); result.put("initial",EntityInterpolationDump.state(entity,interpolation,stepped));
            List<Object> ticks = new ArrayList<>();
            int count = name.equals("burst") ? 320 : 12;
            for (int tick=0;tick<count;tick++) {
                EntityInterpolationDump.collisionQueries.clear();
                List<Object> operations = new ArrayList<>();
                if (name.equals("burst")) {
                    if (tick<200) operations.add(target(entity,1.5+tick*0.125,64+(tick%7)*0.0625,1.5-(tick%5)*0.125,(tick*37.125F)%360.0F,((tick%19)-9)*7.125F,length));
                } else if (tick==0) {
                    operations.add(target(entity,4.5,67.0,-1.5,name.equals("fractional_rotation") ? -178.765432F : 90.0F,name.equals("fractional_rotation") ? 59.87654F : 30.0F,length));
                }
                if (name.equals("external_rotation") && tick==1) operations.add(rotation(entity,EntityInterpolationDump.rotation(entity,"yRot")+17.12345F,EntityInterpolationDump.rotation(entity,"xRot")-6.54321F));
                if (name.equals("pose_during_interpolation") && tick==1) operations.add(pose(entity,"SLEEPING"));
                if (name.equals("pose_during_interpolation") && tick==2) operations.add(pose(entity,"STANDING"));
                Map<String,Object> row = new LinkedHashMap<>(); row.put("tick",tick); row.put("operations",operations); row.put("before",EntityInterpolationDump.state(entity,interpolation,stepped));
                EntityInterpolationDump.tick(entity,interpolation);
                row.put("after",EntityInterpolationDump.state(entity,interpolation,stepped)); row.put("collision_queries",new ArrayList<>(EntityInterpolationDump.collisionQueries)); ticks.add(row);
            }
            result.put("ticks",ticks); cases.add(result);
        }
        Map<String,Object> result = new LinkedHashMap<>(); result.put("schema",1); result.put("suite","edges"); result.put("profile",profile); result.put("legacy_tick_boundary",profile.equals("living_inline") ? "first_velocity_set_after_interpolation" : "not_used"); result.put("partial_updates",partial); result.put("stepped_paths",stepped); result.put("cases",cases);
        Map<String,Object> actor = new LinkedHashMap<>(); actor.put("class","EntityPlayerAdapter"); actor.put("controller",profile); actor.put("factory","owned_player_subtype"); result.put("entity_profiles",Collections.singletonMap("minecraft:player",actor));
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]),((String)gson.getClass().getMethod("toJson",Object.class).invoke(gson,result)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native interpolation edge cases=" + cases.size());
    }
}
