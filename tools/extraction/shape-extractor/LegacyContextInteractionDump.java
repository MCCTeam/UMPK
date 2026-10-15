import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures 1.13–1.14.3 placement-context inheritance and actual native collision shapes. Arguments: symbols.json, output.json, geometry-evidence.json. */
public final class LegacyContextInteractionDump {
    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        Object gson = LegacyInteractionDump.cls("com.google.gson.Gson").getConstructor().newInstance();
        Map<String,Object> symbols;
        try (Reader reader = Files.newBufferedReader(Paths.get(args[0]))) {
            symbols = (Map<String,Object>)gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, reader, Map.class);
        }
        LegacyInteractionDump.cls((String)symbols.get("bootstrap")).getMethod((String)symbols.get("boot")).invoke(null);
        Class<?> block = LegacyInteractionDump.cls((String)symbols.get("block")), state = LegacyInteractionDump.cls((String)symbols.get("state"));
        Map<String,List<?>> registries = (Map<String,List<?>>)symbols.get("registries");
        Object blocks = LegacyMiningGeneric.registry(registries.get("block")), items = LegacyMiningGeneric.registry(registries.get("item"));
        Object states = LegacyMiningGeneric.states(block, state);
        Method owner = LegacyMiningGeneric.sole(state, block, 0, null), id = LegacyMiningGeneric.sole(states.getClass(), int.class, 1, state);
        Method blockKey = LegacyMiningGeneric.key(blocks, ((Iterable<?>)blocks).iterator().next()), itemKey = LegacyMiningGeneric.key(items, ((Iterable<?>)items).iterator().next());
        Map<String,Object> binding = (Map<String,Object>)symbols.get("interaction_bindings");
        Method use = LegacyInteractionDump.method((Map<String,Object>)binding.get("use")), replace = LegacyInteractionDump.method((Map<String,Object>)binding.get("replace"));
        Method collision = LegacyInteractionDump.method((Map<String,Object>)binding.get("collision")), itemUse = LegacyInteractionDump.method((Map<String,Object>)binding.get("item_use"));
        Method itemBlockGetter = LegacyInteractionDump.method((Map<String,Object>)binding.get("item_block_getter"));
        List<Method> blockSteps = LegacyInteractionDump.methods(binding.get("block_steps")), itemSteps = LegacyInteractionDump.methods(binding.get("item_steps"));
        Class<?> itemBlock = LegacyInteractionDump.cls((String)binding.get("item_block")), position = LegacyInteractionDump.cls((String)binding.get("position"));
        Class<?> shape = LegacyInteractionDump.cls((String)binding.get("shape")), context = binding.get("context") == null ? null : LegacyInteractionDump.cls((String)binding.get("context"));
        Object emptyContext = context == null ? null : context.getMethod((String)binding.get("context_empty")).invoke(null);
        Object origin = position.getConstructor(int.class, int.class, int.class).newInstance(0, 0, 0);
        Method material = state.getMethod((String)symbols.get("material"));
        Method replaceable = material.getReturnType().getMethod((String)binding.get("material_replaceable"));
        Method toBoxes = shape.getMethod((String)binding.get("to_boxes"));
        Method edible = binding.get("edible") == null ? null : LegacyInteractionDump.cls((String)symbols.get("item")).getMethod((String)binding.get("edible"));
        List<?> coordinateNames = (List<?>)binding.get("box_fields");
        Field[] coordinates = new Field[6];
        for (int index = 0; index < coordinates.length; index++) coordinates[index] = LegacyInteractionDump.cls((String)binding.get("box_type")).getField((String)coordinateNames.get(index));
        for (Class<?> type : new Class<?>[] {block, state, LegacyInteractionDump.cls((String)symbols.get("item")), itemBlock, shape, LegacyInteractionDump.cls((String)binding.get("box_type"))})
            System.out.println("native interaction class " + type.getName() + " from " + type.getProtectionDomain().getCodeSource().getLocation());

        Map<String,Object> stateRows = new TreeMap<>(), itemRows = new TreeMap<>(), geometry = new TreeMap<>();
        Map<Object,Boolean> simpleBlocks = new IdentityHashMap<>();
        int visited = 0, aliases = 0;
        for (Object value : (Iterable<?>)states) {
            visited++;
            Object nativeBlock = owner.invoke(value);
            String name = blockKey.invoke(blocks, nativeBlock).toString(), stateId = id.invoke(states, value).toString();
            int flags = (LegacyInteractionDump.inherits(nativeBlock, use) ? 1 : 0) | (LegacyInteractionDump.inherits(nativeBlock, replace) && !((Boolean)replaceable.invoke(material.invoke(value))) ? 2 : 0);
            Object previous = stateRows.putIfAbsent(stateId, Arrays.asList(name, flags));
            if (previous != null) {
                if (!previous.equals(Arrays.asList(name, flags))) throw new IllegalStateException("Conflicting native state-id alias " + stateId);
                aliases++;
            }
            boolean simple = LegacyInteractionDump.inheritsAll(nativeBlock, blockSteps);
            if (simple) {
                Object[] parameters = new Object[collision.getParameterCount()];
                parameters[0] = value;
                parameters[2] = origin;
                // The inherited constant outline and collision guard do not read the world accessor.
                if (context != null) parameters[3] = emptyContext;
                List<?> boxes = (List<?>)toBoxes.invoke(collision.invoke(nativeBlock, parameters));
                if (boxes.size() > 1) throw new IllegalStateException("Inherited constant collision produced multiple boxes");
                List<Double> values = new ArrayList<>();
                for (Object box : boxes) for (Field coordinate : coordinates) {
                    double coordinateValue = coordinate.getDouble(box);
                    if (!Double.isFinite(coordinateValue)) throw new IllegalStateException("Non-finite native collision coordinate");
                    values.add(coordinateValue);
                }
                Object previousGeometry = geometry.putIfAbsent(stateId, Arrays.asList(name, values));
                if (previousGeometry != null && !previousGeometry.equals(Arrays.asList(name, values))) throw new IllegalStateException("State-id alias has different native collision geometry " + stateId);
                simple = values.equals(Arrays.asList(0.0, 0.0, 0.0, 1.0, 1.0, 1.0));
            }
            simpleBlocks.put(nativeBlock, simple && simpleBlocks.getOrDefault(nativeBlock, true));
        }
        for (Object item : (Iterable<?>)items) {
            String name = itemKey.invoke(items, item).toString(), placed = "";
            String kind = LegacyInteractionDump.inherits(item, itemUse) ? "pass" : "unknown";
            if (itemBlock.isInstance(item)) {
                Object nativeBlock = itemBlockGetter.invoke(item);
                placed = blockKey.invoke(blocks, nativeBlock).toString();
                if (Boolean.TRUE.equals(simpleBlocks.get(nativeBlock)) && LegacyInteractionDump.inheritsAll(item, itemSteps) && (edible == null || !((Boolean)edible.invoke(item)))) kind = "simple_cube";
            }
            if (itemRows.put(name, Arrays.asList(kind, placed)) != null) throw new IllegalStateException("Duplicate native item name");
        }
        Map<String,Object> output = new LinkedHashMap<>(); output.put("states", stateRows); output.put("items", itemRows);
        Method json = gson.getClass().getMethod("toJson", Object.class);
        Files.write(Paths.get(args[1]), ((String)json.invoke(gson, output)).getBytes(StandardCharsets.UTF_8));
        Map<String,Object> evidence = new LinkedHashMap<>(); evidence.put("geometry", geometry); evidence.put("registered_states_visited", visited); evidence.put("duplicate_state_id_aliases", aliases);
        Files.write(Paths.get(args[2]), ((String)json.invoke(gson, evidence)).getBytes(StandardCharsets.UTF_8));
        System.out.println("placement-context interaction states=" + stateRows.size() + " items=" + itemRows.size() + " measured collision boxes=" + geometry.size());
    }
}
