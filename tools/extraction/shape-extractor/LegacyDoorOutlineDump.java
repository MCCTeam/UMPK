import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Measures each native door metadata state against every decoded neighboring state, with the unused vertical neighbor as a negative control. Arguments: door-outline-symbols.json, metadata-aliases.json, output.json. */
public final class LegacyDoorOutlineDump {
    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        LegacyOutlineDump.initialize(args[0]);
        Object gson = LegacyOutlineDump.gson;
        Method fromJson = gson.getClass().getMethod("fromJson", Reader.class, Class.class);
        Map<String, Object> metadata;
        try (Reader reader = Files.newBufferedReader(Paths.get(args[1]))) {
            metadata = (Map<String, Object>) fromJson.invoke(gson, reader, Map.class);
        }
        Class<?> family = Class.forName((String) LegacyOutlineDump.bindings.get("door_class"));
        int upperBit = ((Number) LegacyOutlineDump.bindings.get("door_upper_bit")).intValue();
        Method blockId = LegacyMiningGeneric.sole(LegacyOutlineDump.blockRegistry.getClass(), int.class, 1, LegacyOutlineDump.block);
        Map<Integer, Object> nativeBlocks = new TreeMap<>(), states = new TreeMap<>();
        for (Object value : (Iterable<?>) LegacyOutlineDump.blockRegistry) nativeBlocks.put((Integer) blockId.invoke(LegacyOutlineDump.blockRegistry, value), value);
        for (String key : ((Map<String, Object>) metadata.get("canonical_states")).keySet()) {
            int id = Integer.parseInt(key);
            Object value = LegacyOutlineDump.fromMetadata.invoke(nativeBlocks.get(id >> 4), id & 15);
            if ((Integer) LegacyOutlineDump.id.invoke(LegacyOutlineDump.registry, value) != id) throw new IllegalStateException("Noncanonical metadata input: " + id);
            states.put(id, value);
        }
        Constructor<?> position = LegacyOutlineDump.positionType.getConstructor(int.class, int.class, int.class);
        Object above = position.newInstance(0, 1, 0), below = position.newInstance(0, -1, 0);
        Set<String> permittedReads = new HashSet<>(Arrays.asList(above.toString(), below.toString(), LegacyOutlineDump.origin.toString()));
        Method toJson = gson.getClass().getMethod("toJson", Object.class);
        Map<String, Integer> shapeIndices = new LinkedHashMap<>(); List<Object> shapes = new ArrayList<>();
        Map<Integer, Object> doors = new TreeMap<>();
        Map<String, Object> witnessRows = new LinkedHashMap<>(), witnessHits = new LinkedHashMap<>();
        List<double[]> segments = LegacyOutlineDump.segments();
        long contexts = 0;
        for (Map.Entry<Integer, Object> center : states.entrySet()) {
            Object block = LegacyOutlineDump.owner.invoke(center.getValue());
            if (!family.isInstance(block)) continue;
            Object selectedNeighbor = (center.getKey() & upperBit) == 0 ? above : below;
            Object unusedNeighbor = selectedNeighbor == above ? below : above;
            Map<Integer, Integer> byState = new TreeMap<>(); Set<Integer> witnessed = new HashSet<>();
            for (Map.Entry<Integer, Object> neighbor : states.entrySet()) {
                LegacyOutlineDump.blocks.clear(); LegacyOutlineDump.reads.clear();
                LegacyOutlineDump.blocks.put(LegacyOutlineDump.origin, center.getValue());
                LegacyOutlineDump.blocks.put(selectedNeighbor, neighbor.getValue());
                List<double[]> boxes = LegacyOutlineDump.geometry(block, center.getValue(), LegacyOutlineDump.origin);
                String identity = (String) toJson.invoke(gson, boxes);
                Integer index = shapeIndices.get(identity);
                if (index == null) {
                    index = shapes.size(); shapeIndices.put(identity, index); shapes.add(LegacyOutlineDump.shape(boxes));
                }
                byState.put(neighbor.getKey(), index);
                // Source selects only one half. Populate the unused neighbor too and reject any geometry dependence on it.
                LegacyOutlineDump.blocks.put(unusedNeighbor, neighbor.getValue());
                String control = (String) toJson.invoke(gson, LegacyOutlineDump.geometry(block, center.getValue(), LegacyOutlineDump.origin));
                if (!identity.equals(control)) throw new IllegalStateException("Door shape also depends on the unused neighbor at " + center.getKey() + ":" + neighbor.getKey());
                if (!permittedReads.containsAll(LegacyOutlineDump.reads)) throw new IllegalStateException("Unmodeled door neighbor reads: " + LegacyOutlineDump.reads);
                if (witnessed.add(index)) {
                    String key = center.getKey() + ":" + neighbor.getKey();
                    Map<String, Object> row = new LinkedHashMap<>();
                    row.put("block", LegacyOutlineDump.key.invoke(LegacyOutlineDump.blockRegistry, block).toString());
                    row.put("outline", shapes.get(index)); row.put("trace_selection", "nearest_from_start");
                    witnessRows.put(key, row);
                    List<Object> hits = new ArrayList<>();
                    for (double[] segment : segments) hits.add(LegacyOutlineDump.witness(LegacyOutlineDump.trace(block, center.getValue(), LegacyOutlineDump.origin, segment)));
                    witnessHits.put(key, hits);
                }
                contexts++;
            }
            Map<String, Object> row = new LinkedHashMap<>();
            row.put("block", LegacyOutlineDump.key.invoke(LegacyOutlineDump.blockRegistry, block).toString());
            row.put("offset", new int[] {0, selectedNeighbor == above ? 1 : -1, 0}); row.put("by_state", byState);
            doors.put(center.getKey(), row);
        }
        if (doors.isEmpty()) throw new IllegalStateException("Native door family has no registered states");
        Map<String, Object> output = new LinkedHashMap<>(), oracle = new LinkedHashMap<>();
        oracle.put("segments", segments); oracle.put("hits", witnessHits);
        output.put("shape_pool", shapes); output.put("doors", doors); output.put("contexts", contexts);
        output.put("witness_rows", witnessRows); output.put("oracle", oracle);
        Files.write(Paths.get(args[2]), ((String) toJson.invoke(gson, output)).getBytes(StandardCharsets.UTF_8));
        System.out.println("legacy door states=" + doors.size() + " native contexts=" + contexts + " shapes=" + shapes.size() + " native rays=" + segments.size() * witnessRows.size());
    }
}
