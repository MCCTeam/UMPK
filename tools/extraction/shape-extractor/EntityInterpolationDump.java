import java.lang.invoke.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures unmodified native living interpolation controllers. The owned world supplies explicit collision answers and tick speed. Legacy execution stops at the first velocity setter after interpolation. Arguments: mappings-or-none, unused-classpath, output.json. */
public final class EntityInterpolationDump {
    static final String ENTITY = "net.minecraft.world.entity.Entity", LIVING = "net.minecraft.world.entity.LivingEntity", VECTOR = "net.minecraft.world.phys.Vec3", HANDLER = "net.minecraft.world.entity.InterpolationHandler", PATH = "net.minecraft.world.entity.PositionPath";
    public static boolean armed, collisionClear = true;
    public static float relativeTickSpeed = 1.0F;
    static final List<Object> collisionQueries = new ArrayList<>();
    public static final class TickBoundary extends RuntimeException { }
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static Method optional(String owner, String name, String args) throws Exception { return MiningDump.optional(owner, name, args); }
    static Object vector(double x, double y, double z) throws Exception { return MiningDump.cls(VECTOR).getConstructor(double.class, double.class, double.class).newInstance(x, y, z); }
    static double coordinate(Object entity, String axis) throws Exception {
        Method getter = optional(ENTITY, "get" + axis.toUpperCase(Locale.ROOT), "");
        return getter == null ? PushDump.field(ENTITY, axis).getDouble(entity) : ((Number)getter.invoke(entity)).doubleValue();
    }
    static float rotation(Object entity, String name) throws Exception {
        Method getter = optional(ENTITY, "get" + name.substring(0, 1).toUpperCase(Locale.ROOT) + name.substring(1), "");
        return getter == null ? PushDump.field(ENTITY, name).getFloat(entity) : ((Number)getter.invoke(entity)).floatValue();
    }
    static List<Double> vectorValues(Object value) throws Exception {
        List<Double> result = new ArrayList<>();
        for (String axis : new String[] {"x", "y", "z"}) result.add(PushDump.field(VECTOR, axis).getDouble(value));
        return result;
    }
    public static boolean collision(Object box) throws Exception {
        List<Double> values = new ArrayList<>();
        for (String axis : new String[] {"minX", "minY", "minZ", "maxX", "maxY", "maxZ"}) values.add(PushDump.field("net.minecraft.world.phys.AABB", axis).getDouble(box));
        collisionQueries.add(values);
        return collisionClear;
    }
    static Object handler(Object entity) throws Exception {
        Method getter = optional(ENTITY, "getInterpolation", "");
        return getter == null ? null : getter.invoke(entity);
    }
    static Map<String,Object> state(Object entity, Object interpolation, boolean stepped) throws Exception {
        List<Double> position = Arrays.asList(coordinate(entity,"x"), coordinate(entity,"y"), coordinate(entity,"z"));
        float yaw = rotation(entity,"yRot"), pitch = rotation(entity,"xRot");
        boolean active = interpolation == null ? PushDump.field(LIVING,"lerpSteps").getInt(entity) > 0 : (Boolean)method(HANDLER,"hasActiveInterpolation","").invoke(interpolation);
        List<Object> target = new ArrayList<>();
        if (!active) target.addAll(Arrays.asList(position.get(0),position.get(1),position.get(2),yaw,pitch));
        else if (interpolation == null) {
            for (String field : new String[] {"lerpX","lerpY","lerpZ","lerpYRot","lerpXRot"}) target.add(((Number)PushDump.field(LIVING,field).get(entity)).doubleValue());
        } else if (stepped) {
            Object value = method(HANDLER,"target","").invoke(interpolation);
            target.addAll(vectorValues(method("net.minecraft.core.PositionAndRotation","position","").invoke(value)));
            target.add(method("net.minecraft.core.PositionAndRotation","yRot","").invoke(value));
            target.add(method("net.minecraft.core.PositionAndRotation","xRot","").invoke(value));
        } else {
            target.addAll(vectorValues(method(HANDLER,"position","").invoke(interpolation)));
            target.add(method(HANDLER,"yRot","").invoke(interpolation)); target.add(method(HANDLER,"xRot","").invoke(interpolation));
        }
        Map<String,Object> result = new LinkedHashMap<>();
        result.put("position",position); result.put("rotation",Arrays.asList(yaw,pitch)); result.put("box",EntityPlacementGeometry.box(entity)); result.put("target",target); result.put("active",active);
        return result;
    }
    static void target(Object entity, double x, double y, double z, float yaw, float pitch, int length) throws Exception {
        Method modern = optional(ENTITY,"moveOrInterpolateTo",VECTOR + ",float,float");
        if (modern != null) { modern.invoke(entity,vector(x,y,z),yaw,pitch); return; }
        Method legacy = optional(ENTITY,"lerpTo","double,double,double,float,float,int,boolean");
        if (legacy != null) legacy.invoke(entity,x,y,z,yaw,pitch,length,false);
        else method(ENTITY,"lerpTo","double,double,double,float,float,int").invoke(entity,x,y,z,yaw,pitch,length);
    }
    static void path(Object entity, double x, double y, double z, boolean queued) throws Exception {
        Class<?> step = MiningDump.cls("net.minecraft.world.entity.PositionStep");
        Constructor<?> constructor = step.getConstructor(MiningDump.cls(VECTOR),int.class);
        List<Object> steps = Arrays.asList(constructor.newInstance(vector(x+1,y,z),1),constructor.newInstance(vector(x+1,y+1,z),0),constructor.newInstance(vector(x+3,y+2,z-2),2));
        Object value = method(PATH,"stepped","java.util.List").invoke(null,steps);
        method(ENTITY,"moveOrInterpolateTo",PATH + ",float,float").invoke(entity,value,queued ? -170.0F : 90.0F,30.0F);
    }
    static void tick(Object entity, Object interpolation) throws Exception {
        if (interpolation != null) { method(HANDLER,"interpolate","").invoke(interpolation); return; }
        Method nativeTick = method(LIVING,"aiStep","");
        MethodHandle head = MethodHandles.privateLookupIn(MiningDump.cls(LIVING),MethodHandles.lookup()).unreflectSpecial(nativeTick,MiningDump.cls(LIVING)).bindTo(entity);
        armed = true;
        try { head.invokeWithArguments(); throw new IllegalStateException("Native legacy tick did not reach its audited boundary"); }
        catch (TickBoundary expected) { }
        catch (Throwable failure) { throw new IllegalStateException("Native legacy interpolation failed before its audited boundary",failure); }
        finally { armed = false; }
    }
    static Map<String,Object> profiles(Object world) throws Exception {
        String type = "net.minecraft.world.entity.EntityType";
        Object registry = MiningDump.registry("ENTITY_TYPE"), owner = EntityPlacementProbe.player(world);
        Field factory = PushDump.field(type,"factory");
        Method create = method(type + "$EntityFactory","create",type + ",net.minecraft.world.level.Level");
        Map<String,Object> result = new TreeMap<>();
        for (Object nativeType : (Iterable<?>)registry) {
            String id = MiningDump.key(registry,nativeType);
            Object value = create.invoke(factory.get(nativeType),nativeType,world);
            boolean special = value == null;
            if (value == null) value = EntityPlacementProbe.special(id,world,owner);
            if (value == null) throw new IllegalStateException("Missing native interpolation factory: " + id);
            Map<String,Object> row = new LinkedHashMap<>(); row.put("class",EntityPlacementProbe.named(value.getClass()));
            row.put("factory",!special ? "native" : value.getClass().getName().equals("EntityPlayerAdapter") ? "owned_player_subtype" : "native_special_constructor");
            Method getter = optional(ENTITY,"getInterpolation","");
            if (getter != null) {
                Object controller = getter.invoke(value);
                row.put("controller",controller == null ? "none" : EntityPlacementProbe.named(controller.getClass()));
                Method length = optional(type,"updateInterval","");
                if (length != null) row.put("update_interval",length.invoke(nativeType));
            } else {
                Method lerp = optional(ENTITY,"lerpTo","double,double,double,float,float,int,boolean");
                if (lerp == null) lerp = method(ENTITY,"lerpTo","double,double,double,float,float,int");
                row.put("controller",EntityPlacementProbe.named(EntityPlacementProbe.implementation(value.getClass(),lerp).getDeclaringClass()));
            }
            row.put("living",MiningDump.cls(LIVING).isInstance(value)); result.put(id,row);
        }
        return result;
    }
    public static void main(String[] args) throws Exception {
        EntityPlacementProbe.initialize(args[0]);
        Object world = EntityPlacementProbe.world(), first = EntityPlacementProbe.player(world), firstHandler = handler(first);
        boolean stepped = MiningDump.clsOrNull(PATH) != null;
        boolean partial = optional(ENTITY,"moveOrInterpolateTo","float,float") != null;
        List<String> names = new ArrayList<>(Arrays.asList("linear","repeat","retarget","length_zero","length_one","length_six","cancel","external_clear","external_blocked","small_external_low","small_external_high","world_border","wrap_rotation","speed_half","speed_double","speed_zero"));
        if (partial) names.addAll(Arrays.asList("partial_position","partial_rotation"));
        if (stepped) names.addAll(Arrays.asList("stepped_path","queued_path","predicted_clear","predicted_blocked","predicted_small"));
        List<Object> cases = new ArrayList<>();
        for (String name : names) {
            collisionClear = !name.equals("external_blocked") && !name.equals("predicted_blocked"); relativeTickSpeed = name.equals("speed_half") ? 0.5F : name.equals("speed_double") ? 2.0F : name.equals("speed_zero") ? 0.0F : 1.0F;
            Object entity = EntityPlacementProbe.player(world), interpolation = handler(entity);
            int length = name.equals("length_zero") ? 0 : name.equals("length_one") ? 1 : name.equals("length_six") ? 6 : 3;
            if (interpolation != null) {
                if (stepped) PushDump.field("net.minecraft.world.entity.AbstractInterpolationHandler","interpolationSteps").setInt(interpolation,length);
                else method(HANDLER,"setInterpolationLength","int").invoke(interpolation,length);
            }
            double x = name.equals("world_border") ? 29999990.12345 : 1.5, y = 64.0, z = name.equals("world_border") ? -29999990.12345 : 1.5;
            method(ENTITY,"setPos","double,double,double").invoke(entity,x,y,z);
            method(ENTITY,"setRot","float,float").invoke(entity,name.equals("wrap_rotation") ? 170.0F : 0.0F,0.0F);
            List<Object> ticks = new ArrayList<>();
            for (int index=0;index<12;index++) {
                collisionQueries.clear();
                if (index==0) {
                    if (name.equals("stepped_path") || name.equals("queued_path")) path(entity,x,y,z,false);
                    else target(entity,x+3,y+3,z-3,name.equals("wrap_rotation") ? -170.0F : 90.0F,30.0F,length);
                }
                if (index==1) {
                    if (name.equals("repeat")) target(entity,x+3,y+3,z-3,90.0F,30.0F,length);
                    if (name.equals("retarget")) target(entity,x-4,y+1,z+2,-90.0F,-15.0F,length);
                    if (name.equals("queued_path")) path(entity,x+3,y+2,z-2,true);
                    if (name.equals("partial_position")) {
                        if (stepped) method(ENTITY,"moveOrInterpolateTo",PATH).invoke(entity,method(PATH,"of",VECTOR).invoke(null,vector(x-2,y+1,z+4)));
                        else method(ENTITY,"moveOrInterpolateTo",VECTOR).invoke(entity,vector(x-2,y+1,z+4));
                    }
                    if (name.equals("partial_rotation")) method(ENTITY,"moveOrInterpolateTo","float,float").invoke(entity,-90.0F,-15.0F);
                    if (name.equals("cancel")) {
                        if (interpolation == null) PushDump.field(LIVING,"lerpSteps").setInt(entity,0);
                        else method(HANDLER,"cancel","").invoke(interpolation);
                    }
                    if (name.startsWith("external_") || name.startsWith("small_external_")) {
                        double dx = name.equals("small_external_low") ? 0.003 : name.equals("small_external_high") ? 0.0032 : 0.25;
                        method(ENTITY,"setPos","double,double,double").invoke(entity,coordinate(entity,"x")+dx,coordinate(entity,"y"),coordinate(entity,"z"));
                    }
                    if (name.startsWith("predicted_")) {
                        double dx = name.equals("predicted_small") ? 0.003 : 0.25;
                        method(HANDLER,"applyPredictedMovement",VECTOR).invoke(interpolation,vector(dx,0,0));
                        method(ENTITY,"setPos","double,double,double").invoke(entity,coordinate(entity,"x")+dx,coordinate(entity,"y"),coordinate(entity,"z"));
                    }
                }
                if (index==2 && name.equals("retarget")) target(entity,x+6,y-2,z-1,170.0F,45.0F,length);
                Map<String,Object> row = new LinkedHashMap<>(); row.put("tick",index); row.put("before",state(entity,interpolation,stepped));
                tick(entity,interpolation); row.put("after",state(entity,interpolation,stepped)); row.put("collision_queries",new ArrayList<>(collisionQueries)); ticks.add(row);
            }
            Map<String,Object> row = new LinkedHashMap<>(); row.put("name",name); row.put("length",length); row.put("length_input",interpolation == null ? "native_lerp_argument" : stepped ? "protected_controller_field" : "native_setter"); row.put("collision_clear",collisionClear); row.put("relative_tick_speed",relativeTickSpeed); row.put("ticks",ticks); cases.add(row);
        }
        Map<String,Object> result = new LinkedHashMap<>(); result.put("schema",1); result.put("profile",firstHandler == null ? "living_inline" : stepped ? "living_steps" : "living_handler");
        result.put("legacy_tick_boundary",firstHandler == null ? "first_velocity_set_after_interpolation" : "not_used"); result.put("partial_updates",partial); result.put("stepped_paths",stepped); result.put("entity_profiles",profiles(world)); result.put("cases",cases);
        result.put("actor","owned survival Player subtype with inherited native LivingEntity interpolation");
        result.put("scope","Interpolation controller and entity factory selection only; packet listeners, RemotePlayer-specific behavior, complete entity ticks and world collision execution are separate");
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]),((String)gson.getClass().getMethod("toJson",Object.class).invoke(gson,result)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native living interpolation cases=" + cases.size());
    }
}
