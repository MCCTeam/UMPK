import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures outline and interaction geometry at multiple coordinates, with empty and queried-item holding contexts. Arguments: mappings-or-none, output.json. Context or coordinate dependence that cannot be represented by a translated shape aborts extraction. */
public final class OutlineDump {
    static Method shape, interaction, boxes, offset;
    static Object getter;
    static boolean offsetNeedsWorld;
    static Field[] coordinates;
    static Field[] vector;

    static double[] displacement(Object state, Object position) throws Exception {
        Object value = offsetNeedsWorld ? offset.invoke(state, getter, position) : offset.invoke(state, position);
        double[] result = new double[3];
        for (int axis = 0; axis < 3; axis++) result[axis] = vector[axis].getDouble(value);
        return result;
    }

    static double[][] geometry(Object state, Object position, Object context, boolean override) throws Exception {
        Object value = override ? interaction.invoke(state, getter, position)
            : shape.getParameterCount() == 2 ? shape.invoke(state, getter, position) : shape.invoke(state, getter, position, context);
        List<?> source = (List<?>) boxes.invoke(value);
        double[][] result = new double[source.size()][6];
        for (int box = 0; box < source.size(); box++)
            for (int coordinate = 0; coordinate < 6; coordinate++) {
                double v = coordinates[coordinate].getDouble(source.get(box));
                if (!Double.isFinite(v)) throw new IllegalStateException("Non-finite outline coordinate");
                result[box][coordinate] = v;
            }
        return result;
    }

    static boolean equal(double[][] first, double[][] second, double[] translation) {
        if (first.length != second.length) return false;
        for (int box = 0; box < first.length; box++)
            for (int coordinate = 0; coordinate < 6; coordinate++)
                if (Math.abs(first[box][coordinate] + translation[coordinate % 3] - second[box][coordinate]) > 1.0E-12)
                    return false;
        return true;
    }

    static String normalize(Object state, Object[] positions, Object context, boolean override, double horizontal, double vertical) throws Exception {
        double[][] origin = geometry(state, positions[0], context, override);
        double[] initialOffset = displacement(state, positions[0]);
        boolean fixed = true, translated = true;
        for (int index = 1; index < positions.length; index++) {
            double[][] measured = geometry(state, positions[index], context, override);
            double[] current = displacement(state, positions[index]);
            for (int axis = 0; axis < 3; axis++) current[axis] -= initialOffset[axis];
            fixed &= equal(origin, measured, new double[3]);
            translated &= equal(origin, measured, current);
        }
        if (!fixed && !translated) throw new IllegalStateException("Outline has unsupported coordinate dependence: " + state);
        String kind = fixed ? "none" : initialOffset[1] == 0 ? "xz" : "xyz";
        if (!fixed)
            for (double[] box : origin)
                for (int coordinate = 0; coordinate < 6; coordinate++) box[coordinate] -= initialOffset[coordinate % 3];
        StringBuilder result = new StringBuilder("{\"boxes\":[");
        for (int box = 0; box < origin.length; box++) {
            if (box != 0) result.append(',');
            result.append(Arrays.toString(origin[box]));
        }
        return result.append("],\"offset\":").append(MiningDump.quote(kind))
            .append(",\"max_horizontal_offset\":").append(horizontal)
            .append(",\"max_vertical_offset\":").append(vertical).append('}').toString();
    }

    public static void main(String[] args) throws Exception {
        PushDump.loader = OutlineDump.class.getClassLoader();
        if (!args[0].equals("none")) PushDump.parse(Paths.get(args[0]));
        Method detect = MiningDump.optional("net.minecraft.SharedConstants", "tryDetectVersion", "");
        if (detect != null) detect.invoke(null);
        MiningDump.method("net.minecraft.server.Bootstrap", "bootStrap", "").invoke(null);
        Object mapper = MiningDump.field("net.minecraft.world.level.block.Block", "BLOCK_STATE_REGISTRY");
        getter = MiningDump.field("net.minecraft.world.level.EmptyBlockGetter", "INSTANCE");
        Constructor<?> position = MiningDump.cls("net.minecraft.core.BlockPos").getConstructor(int.class, int.class, int.class);
        Object[] positions = {position.newInstance(0, 0, 0), position.newInstance(17, 63, -31), position.newInstance(-30000000, -64, 29999999), position.newInstance(0, 100, 0)};
        shape = PushDump.stateMethod("getShape", "net.minecraft.world.level.BlockGetter,net.minecraft.core.BlockPos,net.minecraft.world.phys.shapes.CollisionContext");
        interaction = PushDump.stateMethod("getInteractionShape", "net.minecraft.world.level.BlockGetter,net.minecraft.core.BlockPos");
        if (shape == null || interaction == null) throw new IllegalStateException("Missing native shape accessor");
        Object empty = MiningDump.method("net.minecraft.world.phys.shapes.CollisionContext", "empty", "").invoke(null);
        Method holdingMethod = MiningDump.method("net.minecraft.world.phys.shapes.CollisionContext", "isHoldingItem", "net.minecraft.world.item.Item");
        Set<String> queried = new LinkedHashSet<>();
        Object items = MiningDump.registry("ITEM");
        Object holding = Proxy.newProxyInstance(PushDump.loader, new Class<?>[] {MiningDump.cls("net.minecraft.world.phys.shapes.CollisionContext")}, (proxy, method, arguments) -> {
            if (method.getName().equals(holdingMethod.getName()) && method.getParameterCount() == 1) {
                queried.add(MiningDump.key(items, arguments[0]));
                return true;
            }
            return method.invoke(empty, arguments);
        });
        boxes = MiningDump.method("net.minecraft.world.phys.shapes.VoxelShape", "toAabbs", "");
        coordinates = new Field[6];
        String[] names = {"minX", "minY", "minZ", "maxX", "maxY", "maxZ"};
        for (int index = 0; index < names.length; index++) coordinates[index] = PushDump.field("net.minecraft.world.phys.AABB", names[index]);
        vector = new Field[] {PushDump.field("net.minecraft.world.phys.Vec3", "x"), PushDump.field("net.minecraft.world.phys.Vec3", "y"), PushDump.field("net.minecraft.world.phys.Vec3", "z")};
        offset = PushDump.stateMethod("getOffset", "net.minecraft.core.BlockPos");
        if (offset == null) {
            offset = PushDump.stateMethod("getOffset", "net.minecraft.world.level.BlockGetter,net.minecraft.core.BlockPos");
            offsetNeedsWorld = true;
        }
        if (offset == null) throw new IllegalStateException("Missing native coordinate offset accessor");
        Method block = PushDump.stateMethod("getBlock", "");
        Method horizontal = MiningDump.optional("net.minecraft.world.level.block.state.BlockBehaviour", "getMaxHorizontalOffset", "");
        Method vertical = MiningDump.optional("net.minecraft.world.level.block.state.BlockBehaviour", "getMaxVerticalOffset", "");
        Method byId = MiningDump.method("net.minecraft.core.IdMapper", "byId", "int");
        int count = (Integer) MiningDump.method("net.minecraft.core.IdMapper", "size", "").invoke(mapper);
        Object blocks = MiningDump.registry("BLOCK");
        StringBuilder output = new StringBuilder("{\n");
        for (int id = 0; id < count; id++) {
            Object state = Objects.requireNonNull(byId.invoke(mapper, id));
            Object kind = block.invoke(state);
            double maxHorizontal = horizontal == null ? 0.25 : ((Number) horizontal.invoke(kind)).doubleValue();
            double maxVertical = vertical == null ? 0.2 : ((Number) vertical.invoke(kind)).doubleValue();
            queried.clear();
            String normal = normalize(state, positions, empty, false, maxHorizontal, maxVertical);
            String held = normalize(state, positions, holding, false, maxHorizontal, maxVertical);
            String override = normalize(state, positions, empty, true, maxHorizontal, maxVertical);
            if (!normal.equals(held) && queried.size() != 1) throw new IllegalStateException("Unsupported held-item shape selectors: " + queried);
            if (id != 0) output.append(",\n");
            output.append(MiningDump.quote(Integer.toString(id))).append(":{\"block\":").append(MiningDump.quote(MiningDump.key(blocks, kind)))
                .append(",\"outline\":").append(normal).append(",\"interaction\":").append(override);
            if (!normal.equals(held)) output.append(",\"held_item\":").append(MiningDump.quote(queried.iterator().next())).append(",\"held_outline\":").append(held);
            output.append('}');
        }
        output.append("\n}\n");
        Files.write(Paths.get(args[1]), output.toString().getBytes(StandardCharsets.UTF_8));
        System.out.println("contextual outline states=" + count);
    }
}
