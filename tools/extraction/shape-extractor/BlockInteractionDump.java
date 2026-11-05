import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures inherited vanilla use/placement contracts without invoking world mutations. Unknown subclasses remain explicit. Arguments: mappings-or-none, exploded-classpath, output.json. */
public final class BlockInteractionDump {
    static Class<?> cls(String name) throws Exception { return MiningDump.cls(name); }
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static Method owner(Class<?> runtime, Method base) throws Exception {
        for (Class<?> current = runtime; current != null; current = current.getSuperclass()) {
            try { return current.getDeclaredMethod(base.getName(), base.getParameterTypes()); }
            catch (NoSuchMethodException inherited) { }
        }
        throw new NoSuchMethodException(base.toString());
    }
    static boolean inherits(Object value, Method base) throws Exception { return owner(value.getClass(), base).getDeclaringClass() == base.getDeclaringClass(); }
    static boolean ordinaryFeatures(Object value, String owner) throws Exception {
        if (MiningDump.clsOrNull("net.minecraft.world.flag.FeatureFlags") == null) return true;
        Object features = method(owner, "requiredFeatures", "").invoke(value);
        return (Boolean) method("net.minecraft.world.flag.FeatureFlagSet", "isSubsetOf", "net.minecraft.world.flag.FeatureFlagSet")
            .invoke(features, MiningDump.field("net.minecraft.world.flag.FeatureFlags", "VANILLA_SET"));
    }
    static boolean edible(Object item) throws Exception {
        Method old = MiningDump.optional("net.minecraft.world.item.Item", "isEdible", "");
        if (old != null) return (Boolean) old.invoke(item);
        return component(item, "CONSUMABLE") != null;
    }
    static Object component(Object item, String name) throws Exception {
        Class<?> componentTypes = MiningDump.clsOrNull("net.minecraft.core.component.DataComponents");
        if (componentTypes == null) return null;
        if (PushDump.classN2O.isEmpty()) {
            try { componentTypes.getDeclaredField(name); }
            catch (NoSuchFieldException absent) { return null; }
        } else if (!PushDump.fieldN2O.getOrDefault("net.minecraft.core.component.DataComponents", Collections.emptyMap()).containsKey(name)) return null;
        Object key = MiningDump.field("net.minecraft.core.component.DataComponents", name);
        Object components = method("net.minecraft.world.item.Item", "components", "").invoke(item);
        String getter = MiningDump.clsOrNull("net.minecraft.core.component.DataComponentGetter") == null
            ? "net.minecraft.core.component.DataComponentMap" : "net.minecraft.core.component.DataComponentGetter";
        return method(getter, "get", "net.minecraft.core.component.DataComponentType").invoke(components, key);
    }
    public static void main(String[] args) throws Exception {
        PushDump.loader = BlockInteractionDump.class.getClassLoader();
        if (!args[0].equals("none")) PushDump.parse(Paths.get(args[0]));
        MiningDump.identifierClass = MiningDump.clsOrNull("net.minecraft.resources.Identifier");
        if (MiningDump.identifierClass == null) MiningDump.identifierClass = cls("net.minecraft.resources.ResourceLocation");
        Method detect = MiningDump.optional("net.minecraft.SharedConstants", "tryDetectVersion", "");
        if (detect != null) detect.invoke(null);
        method("net.minecraft.server.Bootstrap", "bootStrap", "").invoke(null);
        Object blocks = MiningDump.registry("BLOCK");
        MiningDump.bindTags(Paths.get(args[1]), blocks); MiningDump.initializeComponents();
        String block = "net.minecraft.world.level.block.Block", state = "net.minecraft.world.level.block.state.BlockState";
        String behavior = MiningDump.clsOrNull("net.minecraft.world.level.block.state.BlockBehaviour") == null ? block : "net.minecraft.world.level.block.state.BlockBehaviour";
        String stateBase = MiningDump.clsOrNull("net.minecraft.world.level.block.state.BlockBehaviour$BlockStateBase") == null ? state : "net.minecraft.world.level.block.state.BlockBehaviour$BlockStateBase";
        String level = "net.minecraft.world.level.Level", player = "net.minecraft.world.entity.player.Player", pos = "net.minecraft.core.BlockPos";
        String hit = "net.minecraft.world.phys.BlockHitResult", hand = "net.minecraft.world.InteractionHand", stack = "net.minecraft.world.item.ItemStack";
        String useContext = "net.minecraft.world.item.context.UseOnContext", placeContext = "net.minecraft.world.item.context.BlockPlaceContext";
        if (MiningDump.clsOrNull(placeContext) == null) {
            useContext = "net.minecraft.world.item.UseOnContext";
            placeContext = "net.minecraft.world.item.BlockPlaceContext";
        }
        Method use = MiningDump.optional(behavior, "useWithoutItem", state + "," + level + "," + pos + "," + player + "," + hit);
        boolean split = use != null;
        if (!split) use = method(behavior, "use", state + "," + level + "," + pos + "," + player + "," + hand + "," + hit);
        Method useItem = split ? method(behavior, "useItemOn", stack + "," + state + "," + level + "," + pos + "," + player + "," + hand + "," + hit) : null;
        Method replace = method(behavior, "canBeReplaced", state + "," + placeContext);
        Method replaceFlag = MiningDump.optional(stateBase, "canBeReplaced", "");
        Method material = replaceFlag == null ? method(stateBase, "getMaterial", "") : null;
        Method materialReplace = material == null ? null : method("net.minecraft.world.level.material.Material", "isReplaceable", "");
        Method placement = method(block, "getStateForPlacement", placeContext);
        Method survive = method(behavior, "canSurvive", state + ",net.minecraft.world.level.LevelReader," + pos);
        Method shape = method(behavior, "getShape", state + ",net.minecraft.world.level.BlockGetter," + pos + ",net.minecraft.world.phys.shapes.CollisionContext");
        Method collision = method(behavior, "getCollisionShape", state + ",net.minecraft.world.level.BlockGetter," + pos + ",net.minecraft.world.phys.shapes.CollisionContext");
        Method full = method(stateBase, "isCollisionShapeFullBlock", "net.minecraft.world.level.BlockGetter," + pos);
        Object emptyWorld = cls("net.minecraft.world.level.EmptyBlockGetter").getEnumConstants()[0];
        Object origin = cls(pos).getConstructor(int.class, int.class, int.class).newInstance(0, 0, 0);
        Method defaultState = method(block, "defaultBlockState", ""), getBlock = method(stateBase, "getBlock", "");
        Object states = MiningDump.field(block, "BLOCK_STATE_REGISTRY");
        Method stateId = method("net.minecraft.core.IdMapper", "getId", "java.lang.Object");
        Map<String, Object> stateRows = new TreeMap<>(), itemRows = new TreeMap<>();
        Map<Object, Boolean> simpleBlocks = new IdentityHashMap<>();
        for (Object value : (Iterable<?>) states) {
            Object nativeBlock = getBlock.invoke(value);
            boolean passive = inherits(nativeBlock, use) && (useItem == null || inherits(nativeBlock, useItem));
            boolean rawReplaceable = replaceFlag != null ? (Boolean) replaceFlag.invoke(value) : (Boolean) materialReplace.invoke(material.invoke(value));
            boolean neverReplace = inherits(nativeBlock, replace) && !rawReplaceable;
            int flags = (passive ? 1 : 0) | (neverReplace ? 2 : 0);
            stateRows.put(stateId.invoke(states, value).toString(), Arrays.asList(MiningDump.key(blocks, nativeBlock), flags));
            if (!simpleBlocks.containsKey(nativeBlock)) {
                boolean simple = inherits(nativeBlock, placement) && inherits(nativeBlock, survive) && inherits(nativeBlock, shape) && inherits(nativeBlock, collision)
                    && (Boolean) full.invoke(defaultState.invoke(nativeBlock), emptyWorld, origin) && ordinaryFeatures(nativeBlock, behavior);
                simpleBlocks.put(nativeBlock, simple);
            }
        }
        Class<?> blockItem = cls("net.minecraft.world.item.BlockItem");
        String baseItem = "net.minecraft.world.item.Item", baseBlockItem = "net.minecraft.world.item.BlockItem";
        Method itemUse = method(baseItem, "useOn", useContext), blockItemUse = method(baseBlockItem, "useOn", useContext);
        Method itemBlock = method(baseBlockItem, "getBlock", "");
        List<Method> placementSteps = Arrays.asList(method(baseBlockItem, "place", placeContext), method(baseBlockItem, "updatePlacementContext", placeContext),
            method(baseBlockItem, "getPlacementState", placeContext), method(baseBlockItem, "placeBlock", placeContext + "," + state), method(baseBlockItem, "canPlace", placeContext + "," + state));
        Object items = MiningDump.registry("ITEM");
        for (Object item : (Iterable<?>) items) {
            String kind = inherits(item, itemUse) && ordinaryFeatures(item, baseItem) && component(item, "BLOCK_TRANSFORMER") == null ? "pass" : "unknown";
            String placed = "";
            if (blockItem.isInstance(item)) {
                Object nativeBlock = itemBlock.invoke(item);
                placed = MiningDump.key(blocks, nativeBlock);
                boolean simple = Boolean.TRUE.equals(simpleBlocks.get(nativeBlock)) && inherits(item, blockItemUse) && ordinaryFeatures(item, baseItem) && !edible(item);
                for (Method step : placementSteps) simple &= inherits(item, step);
                if (simple) kind = "simple_cube";
            }
            itemRows.put(MiningDump.key(items, item), Arrays.asList(kind, placed));
        }
        Map<String, Object> output = new LinkedHashMap<>(); output.put("states", stateRows); output.put("items", itemRows);
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        String json = (String) gson.getClass().getMethod("toJson", Object.class).invoke(gson, output);
        Files.write(Paths.get(args[2]), json.getBytes(StandardCharsets.UTF_8));
        System.out.println("interaction states=" + stateRows.size() + " items=" + itemRows.size() + " simple block families=" + simpleBlocks.values().stream().filter(Boolean::booleanValue).count());
    }
}
