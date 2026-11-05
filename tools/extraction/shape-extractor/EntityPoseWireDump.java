import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Writes and reads every native pose through its actual metadata serializer, retaining invalid-id results. Arguments: mappings-or-none, unused-classpath, output.json. */
public final class EntityPoseWireDump {
    static final String BUFFER = "net.minecraft.network.FriendlyByteBuf", SERIALIZER = "net.minecraft.network.syncher.EntityDataSerializer";
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static Object buffer() throws Exception {
        Object bytes = Class.forName("io.netty.buffer.Unpooled").getMethod("buffer").invoke(null);
        return MiningDump.cls(BUFFER).getConstructor(Class.forName("io.netty.buffer.ByteBuf")).newInstance(bytes);
    }
    static String hex(Object buffer) throws Exception {
        Class<?> type = Class.forName("io.netty.buffer.ByteBuf");
        byte[] bytes = new byte[(Integer)type.getMethod("readableBytes").invoke(buffer)];
        type.getMethod("getBytes", int.class, byte[].class).invoke(buffer, 0, bytes);
        StringBuilder hex = new StringBuilder();
        for (byte value : bytes) hex.append(String.format(Locale.ROOT, "%02x", value & 255));
        return hex.toString();
    }
    static void release(Object buffer) throws Exception { Class.forName("io.netty.buffer.ByteBuf").getMethod("release").invoke(buffer); }
    public static void main(String[] args) throws Exception {
        EntityPlacementProbe.initialize(args[0]);
        Object serializer = MiningDump.field("net.minecraft.network.syncher.EntityDataSerializers", "POSE");
        Method stream = MiningDump.optional(SERIALIZER, "codec", "");
        Object codec = stream == null ? serializer : stream.invoke(serializer);
        Method write = stream == null ? method(SERIALIZER, "write", BUFFER + ",java.lang.Object") : method("net.minecraft.network.codec.StreamEncoder", "encode", "java.lang.Object,java.lang.Object");
        Method read = stream == null ? method(SERIALIZER, "read", BUFFER) : method("net.minecraft.network.codec.StreamDecoder", "decode", "java.lang.Object");
        Method integer = method(BUFFER, "writeVarInt", "int"), byteValue = Class.forName("io.netty.buffer.ByteBuf").getMethod("writeByte", int.class);
        int serializerId = (Integer)method("net.minecraft.network.syncher.EntityDataSerializers", "getSerializedId", SERIALIZER).invoke(null, serializer);
        Object[] values = MiningDump.cls("net.minecraft.world.entity.Pose").getEnumConstants();
        List<Object> poses = new ArrayList<>(), invalid = new ArrayList<>();
        for (Object pose : values) {
            Object payload = buffer(), frame = buffer();
            try {
                write.invoke(codec, payload, pose);
                String encoded = hex(payload);
                Object decoded = read.invoke(codec, payload);
                if (decoded != pose || (Integer)Class.forName("io.netty.buffer.ByteBuf").getMethod("readableBytes").invoke(payload) != 0) throw new IllegalStateException("Native pose did not round-trip");
                integer.invoke(frame, 1); byteValue.invoke(frame, 6); integer.invoke(frame, serializerId); write.invoke(codec, frame, pose); byteValue.invoke(frame, 255);
                poses.add(Arrays.asList(((Enum<?>)pose).name(), encoded, hex(frame)));
            } finally { release(payload); release(frame); }
        }
        for (int id : new int[] {-1, values.length, values.length + 1, 127, 128, Integer.MIN_VALUE, Integer.MAX_VALUE}) {
            Object payload = buffer();
            try {
                integer.invoke(payload, id);
                String encoded = hex(payload);
                try { invalid.add(Arrays.asList(id, encoded, ((Enum<?>)read.invoke(codec, payload)).name())); }
                catch (InvocationTargetException rejected) { invalid.add(Arrays.asList(id, encoded, "rejected", EntityPlacementProbe.error(rejected))); }
            } finally { release(payload); }
        }
        Map<String,Object> output = new LinkedHashMap<>(); output.put("serializer_id", serializerId); output.put("poses", poses); output.put("invalid", invalid);
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, output)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native pose wire witnesses=" + poses.size() + " invalid-id witnesses=" + invalid.size());
    }
}
