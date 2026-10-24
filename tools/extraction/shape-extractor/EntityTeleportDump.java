import java.lang.reflect.*;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;

/** Executes native PositionMoveRotation.calculateAbsolute for every relative-flag combination. Arguments: mappings-or-none, unused-classpath, output.json. */
public final class EntityTeleportDump {
    static final String VECTOR = "net.minecraft.world.phys.Vec3", VALUES = "net.minecraft.world.entity.PositionMoveRotation", RELATIVE = "net.minecraft.world.entity.Relative";
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static final double[][] SOURCES = {
        {10.25,64.5,-2.75,0.3,-0.2,0.7,37.25,-23.75},
        {-10.25,-64.5,2.75,-0.3,0.2,-0.7,-179.9,89.9},
        {29999999.12345,-64.12345,-29999999.12345,1.25,-2.5,3.75,170,-89.99},
        {0,0,0,0,0,0,0,0},
        {0.5,-0.5,1.5,0.1,0.2,0.3,720.125,80},
        {-1,2,-3,-1,2,-3,-450,-80},
        {1,64,2,0.125,-0.25,0.5,2147483648.0,0},
        {1,64,2,0.125,-0.25,0.5,-2147483648.0,0}
    };
    static final double[][] CHANGES = {
        {2.5,-3.25,4.75,0.01,0.02,-0.03,13.125,17.625},
        {-0.25,1.5,0.125,-0.01,0.02,0.03,179.9,90.1},
        {-100.5,128.25,100.5,0.25,-0.5,0.75,-170,-90.01},
        {-0.0,0.0,-0.0,-0.0,0.0,-0.0,-0.0,-0.0},
        {1,2,3,0.2,0.3,0.4,370,450},
        {-1,-2,-3,-0.2,-0.3,-0.4,-370,-450},
        {0,0,0,0,0,0,1,0.000001},
        {0,0,0,0,0,0,-1,-0.000001}
    };
    static Object values(Constructor<?> vector, Constructor<?> values, double[] input) throws Exception {
        return values.newInstance(vector.newInstance(input[0],input[1],input[2]),vector.newInstance(input[3],input[4],input[5]),(float)input[6],(float)input[7]);
    }
    static List<Number> read(Object value) throws Exception {
        List<Number> output = new ArrayList<>();
        for (String accessor : new String[] {"position","deltaMovement"}) {
            Object vector = method(VALUES,accessor,"").invoke(value);
            for (String axis : new String[] {"x","y","z"}) output.add(PushDump.field(VECTOR,axis).getDouble(vector));
        }
        output.add((Float)method(VALUES,"yRot","").invoke(value));
        output.add((Float)method(VALUES,"xRot","").invoke(value));
        return output;
    }
    public static void main(String[] args) throws Exception {
        PushDump.loader = EntityTeleportDump.class.getClassLoader();
        if (!args[0].equals("none")) PushDump.parse(Paths.get(args[0]));
        Constructor<?> vector = MiningDump.cls(VECTOR).getConstructor(double.class,double.class,double.class);
        Constructor<?> values = MiningDump.cls(VALUES).getConstructor(MiningDump.cls(VECTOR),MiningDump.cls(VECTOR),float.class,float.class);
        Method absolute = method(VALUES,"calculateAbsolute",VALUES+","+VALUES+",java.util.Set");
        Method unpack = method(RELATIVE,"unpack","int");
        List<Object> inputs = new ArrayList<>(), rows = new ArrayList<>();
        for (int index=0; index<SOURCES.length; index++) {
            Object source=values(vector,values,SOURCES[index]), change=values(vector,values,CHANGES[index]);
            Map<String,Object> input=new LinkedHashMap<>(); input.put("source",read(source)); input.put("change",read(change)); inputs.add(input);
            for (int flags=0; flags<512; flags++) {
                Map<String,Object> row=new LinkedHashMap<>(); row.put("input",index); row.put("flags",flags);
                row.put("result",read(absolute.invoke(null,source,change,unpack.invoke(null,flags)))); rows.add(row);
            }
        }
        float[] sine=(float[])PushDump.field("net.minecraft.util.Mth","SIN").get(null);
        ByteBuffer bytes=ByteBuffer.allocate(sine.length*4);
        for (float value : sine) bytes.putFloat(value);
        StringBuilder hash=new StringBuilder();
        for (byte value : MessageDigest.getInstance("SHA-256").digest(bytes.array())) hash.append(String.format("%02x",value&255));
        Map<String,Object> output=new LinkedHashMap<>(); output.put("operation","PositionMoveRotation.calculateAbsolute");
        output.put("inputs",inputs); output.put("rows",rows); output.put("sine_sha256",hash.toString());
        Object gson=Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]),((String)gson.getClass().getMethod("toJson",Object.class).invoke(gson,output)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native teleport cases="+rows.size());
    }
}
