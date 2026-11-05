import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures native relative-position decoding independently of entity interpolation. Arguments: mappings-or-none, unused-classpath, output.json. */
public final class EntityDeltaPositionDump {
    static final String VECTOR = "net.minecraft.world.phys.Vec3", PACKET = "net.minecraft.network.protocol.game.ClientboundMoveEntityPacket", CODEC = "net.minecraft.network.protocol.game.VecDeltaCodec";
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static List<Double> vector(Object value) throws Exception {
        List<Double> result = new ArrayList<>();
        for (String name : new String[] {"x", "y", "z"}) result.add(PushDump.field(VECTOR, name).getDouble(value));
        return result;
    }
    public static void main(String[] args) throws Exception {
        PushDump.loader = EntityDeltaPositionDump.class.getClassLoader();
        if (!args[0].equals("none")) PushDump.parse(Paths.get(args[0]));
        Class<?> vector = MiningDump.cls(VECTOR), codecType = MiningDump.clsOrNull(CODEC);
        Constructor<?> constructVector = vector.getConstructor(double.class, double.class, double.class);
        Method update = MiningDump.optional(PACKET, "updateEntityPosition", VECTOR);
        Method encode = codecType == null && update == null ? method(PACKET, "entityToPacket", "double") : null;
        Method fromPacket = codecType == null && update == null ? method(PACKET, "packetToEntity", "long,long,long") : null;
        Object codec = codecType == null ? null : codecType.getConstructor().newInstance();
        Method setBase = codecType == null ? null : method(CODEC, "setBase", VECTOR);
        Method decode = codecType == null ? null : method(CODEC, "decode", "long,long,long");
        double half = 0.5 / 4096.0;
        double[][] origins = {{1.1,65.2,-2.3},{-1.1,-64.2,2.3},{half,-half,65},{Math.nextDown(half),Math.nextUp(-half),-64},{Math.nextUp(half),Math.nextDown(-half),0},{29999999.12345,-64.12345,-29999999.12345},{0,0,0},{1.25,65.5,-2.75}};
        short[][][] sequences = {{{0,0,0}},{{1,0,0}},{{-1,0,0}},{{0,1,0}},{{0,0,-1}},{{32767,-32768,1023}},{{1,0,0},{-1,0,0}},{{0,1,0},{0,-1,0},{0,0,0}},{{1,2,3},{-1,-2,-3},{1,0,0}}};
        List<Object> rows = new ArrayList<>();
        for (double[] origin : origins) for (short[][] sequence : sequences) {
            Object current = constructVector.newInstance(origin[0], origin[1], origin[2]);
            List<Object> positions = new ArrayList<>();
            for (short[] delta : sequence) {
                if (codec != null) {
                    setBase.invoke(codec, current);
                    current = decode.invoke(codec, (long)delta[0], (long)delta[1], (long)delta[2]);
                } else if (update != null) {
                    Object packet = MiningDump.cls(PACKET + "$Pos").getConstructor(int.class, short.class, short.class, short.class, boolean.class).newInstance(1, delta[0], delta[1], delta[2], false);
                    current = update.invoke(packet, current);
                } else {
                    List<Double> base = vector(current);
                    current = fromPacket.invoke(null, (Long)encode.invoke(null, base.get(0)) + delta[0], (Long)encode.invoke(null, base.get(1)) + delta[1], (Long)encode.invoke(null, base.get(2)) + delta[2]);
                }
                positions.add(vector(current));
            }
            Map<String,Object> row = new LinkedHashMap<>(); row.put("base", origin); row.put("steps", sequence); row.put("positions", positions); rows.add(row);
        }
        Map<String,Object> result = new LinkedHashMap<>(); result.put("operation", codec != null ? "VecDeltaCodec.decode" : update != null ? "ClientboundMoveEntityPacket.updateEntityPosition" : "native packet-coordinate cache"); result.put("rows", rows);
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, result)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native delta-position sequences=" + rows.size());
    }
}
