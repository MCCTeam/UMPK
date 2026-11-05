import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures metadata-era use inheritance and every eligible state's native collision box. Arguments: interaction-symbols.json, output.json, geometry-evidence.json. */
public final class LegacyInteractionDump {
    static Class<?> cls(String name) throws Exception {
        switch (name) {
            case "int": return int.class;
            case "float": return float.class;
            case "double": return double.class;
            case "boolean": return boolean.class;
            default: return Class.forName(name);
        }
    }
    static Method method(Map<String, Object> spec) throws Exception {
        List<?> names = (List<?>)spec.get("args");
        Class<?>[] types = new Class<?>[names.size()];
        for (int index = 0; index < types.length; index++) types[index] = cls((String)names.get(index));
        Method result = cls((String)spec.get("owner")).getDeclaredMethod((String)spec.get("name"), types);
        result.setAccessible(true);
        return result;
    }
    static boolean inherits(Object value, Method base) throws Exception {
        for (Class<?> current = value.getClass(); current != null; current = current.getSuperclass()) {
            try { return current.getDeclaredMethod(base.getName(), base.getParameterTypes()).getDeclaringClass() == base.getDeclaringClass(); }
            catch (NoSuchMethodException inherited) { }
        }
        throw new NoSuchMethodException(base.toString());
    }
    @SuppressWarnings("unchecked")
    static List<Method> methods(Object rows) throws Exception {
        List<Method> result = new ArrayList<>();
        for (Object row : (List<?>)rows) result.add(method((Map<String,Object>)row));
        return result;
    }
    static boolean inheritsAll(Object value, List<Method> methods) throws Exception {
        for (Method method : methods) if (!inherits(value, method)) return false;
        return true;
    }
    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        Object gson = cls("com.google.gson.Gson").getConstructor().newInstance();
        Map<String,Object> symbols;
        try (Reader reader = Files.newBufferedReader(Paths.get(args[0]))) {
            symbols = (Map<String,Object>)gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, reader, Map.class);
        }
        cls((String)symbols.get("bootstrap")).getMethod((String)symbols.get("boot")).invoke(null);
        Class<?> block = cls((String)symbols.get("block")), state = cls((String)symbols.get("state"));
        Map<String,List<?>> registries = (Map<String,List<?>>)symbols.get("registries");
        Object blocks = LegacyMiningGeneric.registry(registries.get("block")), items = LegacyMiningGeneric.registry(registries.get("item"));
        Object states = LegacyMiningGeneric.states(block, state);
        Method owner = LegacyMiningGeneric.sole(state, block, 0, null), id = LegacyMiningGeneric.sole(states.getClass(), int.class, 1, state);
        Method blockKey = LegacyMiningGeneric.key(blocks, ((Iterable<?>)blocks).iterator().next()), itemKey = LegacyMiningGeneric.key(items, ((Iterable<?>)items).iterator().next());
        Map<String,Object> binding = (Map<String,Object>)symbols.get("interaction_bindings");
        Method use = method((Map<String,Object>)binding.get("use")), replace = method((Map<String,Object>)binding.get("replace"));
        Method collision = method((Map<String,Object>)binding.get("collision")), itemUse = method((Map<String,Object>)binding.get("item_use"));
        Method itemBlockGetter = method((Map<String,Object>)binding.get("item_block_getter"));
        List<Method> blockSteps = methods(binding.get("block_steps")), itemSteps = methods(binding.get("item_steps"));
        Class<?> position = cls((String)binding.get("position")), itemBlock = cls((String)binding.get("item_block"));
        Object origin = position.getConstructor(int.class, int.class, int.class).newInstance(0, 0, 0);
        List<?> coordinateNames = (List<?>)binding.get("box_fields");
        Field[] coordinates = new Field[6];
        for (int index = 0; index < coordinates.length; index++) coordinates[index] = cls((String)binding.get("box_type")).getField((String)coordinateNames.get(index));
        for (Class<?> type : new Class<?>[] {block, state, cls((String)symbols.get("item")), itemBlock, cls((String)binding.get("box_type"))})
            System.out.println("native interaction class " + type.getName() + " from " + type.getProtectionDomain().getCodeSource().getLocation());

        Map<String,Object> stateRows = new TreeMap<>(), itemRows = new TreeMap<>(), geometry = new TreeMap<>();
        Map<Object,Boolean> simpleBlocks = new IdentityHashMap<>();
        int visited = 0, aliases = 0;
        for (Object value : (Iterable<?>)states) {
            visited++;
            Object nativeBlock = owner.invoke(value);
            String name = blockKey.invoke(blocks, nativeBlock).toString(), stateId = id.invoke(states, value).toString();
            int flags = (inherits(nativeBlock, use) ? 1 : 0) | (inherits(nativeBlock, replace) ? 2 : 0);
            Object previous = stateRows.putIfAbsent(stateId, Arrays.asList(name, flags));
            if (previous != null) {
                if (!previous.equals(Arrays.asList(name, flags))) throw new IllegalStateException("Conflicting native state-id alias " + stateId);
                aliases++;
            }
            boolean simple = inheritsAll(nativeBlock, blockSteps);
            if (simple) {
                Object[] parameters = new Object[collision.getParameterCount()];
                Class<?>[] types = collision.getParameterTypes();
                for (int index = 0; index < types.length; index++) {
                    if (types[index] == position) parameters[index] = origin;
                    else if (types[index] == state) parameters[index] = value;
                    else if (types[index] != cls((String)binding.get("world"))) {
                        // The inherited outline is a constant full box, so its accessor argument is not read.
                        if (!types[index].isInterface()) throw new IllegalStateException("Unexpected collision input " + types[index]);
                    }
                }
                Object box = collision.invoke(nativeBlock, parameters);
                List<Double> values = new ArrayList<>();
                if (box != null) for (Field coordinate : coordinates) {
                    double coordinateValue = coordinate.getDouble(box);
                    if (!Double.isFinite(coordinateValue)) throw new IllegalStateException("Non-finite native collision coordinate");
                    values.add(coordinateValue);
                }
                Object previousGeometry = geometry.putIfAbsent(stateId, Arrays.asList(name, values));
                if (previousGeometry != null && !previousGeometry.equals(Arrays.asList(name, values))) throw new IllegalStateException("State-id alias has different inherited collision geometry " + stateId);
                simple = values.equals(Arrays.asList(0.0, 0.0, 0.0, 1.0, 1.0, 1.0));
            }
            simpleBlocks.put(nativeBlock, simple && simpleBlocks.getOrDefault(nativeBlock, true));
        }
        boolean air = false;
        for (Object item : (Iterable<?>)items) {
            String name = itemKey.invoke(items, item).toString(), placed = "";
            air |= name.equals("minecraft:air");
            String kind = inherits(item, itemUse) ? "pass" : "unknown";
            if (itemBlock.isInstance(item)) {
                Object nativeBlock = itemBlockGetter.invoke(item);
                placed = blockKey.invoke(blocks, nativeBlock).toString();
                if (Boolean.TRUE.equals(simpleBlocks.get(nativeBlock)) && inheritsAll(item, itemSteps)) kind = "simple_cube";
            }
            if (itemRows.put(name, Arrays.asList(kind, placed)) != null) throw new IllegalStateException("Duplicate native item name");
        }
        if (!air) itemRows.put("minecraft:air", Arrays.asList("pass", ""));
        Map<String,Object> output = new LinkedHashMap<>(); output.put("states", stateRows); output.put("items", itemRows);
        Method json = gson.getClass().getMethod("toJson", Object.class);
        Files.write(Paths.get(args[1]), ((String)json.invoke(gson, output)).getBytes(StandardCharsets.UTF_8));
        Map<String,Object> evidence = new LinkedHashMap<>(); evidence.put("geometry", geometry); evidence.put("registered_states_visited", visited); evidence.put("duplicate_state_id_aliases", aliases);
        Files.write(Paths.get(args[2]), ((String)json.invoke(gson, evidence)).getBytes(StandardCharsets.UTF_8));
        System.out.println("metadata-era interaction states=" + stateRows.size() + " items=" + itemRows.size() + " measured collision boxes=" + geometry.size() + " registered states=" + visited + " equal wire-id aliases=" + aliases);
    }
}
