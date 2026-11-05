import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
/** Emits owned client-level and survival RemotePlayer adapters. Native entity constructors and aiStep remain unchanged; the world adapter is allocated by the existing controlled-world harness, without running its constructor. */
public final class EntityRemotePlayerSource {
 static void override(StringBuilder extra, Method method, String body) {
  extra.append(" @Override public ").append(EntityWorldSource.type(method.getReturnType())).append(' ').append(method.getName()).append('(').append(EntityWorldSource.parameters(method.getParameterTypes())).append(") { ").append(body).append(" }\n");
 }
 static String replaceOrAppend(String source,Method method,String body) {
  String signature=" "+method.getName()+"("+EntityWorldSource.parameters(method.getParameterTypes())+") {";
  int start=source.indexOf(signature);
  if(start>=0) { int begin=start+signature.length(),end=source.indexOf('}',begin);return source.substring(0,begin)+" "+body+" "+source.substring(end); }
  StringBuilder extra=new StringBuilder();override(extra,method,body);int end=source.lastIndexOf('}');return source.substring(0,end)+extra+source.substring(end);
 }
 public static void main(String[] args) throws Exception {
  EntityWorldSource.main(args);
  Class<?> remote=MiningDump.cls("net.minecraft.client.player.RemotePlayer"),level=MiningDump.cls("net.minecraft.world.level.Level");
  Constructor<?> constructor=remote.getConstructors()[0];Class<?> clientLevel=constructor.getParameterTypes()[0];
  List<Constructor<?>> candidates=new ArrayList<>();for(Constructor<?> value:clientLevel.getDeclaredConstructors()) if(!Modifier.isPrivate(value.getModifiers())) candidates.add(value);
  if(candidates.size()!=1)throw new IllegalStateException("Ambiguous client world constructors");
  List<String> params=new ArrayList<>();for(Class<?> p:candidates.get(0).getParameterTypes())params.add(EntityWorldSource.zero(p));
  Path world=Paths.get(args[1]);String source=new String(Files.readAllBytes(world),StandardCharsets.UTF_8);
  source=source.replace("extends "+EntityWorldSource.type(level)+" {","extends "+EntityWorldSource.type(clientLevel)+" {");
  int start=source.indexOf("super("),end=source.indexOf(");",start);source=source.substring(0,start)+"super("+String.join(",",params)+source.substring(end);
  Method angle=MiningDump.optional("net.minecraft.world.level.Level","getSharedSpawnAngle","");if(angle!=null)source=replaceOrAppend(source,angle,"return 0.0F;");
  Method entities=MiningDump.method("net.minecraft.world.level.Level","getEntities","net.minecraft.world.entity.Entity,net.minecraft.world.phys.AABB,java.util.function.Predicate");source=replaceOrAppend(source,entities,"return java.util.Collections.emptyList();");
  Method collision=MiningDump.optional("net.minecraft.world.level.CollisionGetter","noCollision","net.minecraft.world.entity.Entity,net.minecraft.world.phys.AABB");if(collision!=null)source=replaceOrAppend(source,collision,"try { return EntityInterpolationDump.collision(p1); } catch(Exception error) { throw new IllegalStateException(error); }");
  Method pushes=MiningDump.optional("net.minecraft.world.level.Level","getPushableEntities","net.minecraft.world.entity.Entity,net.minecraft.world.phys.AABB");if(pushes!=null)source=replaceOrAppend(source,pushes,"return java.util.Collections.emptyList();");
  Method speed=MiningDump.optional("net.minecraft.world.level.Level","getRelativeTickSpeed","");if(speed!=null)source=replaceOrAppend(source,speed,"return EntityInterpolationDump.relativeTickSpeed;");
  for(String signature:new String[]{"net.minecraft.world.level.entity.EntityTypeTest,net.minecraft.world.phys.AABB,java.util.function.Predicate","java.lang.Class,net.minecraft.world.phys.AABB,java.util.function.Predicate"}) {
   for(String name:new String[]{"getEntities","getEntitiesOfClass"}) {
    Method selected=MiningDump.optional("net.minecraft.world.level.Level",name,signature);
    if(selected!=null)source=replaceOrAppend(source,selected,"return java.util.Collections.emptyList();");
   }
  }
  Map<String,Method> effective=new TreeMap<>();
  for(Class<?> owner=clientLevel;owner!=null;owner=owner.getSuperclass())for(Method selected:owner.getDeclaredMethods())EntityWorldSource.remember(effective,selected);
  List<Method> originals=new ArrayList<>(Arrays.asList(level.getMethods()));
  for(Class<?> owner=level;owner!=null;owner=owner.getSuperclass())Collections.addAll(originals,owner.getDeclaredMethods());
  for(Method original:originals) {
   Method selected=effective.get(EntityWorldSource.signature(original));
   if(selected!=null&&selected.getReturnType()!=original.getReturnType()) {
    String oldType=EntityWorldSource.type(original.getReturnType()),newType=EntityWorldSource.type(selected.getReturnType());
    source=source.replace(oldType+" "+original.getName()+"("+EntityWorldSource.parameters(original.getParameterTypes())+") {",newType+" "+original.getName()+"("+EntityWorldSource.parameters(original.getParameterTypes())+") {");
    source=source.replace("return ("+oldType+")EntityPlacementProbe.worldValue", "return ("+newType+")EntityPlacementProbe.worldValue");
   }
  }
  String clientOwner=EntityPlacementProbe.named(clientLevel);
  Method spawn=MiningDump.optional(clientOwner,"getSharedSpawnPos","");if(spawn!=null)source=replaceOrAppend(source,spawn,"return ("+EntityWorldSource.type(spawn.getReturnType())+")EntityPlacementProbe.worldValue(\"getSharedSpawnPos\");");
  Method clientAngle=MiningDump.optional(clientOwner,"getSharedSpawnAngle","");if(clientAngle!=null)source=replaceOrAppend(source,clientAngle,"return 0.0F;");
  Files.write(world,source.getBytes(StandardCharsets.UTF_8));
  List<String> actorArgs=new ArrayList<>();actorArgs.add("("+EntityWorldSource.type(clientLevel)+")world");actorArgs.add("profile");
  for(int index=2;index<constructor.getParameterCount();index++) {
   if(!EntityPlacementProbe.named(constructor.getParameterTypes()[index]).equals("net.minecraft.world.entity.player.ProfilePublicKey"))throw new IllegalStateException("Unmeasured RemotePlayer constructor input");actorArgs.add("null");
  }
  StringBuilder actor=new StringBuilder("import com.mojang.authlib.GameProfile;\npublic final class EntityPlayerAdapter extends "+EntityWorldSource.type(remote)+" {\n public EntityPlayerAdapter("+EntityWorldSource.type(level)+" world,GameProfile profile) { super("+String.join(",",actorArgs)+"); }\n");
  for(String semantic:new String[]{"isSpectator","isCreative"}) {Method m=MiningDump.method("net.minecraft.world.entity.player.Player",semantic,"");override(actor,m,"return false;");}
  Method mode=MiningDump.optional("net.minecraft.world.entity.player.Player","gameMode","");if(mode!=null)override(actor,mode,"return "+EntityWorldSource.type(mode.getReturnType())+"."+PushDump.field("net.minecraft.world.level.GameType","SURVIVAL").getName()+";");
  Files.write(world.resolveSibling("EntityPlayerAdapter.java"),actor.append("}\n").toString().getBytes(StandardCharsets.UTF_8));
 }
}
