import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
/** Executes unchanged native RemotePlayer.aiStep with empty peers, explicit collision answers and controlled survival mode. From 26.3 the native interpolation handler is advanced before aiStep as a separate measured phase. This is not a full entity or world tick. */
public final class RemotePlayerDump {
 static final String ENTITY="net.minecraft.world.entity.Entity",REMOTE="net.minecraft.client.player.RemotePlayer",VECTOR="net.minecraft.world.phys.Vec3";
 static Method method(String owner,String name,String args) throws Exception {return MiningDump.method(owner,name,args);}
 static void motion(Object entity,double x,double y,double z) throws Exception {
  Method setter=MiningDump.optional(ENTITY,"lerpMotion",VECTOR);
  if(setter!=null)setter.invoke(entity,EntityInterpolationDump.vector(x,y,z));
  else method(ENTITY,"lerpMotion","double,double,double").invoke(entity,x,y,z);
 }
 public static void main(String[] args) throws Exception {
  try { measure(args); }
  catch(Throwable failure) {
   try(java.io.PrintWriter output=new java.io.PrintWriter(Paths.get(args[2]+".error.txt").toFile())) { failure.printStackTrace(output); }
   throw failure;
  }
 }
 public static void measure(String[] args) throws Exception {
  EntityPlacementProbe.initialize(args[0]);Object world=EntityPlacementProbe.world();List<Object> cases=new ArrayList<>();
  boolean stepped=MiningDump.clsOrNull("net.minecraft.world.entity.PositionPath")!=null;
  Method tick=method(REMOTE,"aiStep","");
  for(String name:new String[]{"linear","repeat","retarget","motion","motion_retarget","wrap_rotation","pose","stationary"}) {
   Object entity=EntityPlacementProbe.player(world),handler=EntityInterpolationDump.handler(entity);
   if(EntityPlacementProbe.implementation(entity.getClass(),tick).getDeclaringClass()!=MiningDump.cls(REMOTE))throw new IllegalStateException("Remote aiStep was replaced");
   method(ENTITY,"setPos","double,double,double").invoke(entity,1.5,64.0,1.5);
   method(ENTITY,"setRot","float,float").invoke(entity,name.equals("wrap_rotation")?170.0F:0.0F,0.0F);
   List<Object> ticks=new ArrayList<>();
   for(int index=0;index<12;index++) {
    List<Object> operations=new ArrayList<>();
    if(index==0&&!name.equals("stationary")) {
     EntityInterpolationDump.target(entity,4.5,67.0,-1.5,name.equals("wrap_rotation")?-170.0F:90.0F,30.0F,3);
     operations.add("target");
    }
    if(index==1&&(name.equals("repeat")||name.equals("retarget"))) {
     EntityInterpolationDump.target(entity,name.equals("repeat")?4.5:-2.5,name.equals("repeat")?67.0:65.0,name.equals("repeat")?-1.5:3.5,name.equals("repeat")?90.0F:-90.0F,name.equals("repeat")?30.0F:-15.0F,3);operations.add(name);
    }
    if(index==0&&(name.equals("motion")||name.equals("motion_retarget"))) {motion(entity,.3,-.2,.7);operations.add("motion");}
    if(index==1&&name.equals("motion_retarget")) {motion(entity,-.15,.4,-.35);operations.add("motion_retarget");}
    if(index==1&&name.equals("pose")) {
     Object pose;
     try { pose=MiningDump.field("net.minecraft.world.entity.Pose","CROUCHING"); }
     catch(NoSuchFieldException oldNamedPose) { pose=MiningDump.field("net.minecraft.world.entity.Pose","SNEAKING"); }
     method(ENTITY,"setPose","net.minecraft.world.entity.Pose").invoke(entity,pose);operations.add("crouch");
    }
    Map<String,Object> row=new LinkedHashMap<>();row.put("tick",index);row.put("operations",operations);row.put("before",EntityInterpolationDump.state(entity,handler,stepped));
    if(stepped&&handler!=null)method("net.minecraft.world.entity.InterpolationHandler","interpolate","").invoke(handler);
    tick.invoke(entity);
    row.put("after",EntityInterpolationDump.state(entity,handler,stepped));row.put("velocity",EntityInterpolationDump.vectorValues(method(ENTITY,"getDeltaMovement","").invoke(entity)));ticks.add(row);
   }
   Map<String,Object> entry=new LinkedHashMap<>();entry.put("name",name);entry.put("ticks",ticks);cases.add(entry);
  }
  Map<String,Object> result=new LinkedHashMap<>();result.put("actor","owned RemotePlayer subtype; only survival game-mode queries overridden");result.put("scope","native constructors and complete RemotePlayer.aiStep under a controlled client-level adapter; 26.3 interpolation handler advanced explicitly before aiStep, not a complete entity or world tick");result.put("cases",cases);
  Object gson=Class.forName("com.google.gson.Gson").getConstructor().newInstance();Files.write(Paths.get(args[2]),((String)gson.getClass().getMethod("toJson",Object.class).invoke(gson,result)).getBytes(StandardCharsets.UTF_8));System.out.println("native remote traces="+cases.size());
 }
}
