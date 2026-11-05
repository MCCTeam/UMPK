import java.io.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;
import java.util.function.*;
import java.util.jar.*;
public final class LegacyTagBinding {
 static Set<Object> resolve(String name,Map<String,Map<?,?>> raw,Map<String,Set<Object>> done,Set<String> pending,Map<String,Object> blocks){
  if(done.containsKey(name))return done.get(name);
  if(!pending.add(name))throw new IllegalStateException("tag cycle "+name);
  Map<?,?> doc=raw.get(name);if(doc==null)throw new IllegalStateException("missing tag "+name);
  Set<Object> values=new LinkedHashSet<>();
  for(Object row:(List<?>)doc.get("values")){
   String key;boolean required=true;
   if(row instanceof String)key=(String)row;else{Map<?,?> entry=(Map<?,?>)row;key=(String)entry.get("id");required=!Boolean.FALSE.equals(entry.get("required"));}
   boolean reference=key.startsWith("#");if(reference)key=key.substring(1);if(!key.contains(":"))key="minecraft:"+key;
   if(reference){if(!required&&!raw.containsKey(key))continue;values.addAll(resolve(key,raw,done,pending,blocks));}
   else{Object b=blocks.get(key);if(b==null){if(required)throw new IllegalStateException("missing block "+key);}else values.add(b);}
  }
  pending.remove(name);done.put(name,values);return values;
 }
 @SuppressWarnings("unchecked")
 static void bind(Map<String,Object> symbols,Object gson,Object blocks,Method key,Path jar)throws Exception{
  if(!symbols.containsKey("block_tags"))return;
  Map<String,String> binding=(Map<String,String>)symbols.get("block_tags");Map<String,Map<?,?>> raw=new TreeMap<>();
  try(JarFile file=new JarFile(jar.toFile())){
   Enumeration<JarEntry> entries=file.entries();
   while(entries.hasMoreElements()){
    JarEntry entry=entries.nextElement();String path=entry.getName();String prefix="data/minecraft/tags/blocks/";
    if(!path.startsWith(prefix)||!path.endsWith(".json"))continue;
    try(Reader reader=new InputStreamReader(file.getInputStream(entry),"UTF-8")){raw.put("minecraft:"+path.substring(prefix.length(),path.length()-5),(Map<?,?>)gson.getClass().getMethod("fromJson",Reader.class,Class.class).invoke(gson,reader,Map.class));}
   }
  }
  if(raw.isEmpty())throw new IllegalStateException("missing artifact block tags");
  Map<String,Object> values=new HashMap<>();for(Object b:(Iterable<?>)blocks)values.put(key.invoke(blocks,b).toString(),b);
  Map<String,Set<Object>> resolved=new TreeMap<>();for(String name:raw.keySet())resolve(name,raw,resolved,new HashSet<>(),values);
  Class<?> tag=Class.forName(binding.get("tag")),collection=Class.forName(binding.get("collection")),identifier=key.invoke(blocks,((Iterable<?>)blocks).iterator().next()).getClass();
  List<Constructor<?>> entries=new ArrayList<>();for(Class<?> nested:tag.getDeclaredClasses())for(Constructor<?> ctor:nested.getConstructors())if(Arrays.equals(ctor.getParameterTypes(),new Class<?>[]{Collection.class}))entries.add(ctor);
  if(entries.size()!=1)throw new IllegalStateException("tag values entry candidates="+entries);
  Map<Object,Object> tags=new HashMap<>();Constructor<?> id=identifier.getConstructor(String.class),tagConstructor=tag.getConstructor(identifier,Collection.class,boolean.class);
  for(Map.Entry<String,Set<Object>> entry:resolved.entrySet()){
   Object name=id.newInstance(entry.getKey());tags.put(name,tagConstructor.newInstance(name,Collections.singletonList(entries.get(0).newInstance(entry.getValue())),false));
  }
  Function<Object,Object> lookup=x->values.get(x.toString());Object target;
  if(collection.getConstructors()[0].getParameterCount()==5){Predicate<Object> contains=x->values.containsKey(x.toString());target=collection.getConstructor(Predicate.class,Function.class,String.class,boolean.class,String.class).newInstance(contains,lookup,"tags/blocks",false,"blocks");}
  else{Function<Object,Optional<Object>> optional=x->Optional.ofNullable(lookup.apply(x));target=collection.getConstructor(Function.class,String.class,boolean.class,String.class).newInstance(optional,"tags/blocks",false,"blocks");}
  List<Field> fields=new ArrayList<>();for(Field field:collection.getDeclaredFields())if(!Modifier.isStatic(field.getModifiers())&&Map.class.isAssignableFrom(field.getType()))fields.add(field);
  if(fields.size()!=1)throw new IllegalStateException("tag maps="+fields);Field field=fields.get(0);field.setAccessible(true);
  if(Modifier.isFinal(field.getModifiers())){Map<Object,Object> contents=(Map<Object,Object>)field.get(target);contents.clear();contents.putAll(tags);}else field.set(target,tags);
  Class.forName(binding.get("owner")).getMethod(binding.get("reset"),collection).invoke(null,target);
  System.out.println("bound block tags="+resolved.size());
 }
}
