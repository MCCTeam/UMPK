import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;

/** Measures the native PiercingWeapon data/hash/wire codecs, real slot packet bodies and every item's default attack components. */
public final class PiercingComponentDump {
    static Method parse, encode, get, streamEncode, streamDecode, writerIndex, readableBytes, getBytes, release;
    static Object jsonContext, hashContext, provider;
    static Constructor<?> bufferConstructor;
    static Method newBuffer;
    static Object buffer() throws Exception { return bufferConstructor.newInstance(newBuffer.invoke(null),provider); }
    static byte[] encodeWire(Object codec,Object value) throws Exception {
        Object buffer=buffer();
        try {
            streamEncode.invoke(codec,buffer,value);
            byte[] bytes=new byte[(Integer)readableBytes.invoke(buffer)];
            getBytes.invoke(buffer,0,bytes);return bytes;
        } finally {release.invoke(buffer);}
    }
    static Object decodeWire(Object codec,byte[] bytes) throws Exception {
        Object buffer=buffer();
        try {
            buffer.getClass().getMethod("writeBytes",byte[].class).invoke(buffer,(Object)bytes);
            Object value=streamDecode.invoke(codec,buffer);
            if((Integer)readableBytes.invoke(buffer)!=0)throw new IllegalStateException("Native component left trailing bytes");
            return value;
        } finally {release.invoke(buffer);}
    }
    static String hex(byte[] bytes) {
        StringBuilder out=new StringBuilder();for(byte b:bytes)out.append(String.format(java.util.Locale.ROOT,"%02x",b&255));return out.toString();
    }
    static Object json(String source) throws Exception { return Class.forName("com.google.gson.JsonParser").getMethod("parseString",String.class).invoke(null,source); }
    static Object data(Object codec,Object value) throws Exception { return get.invoke(encode.invoke(codec,jsonContext,value)); }
    static int hash(Object codec,Object value) throws Exception {
        Object hash=get.invoke(encode.invoke(codec,hashContext,value));return (Integer)Class.forName("com.google.common.hash.HashCode").getMethod("asInt").invoke(hash);
    }
    static String sound(int kind) {
        if(kind==1)return "\"minecraft:entity.player.attack.sweep\"";
        if(kind==2)return "{\"sound_id\":\"umpk:probe\"}";
        if(kind==3)return "{\"sound_id\":\"umpk:probe\",\"range\":12.5}";
        throw new IllegalArgumentException("No sound for absence");
    }
    public static void main(String[] args)throws Exception {
        PushDump.loader=PiercingComponentDump.class.getClassLoader();if(!args[0].equals("none"))PushDump.parse(Paths.get(args[0]));
        MiningDump.identifierClass=MiningDump.clsOrNull("net.minecraft.resources.Identifier");if(MiningDump.identifierClass==null)MiningDump.identifierClass=MiningDump.cls("net.minecraft.resources.ResourceLocation");
        MiningDump.method("net.minecraft.SharedConstants","tryDetectVersion","").invoke(null);MiningDump.method("net.minecraft.server.Bootstrap","bootStrap","").invoke(null);MiningDump.initializeComponents();
        provider=MiningDump.method("net.minecraft.core.RegistryAccess","fromRegistryOfRegistries","net.minecraft.core.Registry").invoke(null,MiningDump.field("net.minecraft.core.registries.BuiltInRegistries","REGISTRY"));
        Method ops=MiningDump.method("net.minecraft.resources.RegistryOps","create","com.mojang.serialization.DynamicOps,net.minecraft.core.HolderLookup$Provider");
        jsonContext=ops.invoke(null,Class.forName("com.mojang.serialization.JsonOps").getField("INSTANCE").get(null),provider);
        hashContext=ops.invoke(null,MiningDump.field("net.minecraft.util.HashOps","CRC32C_INSTANCE"),provider);
        Class<?> dynamicOps=Class.forName("com.mojang.serialization.DynamicOps");
        parse=Class.forName("com.mojang.serialization.Decoder").getMethod("parse",dynamicOps,Object.class);encode=Class.forName("com.mojang.serialization.Encoder").getMethod("encodeStart",dynamicOps,Object.class);get=Class.forName("com.mojang.serialization.DataResult").getMethod("getOrThrow");
        Class<?> bytebuf=Class.forName("io.netty.buffer.ByteBuf"),registryBuffer=MiningDump.cls("net.minecraft.network.RegistryFriendlyByteBuf");
        for(Constructor<?> c:registryBuffer.getConstructors())if(c.getParameterCount()==2&&c.getParameterTypes()[0]==bytebuf&&c.getParameterTypes()[1].isInstance(provider))bufferConstructor=c;
        if(bufferConstructor==null)throw new IllegalStateException("No native registry buffer constructor");
        newBuffer=Class.forName("io.netty.buffer.Unpooled").getMethod("buffer");readableBytes=bytebuf.getMethod("readableBytes");getBytes=bytebuf.getMethod("getBytes",int.class,byte[].class);release=bytebuf.getMethod("release");
        Class<?> stream=MiningDump.cls("net.minecraft.network.codec.StreamCodec");streamEncode=stream.getMethod("encode",Object.class,Object.class);streamDecode=stream.getMethod("decode",Object.class);
        Object codec=MiningDump.field("net.minecraft.world.item.component.PiercingWeapon","CODEC"),wire=MiningDump.field("net.minecraft.world.item.component.PiercingWeapon","STREAM_CODEC"),slotWire=MiningDump.field("net.minecraft.network.protocol.game.ClientboundContainerSetSlotPacket","STREAM_CODEC");
        Object piercingKey=MiningDump.field("net.minecraft.core.component.DataComponents","PIERCING_WEAPON"),chargeKey=MiningDump.field("net.minecraft.core.component.DataComponents","MINIMUM_ATTACK_CHARGE");
        Method set=MiningDump.method("net.minecraft.world.item.ItemStack","set","net.minecraft.core.component.DataComponentType,java.lang.Object");
        Object stone=MiningDump.field("net.minecraft.world.item.Items","STONE");Method defaultStack=MiningDump.method("net.minecraft.world.item.Item","getDefaultInstance","");
        Constructor<?> slot=MiningDump.cls("net.minecraft.network.protocol.game.ClientboundContainerSetSlotPacket").getConstructor(int.class,int.class,int.class,MiningDump.cls("net.minecraft.world.item.ItemStack"));
        List<Object> rows=new ArrayList<>();
        for(int flags=0;flags<4;flags++)for(int sound=0;sound<4;sound++)for(int hit=0;hit<4;hit++) {
            String input="{\"deals_knockback\":"+((flags&1)!=0)+",\"dismounts\":"+((flags&2)!=0)+(sound==0?"":",\"sound\":"+sound(sound))+(hit==0?"":",\"hit_sound\":"+sound(hit))+"}";
            Object value=get.invoke(parse.invoke(codec,jsonContext,json(input)));byte[] bytes=encodeWire(wire,value);Object decoded=decodeWire(wire,bytes);
            if(!Arrays.equals(bytes,encodeWire(wire,decoded))||!data(codec,value).equals(data(codec,decoded)))throw new IllegalStateException("Native component roundtrip differed");
            Object stack=defaultStack.invoke(stone);set.invoke(stack,piercingKey,value);Object packet=slot.newInstance(0,7,36,stack);
            Map<String,Object> row=new LinkedHashMap<>();row.put("flags",flags);row.put("sound_kind",sound);row.put("hit_sound_kind",hit);row.put("data",data(codec,value));row.put("hash",hash(codec,value));row.put("component_hex",hex(bytes));row.put("slot_hex",hex(encodeWire(slotWire,packet)));rows.add(row);
        }
        String getterOwner=MiningDump.clsOrNull("net.minecraft.core.component.DataComponentGetter")==null?"net.minecraft.core.component.DataComponentMap":"net.minecraft.core.component.DataComponentGetter";
        Method componentGet=MiningDump.method(getterOwner,"get","net.minecraft.core.component.DataComponentType"),components=MiningDump.method("net.minecraft.world.item.Item","components","");
        Object items=MiningDump.registry("ITEM"),sounds=MiningDump.registry("SOUND_EVENT");Map<String,Object> defaults=new TreeMap<>();
        for(Object item:(Iterable<?>)items) {
            Object map=components.invoke(item),piercing=componentGet.invoke(map,piercingKey),charge=componentGet.invoke(map,chargeKey);Map<String,Object> row=new LinkedHashMap<>();row.put("piercing_weapon",piercing==null?null:data(codec,piercing));row.put("minimum_attack_charge",charge);defaults.put(MiningDump.key(items,item),row);
        }
        Map<Integer,String> soundNames=new TreeMap<>();Method id=MiningDump.method("net.minecraft.core.Registry","getId","java.lang.Object");
        for(Object value:(Iterable<?>)sounds)soundNames.put((Integer)id.invoke(sounds,value),MiningDump.key(sounds,value));
        Map<String,Object> out=new LinkedHashMap<>();out.put("cases",rows);out.put("default_components",defaults);out.put("sound_names",soundNames);
        Object gson=Class.forName("com.google.gson.GsonBuilder").getConstructor().newInstance();gson=gson.getClass().getMethod("setPrettyPrinting").invoke(gson);gson=gson.getClass().getMethod("create").invoke(gson);String result=(String)gson.getClass().getMethod("toJson",Object.class).invoke(gson,out);
        Files.write(Paths.get(args[1]),(result+"\n").getBytes(java.nio.charset.StandardCharsets.UTF_8));
    }
}
