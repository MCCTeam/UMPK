import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures native full-cube/entity-box intersection near each contact plane, including large world coordinates. Arguments: mappings-or-none, unused-classpath, output.json. */
public final class PlacementIntersectionDump {
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static Object shape(double[] bounds) throws Exception {
        return method("net.minecraft.world.phys.shapes.Shapes", "box", "double,double,double,double,double,double").invoke(null, bounds[0], bounds[1], bounds[2], bounds[3], bounds[4], bounds[5]);
    }
    public static void main(String[] args) throws Exception {
        PushDump.loader = PlacementIntersectionDump.class.getClassLoader();
        if (!args[0].equals("none")) PushDump.parse(Paths.get(args[0]));
        Method join = method("net.minecraft.world.phys.shapes.Shapes", "joinIsNotEmpty", "net.minecraft.world.phys.shapes.VoxelShape,net.minecraft.world.phys.shapes.VoxelShape,net.minecraft.world.phys.shapes.BooleanOp");
        Object and = MiningDump.field("net.minecraft.world.phys.shapes.BooleanOp", "AND");
        List<Object> rows = new ArrayList<>();
        for (double origin : new double[] {0.0, -17.0, 29999983.0, -29999983.0}) {
            double[] cube = {origin, origin, origin, origin + 1.0, origin + 1.0, origin + 1.0};
            for (int axis = 0; axis < 3; axis++) for (int side = 0; side < 2; side++)
                for (double gap : new double[] {-0.5, -2E-7, -1.01E-7, -1E-7, -0.99E-7, -0.5E-7, -1E-9, 0, 1E-9, 0.5E-7, 1E-7, 2E-7}) {
                    double[] other = {origin + 0.2, origin + 0.2, origin + 0.2, origin + 0.8, origin + 0.8, origin + 0.8};
                    if (side == 0) { other[axis] = origin + 1.0 + gap; other[axis + 3] = other[axis] + 0.6; }
                    else { other[axis + 3] = origin - gap; other[axis] = other[axis + 3] - 0.6; }
                    boolean result = (Boolean)join.invoke(null, shape(cube), shape(other), and);
                    rows.add(Arrays.asList(cube.clone(), other, result));
                }
        }
        for (double size : new double[] {0, 1E-9, 0.99E-7, 1E-7, 1.01E-7, 2E-7}) {
            double[] cube = {0,0,0,1,1,1}, other = {0.5,0.5,0.5,0.5 + size,0.8,0.8};
            rows.add(Arrays.asList(cube, other, (Boolean)join.invoke(null, shape(cube), shape(other), and)));
        }
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, rows)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native placement intersection witnesses=" + rows.size());
    }
}
