import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Drives the source-resolved native floor used by older packet-coordinate caches. Arguments: bindings.json, output.json. */
public final class LegacyEntityDeltaPositionDump {
    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Map<String,Object> bindings;
        try (Reader reader = Files.newBufferedReader(Paths.get(args[0]))) { bindings = (Map<String,Object>)gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, reader, Map.class); }
        Class<?> math = Class.forName((String)bindings.get("owner"));
        Method floor = math.getDeclaredMethod((String)bindings.get("method"), double.class);
        int units = ((Number)bindings.get("units")).intValue();
        double half = 0.5 / 4096.0;
        double[][] origins = units == 32 ? new double[][] {{1.25,65.5,-2.75},{-1.25,-64.5,2.75},{0,0,0},{29999999.5,100,-29999999.5}}
            : new double[][] {{1.1,65.2,-2.3},{-1.1,-64.2,2.3},{half,-half,65},{Math.nextDown(half),Math.nextUp(-half),-64},{Math.nextUp(half),Math.nextDown(-half),0},{29999999.12345,-64.12345,-29999999.12345},{0,0,0},{1.25,65.5,-2.75}};
        int maximum = units == 32 ? 127 : 32767, minimum = units == 32 ? -128 : -32768;
        int[][][] sequences = {{{0,0,0}},{{1,0,0}},{{-1,0,0}},{{0,1,0}},{{0,0,-1}},{{maximum,minimum,31}},{{1,0,0},{-1,0,0}},{{0,1,0},{0,-1,0},{0,0,0}},{{1,2,3},{-1,-2,-3},{1,0,0}}};
        List<Object> rows = new ArrayList<>();
        for (double[] origin : origins) for (int[][] sequence : sequences) {
            double[] current = origin.clone();
            List<Object> positions = new ArrayList<>();
            for (int[] delta : sequence) {
                double[] next = new double[3];
                for (int axis = 0; axis < 3; axis++) {
                    long encoded = (Long)floor.invoke(null, current[axis] * units);
                    long value = units == 32 ? (int)encoded + delta[axis] : encoded + delta[axis];
                    next[axis] = value / (double)units;
                }
                current = next;
                positions.add(current);
            }
            Map<String,Object> row = new LinkedHashMap<>(); row.put("base", origin); row.put("steps", sequence); row.put("positions", positions); rows.add(row);
        }
        Map<String,Object> result = new LinkedHashMap<>(); result.put("operation", "source-resolved native floor and packet-coordinate cache"); result.put("rows", rows);
        Files.write(Paths.get(args[1]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, result)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native legacy delta-position sequences=" + rows.size());
    }
}
