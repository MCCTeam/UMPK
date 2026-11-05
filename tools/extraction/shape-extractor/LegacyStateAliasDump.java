import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Records native decoding of every metadata value for every registered legacy block. Native decoder failures are explicit rows, never invented aliases. Arguments: mining-symbols.json, aliases.json. */
public final class LegacyStateAliasDump {
    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Map<String, Object> symbols;
        try (Reader reader = Files.newBufferedReader(Paths.get(args[0]))) {
            symbols = (Map<String, Object>) gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, reader, Map.class);
        }
        Class<?> block = Class.forName((String) symbols.get("block")), state = Class.forName((String) symbols.get("state"));
        Class.forName((String) symbols.get("bootstrap")).getMethod((String) symbols.get("boot")).invoke(null);
        System.out.println("native alias block class " + block.getName() + " from " + block.getProtectionDomain().getCodeSource().getLocation());
        Map<String, List<?>> registries = (Map<String, List<?>>) symbols.get("registries");
        Object blocks = LegacyMiningGeneric.registry(registries.get("block")), states = LegacyMiningGeneric.states(block, state);
        Method blockId = LegacyMiningGeneric.sole(blocks.getClass(), int.class, 1, block);
        Method stateId = LegacyMiningGeneric.sole(states.getClass(), int.class, 1, state);
        Method decode = LegacyMiningGeneric.sole(block, state, 1, int.class), owner = LegacyMiningGeneric.sole(state, block, 0, null);
        Method key = LegacyMiningGeneric.key(blocks, ((Iterable<?>) blocks).iterator().next());
        Map<Integer, Integer> aliases = new TreeMap<>();
        Map<Integer, Object> failures = new TreeMap<>(), canonical = new TreeMap<>();
        int blockCount = 0;
        for (Object nativeBlock : (Iterable<?>) blocks) {
            int nativeId = (Integer) blockId.invoke(blocks, nativeBlock);
            String name = key.invoke(blocks, nativeBlock).toString();
            for (int metadata = 0; metadata < 16; metadata++) {
                int wire = (nativeId << 4) | metadata;
                Object value;
                try { value = decode.invoke(nativeBlock, metadata); }
                catch (InvocationTargetException error) {
                    Throwable cause = error.getCause();
                    failures.put(wire, Arrays.asList(name, cause.getClass().getName(), cause.getMessage()));
                    continue;
                }
                if (owner.invoke(value) != nativeBlock) throw new IllegalStateException("Metadata decoder changed block ownership: " + wire);
                int decoded = (Integer) stateId.invoke(states, value);
                if (decoded < 0 || decoded >> 4 != nativeId) throw new IllegalStateException("Decoded metadata is absent from the native state registry: " + wire + " -> " + decoded);
                if (aliases.put(wire, decoded) != null || failures.containsKey(wire)) throw new IllegalStateException("Duplicate wire metadata ID");
                canonical.put(decoded, name);
            }
            blockCount++;
        }
        if (aliases.size() + failures.size() != blockCount * 16) throw new IllegalStateException("Incomplete wire metadata coverage");
        Map<String, Object> output = new LinkedHashMap<>();
        output.put("block_count", blockCount); output.put("aliases", aliases);
        output.put("decode_failures", failures); output.put("canonical_states", canonical);
        String json = (String) gson.getClass().getMethod("toJson", Object.class).invoke(gson, output);
        Files.write(Paths.get(args[1]), json.getBytes(StandardCharsets.UTF_8));
        System.out.println("legacy metadata aliases=" + aliases.size() + " native decoder failures=" + failures.size() + " canonical states=" + canonical.size());
    }
}
