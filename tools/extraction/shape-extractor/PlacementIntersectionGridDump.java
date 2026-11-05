import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Additional native placement witnesses for dyadic shape quantization and thin boxes. Arguments match PlacementIntersectionDump. */
public final class PlacementIntersectionGridDump {
    static void add(List<Object> rows, Method join, Object and, double[] cube, double[] other) throws Exception {
        rows.add(Arrays.asList(cube.clone(), other.clone(), (Boolean)join.invoke(null, PlacementIntersectionDump.shape(cube), PlacementIntersectionDump.shape(other), and)));
    }
    public static void main(String[] args) throws Exception {
        PushDump.loader = PlacementIntersectionGridDump.class.getClassLoader();
        if (!args[0].equals("none")) PushDump.parse(Paths.get(args[0]));
        Method join = MiningDump.method("net.minecraft.world.phys.shapes.Shapes", "joinIsNotEmpty", "net.minecraft.world.phys.shapes.VoxelShape,net.minecraft.world.phys.shapes.VoxelShape,net.minecraft.world.phys.shapes.BooleanOp");
        Object and = MiningDump.field("net.minecraft.world.phys.shapes.BooleanOp", "AND");
        List<Object> rows = new ArrayList<>();
        double[] cube = {0,0,0,1,1,1};
        for (int axis = 0; axis < 3; axis++) for (double base : new double[] {0, 0.125, 0.25, 0.5, 0.875, 1})
            for (double offset : new double[] {-1.01E-7, -1E-7, -0.99E-7, -1E-9, 0, 1E-9, 0.99E-7, 1E-7, 1.01E-7})
                for (double width : new double[] {0, 1E-9, 0.99E-7, 1E-7, 1.01E-7, 0.125, 0.5}) {
                    double[] other = {0.25, 0.25, 0.25, 0.75, 0.75, 0.75};
                    other[axis] = base + offset; other[axis + 3] = other[axis] + width;
                    add(rows, join, and, cube, other);
                }
        for (double origin : new double[] {64, -17, 29999983, -29999983}) {
            cube = new double[] {origin,origin,origin,origin+1,origin+1,origin+1};
            for (int axis = 0; axis < 3; axis++) for (double size : new double[] {0, 1E-9, 0.99E-7, 1E-7, 1.01E-7, 2E-7}) {
                double[] other = {origin+0.5, origin+0.5, origin+0.5, origin+0.8, origin+0.8, origin+0.8};
                other[axis + 3] = other[axis] + size;
                add(rows, join, and, cube, other);
            }
        }
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, rows)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native placement grid witnesses=" + rows.size());
    }
}
