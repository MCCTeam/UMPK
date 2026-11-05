import java.io.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;
public final class LegacyMiningGeneric {
 static Class<?> cls(String n)throws Exception{return Class.forName(n);}
 static Object registry(List<?> binding)throws Exception{Field f=cls((String)binding.get(0)).getDeclaredField((String)binding.get(1));f.setAccessible(true);return f.get(null);}
 static Object states(Class<?> block,Class<?> state)throws Exception{
  List<Object> found=new ArrayList<>();
  for(Field f:block.getDeclaredFields()){
   if(!Modifier.isStatic(f.getModifiers())||!Iterable.class.isAssignableFrom(f.getType()))continue;
   f.setAccessible(true);Object value=f.get(null);Iterator<?> it=((Iterable<?>)value).iterator();
   if(it.hasNext()&&state.isInstance(it.next()))found.add(value);
  }
  if(found.size()!=1)throw new IllegalStateException("state registry candidates="+found.size());return found.get(0);
 }
 static Method sole(Class<?> owner,Class<?> returns,int args,Class<?> parameter)throws Exception{
  List<Method> found=new ArrayList<>();
  for(Method m:owner.getMethods()){
   if(m.getReturnType()!=returns||m.getParameterCount()!=args||Modifier.isStatic(m.getModifiers()))continue;
   if(args==1&&!m.getParameterTypes()[0].isAssignableFrom(parameter))continue;
   found.add(m);
  }
  if(found.size()!=1)throw new IllegalStateException("method candidates "+owner+" -> "+returns+"="+found);return found.get(0);
 }
 static Method key(Object registry,Object sample)throws Exception{
  List<Method> found=new ArrayList<>();
  for(Method m:registry.getClass().getMethods()){
   if(m.getParameterCount()!=1||m.getReturnType().isPrimitive()||Modifier.isStatic(m.getModifiers())||!m.getParameterTypes()[0].isInstance(sample))continue;
   try{Object v=m.invoke(registry,sample);if(v!=null&&v.toString().matches("[a-z0-9._-]+:[a-z0-9./_-]+"))found.add(m);}catch(InvocationTargetException ignored){}
  }
  if(found.size()!=1)throw new IllegalStateException("key candidates="+found);return found.get(0);
 }
 @SuppressWarnings("unchecked")
 public static void main(String[]args)throws Exception{
  Object gson=cls("com.google.gson.Gson").getConstructor().newInstance();
  Map<String,Object> symbols;
  try(Reader r=Files.newBufferedReader(Paths.get(args[0]))){symbols=(Map<String,Object>)gson.getClass().getMethod("fromJson",Reader.class,Class.class).invoke(gson,r,Map.class);}
  Class<?> block=cls((String)symbols.get("block")),state=cls((String)symbols.get("state")),item=cls((String)symbols.get("item")),stack=cls((String)symbols.get("stack")),statelike=cls((String)symbols.get("statelike"));
  cls((String)symbols.get("bootstrap")).getMethod((String)symbols.get("boot")).invoke(null);
  Map<String,List<?>> bindings=(Map<String,List<?>>)symbols.get("registries");
  Object blocks=registry(bindings.get("block")),items=registry(bindings.get("item")),states=states(block,state);
  Object firstState=((Iterable<?>)states).iterator().next();Object firstBlock=((Iterable<?>)blocks).iterator().next();Object firstItem=((Iterable<?>)items).iterator().next();
  Method owner=sole(state,block,0,null),id=sole(states.getClass(),int.class,1,state),blockKey=key(blocks,firstBlock),itemKey=key(items,firstItem);
  LegacyTagBinding.bind(symbols,gson,blocks,blockKey,Paths.get(args[2]));
  List<String> argsNames=(List<String>)symbols.get("hardness_args");Class<?>[] hardTypes=new Class<?>[argsNames.size()];for(int i=0;i<hardTypes.length;i++)hardTypes[i]=cls(argsNames.get(i));
  Method hardness=block.getMethod((String)symbols.get("hardness"),hardTypes),material=statelike.getMethod((String)symbols.get("material"));
  Method always=material.getReturnType().getMethod((String)symbols.get("always_destroyable"));
  Method speed=stack.getMethod((String)symbols.get("speed"),statelike),correct=stack.getMethod((String)symbols.get("correct"),statelike);
  List<Constructor<?>> ctors=new ArrayList<>();for(Constructor<?> c:stack.getConstructors())if(c.getParameterCount()==1&&c.getParameterTypes()[0].isAssignableFrom(item))ctors.add(c);
  if(ctors.size()!=1)throw new IllegalStateException("stack constructors="+ctors);Constructor<?> ctor=ctors.get(0);
  List<Object> values=new ArrayList<>();List<Integer> ids=new ArrayList<>();StringBuilder out=new StringBuilder("{\"states\":{");boolean first=true;
  for(Object value:(Iterable<?>)states){
   Object b=owner.invoke(value);int stateId=(Integer)id.invoke(states,value);ids.add(stateId);Object like=statelike==block?b:value;values.add(like);
   Object[] parameters=new Object[hardTypes.length];if(hardTypes.length==3)parameters[0]=value;
   float h=(Float)hardness.invoke(b,parameters);boolean required=!(Boolean)always.invoke(material.invoke(like));
   if(!Float.isFinite(h))throw new IllegalStateException("non-finite hardness");
   if(!first)out.append(',');first=false;
   out.append('"').append(stateId).append("\":[\"").append(blockKey.invoke(blocks,b)).append("\",").append(h).append(',').append(required).append(']');
  }
  out.append("},\"items\":{");first=true;int itemCount=0;boolean air=false;
  for(Object object:(Iterable<?>)items){
   String name=itemKey.invoke(items,object).toString();if(name.equals("minecraft:air"))air=true;
   Object held=ctor.newInstance(object);if(!first)out.append(',');first=false;out.append('"').append(name).append("\":{");boolean rowFirst=true;
   for(int i=0;i<values.size();i++){
    float s=(Float)speed.invoke(held,values.get(i));boolean c=(Boolean)correct.invoke(held,values.get(i));
    if(!Float.isFinite(s)||s<0)throw new IllegalStateException("invalid speed for "+name);
    if(s==1.0F&&!c)continue;if(!rowFirst)out.append(',');rowFirst=false;
    out.append('"').append(ids.get(i)).append("\":[").append(s).append(',').append(c).append(']');
   }
   out.append('}');itemCount++;
  }
  if(!air){out.append(",\"minecraft:air\":{}");itemCount++;}
  out.append("}}\n");Files.write(Paths.get(args[1]),out.toString().getBytes("UTF-8"));
  System.out.println("version="+symbols.get("version")+" states="+ids.size()+" items="+itemCount+" stateId="+id+" blockKey="+blockKey+" itemKey="+itemKey);
 }
}
