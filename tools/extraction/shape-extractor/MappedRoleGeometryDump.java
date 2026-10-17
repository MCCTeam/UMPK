import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Executes original entity constructors and age/pose callbacks with controlled world inputs and no entity/world ticks. */
public final class MappedRoleGeometryDump {
    static final String ENTITY = "net.minecraft.world.entity.Entity", LIVING = "net.minecraft.world.entity.LivingEntity", POSE = "net.minecraft.world.entity.Pose";
    static List<Object> size(Object actor) throws Exception {
        Object value = PushDump.field(ENTITY, "dimensions").get(actor);
        return Arrays.asList(EntityPlacementProbe.dimension(value, "width"), EntityPlacementProbe.dimension(value, "height"), EntityPlacementProbe.dimension(value, "fixed"));
    }
    public static void main(String[] args) throws Exception {
        EntityPlacementProbe.initialize(args[0]);
        Object world = EntityPlacementProbe.world(), registry = MiningDump.registry("ENTITY_TYPE"), nativeType = null;
        String key = args[2];
        for (Object candidate : (Iterable<?>)registry) if (MiningDump.key(registry, candidate).equals(key)) nativeType = candidate;
        if (nativeType == null) throw new IllegalStateException("Missing entity type " + key);
        String type = "net.minecraft.world.entity.EntityType";
        Object actor = MiningDump.method(type + "$EntityFactory", "create", type + ",net.minecraft.world.level.Level").invoke(PushDump.field(type, "factory").get(nativeType), nativeType, world);
        if (actor == null) throw new IllegalStateException("Native factory returned null");
        if(MiningDump.method(ENTITY,"getType", "").invoke(actor)!=nativeType)throw new IllegalStateException("Actor type differs from native factory");
        Set<Method> numericGetters=new LinkedHashSet<>();for(String owner:new String[]{"net.minecraft.core.IdMap","net.minecraft.core.Registry"}){Method candidate=MiningDump.optional(owner,"getId","java.lang.Object");if(candidate!=null&&candidate.getDeclaringClass().isInstance(registry))numericGetters.add(EntityPlacementProbe.implementation(registry.getClass(),candidate));}
        if(numericGetters.size()!=1)throw new IllegalStateException("Ambiguous source-named native getId implementation "+numericGetters);
        Method numericGetter=numericGetters.iterator().next();
        boolean selected = key.equals("minecraft:pig") || key.equals("minecraft:sheep") || key.equals("minecraft:chicken");
        if(!selected)throw new IllegalArgumentException("Unaudited farm role "+key);
        String ageable = MiningDump.clsOrNull("net.minecraft.world.entity.AgeableMob") == null ? "net.minecraft.world.entity.AgableMob" : "net.minecraft.world.entity.AgeableMob";
        Object accessor = selected ? MiningDump.field(ageable, "DATA_BABY_ID") : null;
        Object poseAccessor = MiningDump.field(ENTITY, "DATA_POSE");
        Map<Integer,Object[]> fields = EntityPlacementGeometry.metadata(actor);
        int metadata = selected ? EntityPlacementGeometry.index(accessor) : -1;
        Object defaultValue = selected ? fields.get(metadata)[1] : Integer.valueOf(0);
        if (selected && !Boolean.FALSE.equals(defaultValue)) throw new IllegalStateException("Unexpected native baby default");
        Object[] poses = MiningDump.cls(POSE).getEnumConstants();
        Method setPose = MiningDump.method(ENTITY, "setPose", POSE), refresh = MiningDump.method(ENTITY, "refreshDimensions", ""), setPos = MiningDump.method(ENTITY, "setPos", "double,double,double");
        double[][] positions = {{1.5,64.0,1.5},{-17.25,-64.0,-31.75},{29999999.5,128.0,-29999999.5}};
        List<Object> rows = new ArrayList<>();
        for (int a=0;a<(selected?2:1);a++) for (int b=0;b<(selected?2:1);b++) for (int c=0;c<poses.length;c++) for (int d=0;d<poses.length;d++) for (int p=0;p<positions.length;p++) {
            double[] point=positions[p];
            if(selected)EntityPlacementGeometry.set(actor,accessor,a!=0);
            setPose.invoke(actor,poses[c]);setPos.invoke(actor,point[0],point[1],point[2]);
            Map<String,Object> row=new LinkedHashMap<>();row.put("from_age",a);row.put("to_age",b);row.put("from_pose",c);row.put("to_pose",d);row.put("position",p);row.put("before_child",MiningDump.method(LIVING,"isBaby", "").invoke(actor));row.put("from_size",size(actor));row.put("before",EntityPlacementGeometry.box(actor));
            if(selected)EntityPlacementGeometry.set(actor,accessor,b!=0);
            row.put("after_child",MiningDump.method(LIVING,"isBaby", "").invoke(actor));row.put("age_size",size(actor));row.put("after_age_sync",EntityPlacementGeometry.box(actor));setPose.invoke(actor,poses[d]);row.put("to_size",size(actor));row.put("after_pose_sync",EntityPlacementGeometry.box(actor));refresh.invoke(actor);row.put("after_refresh",EntityPlacementGeometry.box(actor));setPos.invoke(actor,point[0],point[1],point[2]);row.put("after_position",EntityPlacementGeometry.box(actor));rows.add(row);
        }
        Map<String,Object> output=new LinkedHashMap<>();output.put("entity",key);output.put("entity_class",EntityPlacementProbe.named(actor.getClass()));output.put("selector_kind",selected?"boolean":"none");output.put("metadata_index",metadata);output.put("metadata_default",defaultValue);output.put("pose_index",EntityPlacementGeometry.index(poseAccessor));output.put("blocks_building",PushDump.field(ENTITY,"blocksBuilding").getBoolean(actor));
        List<String> names=new ArrayList<>();for(Object pose:poses)names.add(((Enum<?>)pose).name());output.put("poses",names);output.put("positions",positions);output.put("rows",rows);
        Map<String,String> owners=new LinkedHashMap<>();
        for(String[] entry:new String[][]{{"dimensions",ENTITY,"getDimensions",POSE},{"scale",LIVING,"getScale",""},{"baby",LIVING,"isBaby",""},{"position",ENTITY,"setPos","double,double,double"},{"refresh",ENTITY,"refreshDimensions",""},{"box",ENTITY,"getBoundingBox",""},{"synced_data",ENTITY,"onSyncedDataUpdated","net.minecraft.network.syncher.EntityDataAccessor"}}) {
            Method method=MiningDump.method(entry[1],entry[2],entry[3]);owners.put(entry[0],EntityPlacementProbe.named(EntityPlacementProbe.implementation(actor.getClass(),method).getDeclaringClass()));
        }
        output.put("method_owners",owners);
        output.put("entity_type_matches_factory",true);Map<String,Object> identity=new LinkedHashMap<>();identity.put("id",numericGetter.invoke(registry,nativeType));identity.put("name",MiningDump.key(registry,nativeType));identity.put("id_method",numericGetter.getName());identity.put("id_owner",EntityPlacementProbe.named(numericGetter.getDeclaringClass()));identity.put("registry_class",EntityPlacementProbe.named(registry.getClass()));output.put("registry",identity);
        List<String> hierarchy=new ArrayList<>();for(Class<?> current=actor.getClass();current!=Object.class;current=current.getSuperclass())hierarchy.add(EntityPlacementProbe.named(current));output.put("actor_hierarchy",hierarchy);
        Map<String,String> origins=new TreeMap<>();Set<Class<?>> classes=new HashSet<>();for(Class<?> current=actor.getClass();current!=Object.class;current=current.getSuperclass())classes.add(current);for(String name:new String[]{ENTITY,LIVING,POSE,"net.minecraft.world.entity.EntityType","net.minecraft.world.entity.EntityDimensions","net.minecraft.network.syncher.SynchedEntityData","net.minecraft.network.syncher.EntityDataAccessor","net.minecraft.world.phys.AABB"})classes.add(MiningDump.cls(name));
        classes.add(MiningDump.cls("net.minecraft.world.level.Level"));classes.add(MiningDump.cls("net.minecraft.world.entity.EntityType$EntityFactory"));classes.add(registry.getClass());classes.add(numericGetter.getDeclaringClass());
        Path expected=Paths.get(args[1]).toRealPath();for(Class<?> current:classes){Path actual=Paths.get(current.getProtectionDomain().getCodeSource().getLocation().toURI()).toRealPath();if(!actual.equals(expected))throw new IllegalStateException("Unexpected native origin "+current);origins.put(EntityPlacementProbe.named(current),actual.toString());}output.put("origins",origins);
        Object gson=Class.forName("com.google.gson.Gson").getConstructor().newInstance();Files.write(Paths.get(args[3]),((String)gson.getClass().getMethod("toJson",Object.class).invoke(gson,output)).getBytes(StandardCharsets.UTF_8));System.out.println(key+" native transition rows="+rows.size()+", owners="+owners);
    }
}
