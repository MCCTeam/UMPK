import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures 1.13–1.14.3 through source-resolved native methods. The optional legacy empty-world proxy follows the next era's native EmptyBlockGetter contract and rejects unknown methods. Arguments: outline-symbols.json, output.json. */
public final class LegacyFlatOutlineDump {
    static Class<?> cls(String name) throws Exception { return Class.forName(name); }
    static Field[] fields(String owner, List<?> names) throws Exception {
        Field[] fields = new Field[names.size()];
        for (int index = 0; index < fields.length; index++) fields[index] = cls(owner).getField((String) names.get(index));
        return fields;
    }

    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        Object gson = cls("com.google.gson.Gson").getConstructor().newInstance();
        Map<String, Object> symbols;
        try (Reader reader = Files.newBufferedReader(Paths.get(args[0]))) {
            symbols = (Map<String, Object>) gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, reader, Map.class);
        }
        Map<String, Object> names = (Map<String, Object>) symbols.get("outline_bindings");
        Class<?> block = cls((String) symbols.get("block")), state = cls((String) symbols.get("state"));
        Class<?> position = cls((String) names.get("position")), getter = cls((String) names.get("getter"));
        cls((String) symbols.get("bootstrap")).getMethod((String) symbols.get("boot")).invoke(null);
        Map<String, List<?>> bindings = (Map<String, List<?>>) symbols.get("registries");
        Object blocks = LegacyMiningGeneric.registry(bindings.get("block"));
        Object items = LegacyMiningGeneric.registry(bindings.get("item"));
        Object registry = LegacyMiningGeneric.states(block, state);
        Method owner = LegacyMiningGeneric.sole(state, block, 0, null);
        Method id = LegacyMiningGeneric.sole(registry.getClass(), int.class, 1, state);
        Method blockKey = LegacyMiningGeneric.key(blocks, ((Iterable<?>) blocks).iterator().next());
        Method itemKey = LegacyMiningGeneric.key(items, ((Iterable<?>) items).iterator().next());
        Object airState = null;
        for (Object value : (Iterable<?>) registry)
            if (blockKey.invoke(blocks, owner.invoke(value)).toString().equals("minecraft:air")) { airState = value; break; }
        if (airState == null) throw new IllegalStateException("No native air state");
        if (names.get("empty_getter") != null) {
            Object[] values = cls((String) names.get("empty_getter")).getEnumConstants();
            if (values.length != 1) throw new IllegalStateException("Ambiguous native empty getter");
            OutlineDump.getter = values[0];
        } else {
            Object air = airState;
            Object fluid = state.getMethod((String) names.get("fluid_method")).invoke(air);
            Class<?> fluidType = cls((String) names.get("fluid_type")), entityType = cls((String) names.get("block_entity_type"));
            Map<String, Number> defaults = (Map<String, Number>) names.get("getter_defaults");
            OutlineDump.getter = Proxy.newProxyInstance(getter.getClassLoader(), new Class<?>[] {getter}, (proxy, method, parameters) -> {
                if (method.getParameterCount() == 1 && method.getParameterTypes()[0] == position) {
                    if (method.getReturnType() == state) return air;
                    if (method.getReturnType() == fluidType) return fluid;
                    if (method.getReturnType() == entityType) return null;
                }
                if (method.getParameterCount() == 0 && defaults.containsKey(method.getName())) return defaults.get(method.getName()).intValue();
                throw new UnsupportedOperationException("Unmodeled empty-world method: " + method);
            });
        }
        Class<?> context = names.get("context") == null ? null : cls((String) names.get("context"));
        Object empty = context == null ? null : context.getMethod((String) names.get("context_empty")).invoke(null);
        Set<String> queried = new LinkedHashSet<>();
        Object holding = context == null ? null : Proxy.newProxyInstance(context.getClassLoader(), new Class<?>[] {context}, (proxy, method, parameters) -> {
            if (method.getName().equals(names.get("context_holding")) && method.getParameterCount() == 1) {
                queried.add(itemKey.invoke(items, parameters[0]).toString());
                return true;
            }
            return method.invoke(empty, parameters);
        });
        OutlineDump.shape = context == null ? state.getMethod((String) names.get("outline"), getter, position)
            : state.getMethod((String) names.get("outline"), getter, position, context);
        OutlineDump.interaction = state.getMethod((String) names.get("interaction"), getter, position);
        OutlineDump.offset = state.getMethod((String) names.get("offset"), getter, position);
        OutlineDump.offsetNeedsWorld = true;
        OutlineDump.boxes = cls((String) names.get("shape")).getMethod((String) names.get("to_boxes"));
        OutlineDump.coordinates = fields((String) names.get("box_type"), (List<?>) names.get("box_fields"));
        OutlineDump.vector = fields((String) names.get("vector_type"), (List<?>) names.get("vector_fields"));
        Constructor<?> constructor = position.getConstructor(int.class, int.class, int.class);
        Object[] positions = {constructor.newInstance(0, 0, 0), constructor.newInstance(17, 63, -31), constructor.newInstance(-30000000, -64, 29999999), constructor.newInstance(0, 100, 0)};
        TreeMap<Integer, String> rows = new TreeMap<>();
        for (Object value : (Iterable<?>) registry) {
            int stateId = (Integer) id.invoke(registry, value);
            queried.clear();
            String normal = OutlineDump.normalize(value, positions, empty, false, 0.25, 0.2);
            String held = OutlineDump.normalize(value, positions, holding, false, 0.25, 0.2);
            String override = OutlineDump.normalize(value, positions, empty, true, 0.25, 0.2);
            if (!normal.equals(held) && queried.size() != 1) throw new IllegalStateException("Ambiguous held-item shape selector");
            String row = "{\"block\":" + MiningDump.quote(blockKey.invoke(blocks, owner.invoke(value)).toString())
                + ",\"outline\":" + normal + ",\"interaction\":" + override;
            if (!normal.equals(held)) row += ",\"held_item\":" + MiningDump.quote(queried.iterator().next()) + ",\"held_outline\":" + held;
            if (rows.put(stateId, row + '}') != null) throw new IllegalStateException("Duplicate native state ID");
        }
        StringBuilder output = new StringBuilder("{\n");
        boolean first = true;
        for (Map.Entry<Integer, String> row : rows.entrySet()) {
            if (!first) output.append(",\n");
            first = false;
            output.append(MiningDump.quote(row.getKey().toString())).append(':').append(row.getValue());
        }
        output.append("\n}\n");
        Files.write(Paths.get(args[1]), output.toString().getBytes(StandardCharsets.UTF_8));
        System.out.println("legacy flattened outline states=" + rows.size());
    }
}
