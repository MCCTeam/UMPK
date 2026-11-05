import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures unmapped 1.14–1.14.3 pose serializers using bindings resolved from that release's source. Arguments: bindings.json, output.json. */
public final class LegacyPoseWireDump {
    static Object buffer(Class<?> type) throws Exception {
        Object bytes = Class.forName("io.netty.buffer.Unpooled").getMethod("buffer").invoke(null);
        return type.getConstructor(Class.forName("io.netty.buffer.ByteBuf")).newInstance(bytes);
    }
    static Method unique(Class<?> owner, Class<?> result, Class<?>... args) throws Exception {
        List<Method> matches = new ArrayList<>();
        for (Method method : owner.getDeclaredMethods())
            if (method.getReturnType() == result && Arrays.equals(method.getParameterTypes(), args)) matches.add(method);
        if (matches.size() != 1) throw new IllegalStateException("Ambiguous native pose method: " + matches);
        Method method = matches.get(0); method.setAccessible(true); return method;
    }
    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Map<String,Object> bindings;
        try (Reader reader = Files.newBufferedReader(Paths.get(args[0]))) {
            bindings = (Map<String,Object>)gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, reader, Map.class);
        }
        Class.forName((String)bindings.get("bootstrap")).getMethod((String)bindings.get("boot")).invoke(null);
        Class<?> pose = Class.forName((String)bindings.get("pose")), type = Class.forName((String)bindings.get("buffer"));
        Class<?> serializers = Class.forName((String)bindings.get("serializers")), contract = Class.forName((String)bindings.get("serializer_interface"));
        Object serializer = serializers.getField((String)bindings.get("pose_field")).get(null);
        Method write = unique(contract, void.class, type, Object.class), read = unique(contract, Object.class, type);
        Method integer = type.getMethod((String)bindings.get("write_varint"), int.class), byteValue = Class.forName("io.netty.buffer.ByteBuf").getMethod("writeByte", int.class);
        int serializerId = (Integer)unique(serializers, int.class, contract).invoke(null, serializer);
        for (Class<?> nativeClass : new Class<?>[] {pose, type, serializers, contract})
            System.out.println("native interaction class " + nativeClass.getName() + " from " + nativeClass.getProtectionDomain().getCodeSource().getLocation());
        Object[] values = pose.getEnumConstants();
        List<Object> poses = new ArrayList<>(), invalid = new ArrayList<>();
        for (Object value : values) {
            Object payload = buffer(type), frame = buffer(type);
            try {
                write.invoke(serializer, payload, value);
                String encoded = EntityPoseWireDump.hex(payload);
                if (read.invoke(serializer, payload) != value) throw new IllegalStateException("Native pose did not round-trip");
                integer.invoke(frame, 1); byteValue.invoke(frame, 6); integer.invoke(frame, serializerId); write.invoke(serializer, frame, value); byteValue.invoke(frame, 255);
                poses.add(Arrays.asList(((Enum<?>)value).name(), encoded, EntityPoseWireDump.hex(frame)));
            } finally { EntityPoseWireDump.release(payload); EntityPoseWireDump.release(frame); }
        }
        for (int id : new int[] {-1, values.length, values.length + 1, 127, 128, Integer.MIN_VALUE, Integer.MAX_VALUE}) {
            Object payload = buffer(type);
            try {
                integer.invoke(payload, id); String encoded = EntityPoseWireDump.hex(payload);
                try { invalid.add(Arrays.asList(id, encoded, ((Enum<?>)read.invoke(serializer, payload)).name())); }
                catch (InvocationTargetException rejected) { invalid.add(Arrays.asList(id, encoded, "rejected", EntityPlacementProbe.error(rejected))); }
            } finally { EntityPoseWireDump.release(payload); }
        }
        Map<String,Object> output = new LinkedHashMap<>(); output.put("serializer_id", serializerId); output.put("poses", poses); output.put("invalid", invalid);
        Files.write(Paths.get(args[1]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, output)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native unmapped pose witnesses=" + poses.size() + " invalid-id witnesses=" + invalid.size());
    }
}
