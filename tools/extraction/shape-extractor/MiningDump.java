import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.stream.Stream;

/**
 * Reads hardness, harvest requirements and default-stack mining behavior from an unmodified vanilla jar. Compile alongside PushDump, whose mapping resolver this program reuses. Arguments: mappings-or-none, exploded-classpath, output.json. Missing methods, states, tags or failed invocations abort the entire measurement. Runtime reflection belongs in this extractor, never in the generated library.
 */
public final class MiningDump {
    static Class<?> cls(String name) throws Exception { return PushDump.cls(name); }
    static Method method(String owner, String name, String args) throws Exception {
        return PushDump.method(owner, name, args);
    }
    static Method optional(String owner, String name, String args) throws Exception {
        try { return method(owner, name, args); }
        catch (ClassNotFoundException | NoSuchMethodException absent) { return null; }
    }
    static Object field(String owner, String name) throws Exception {
        return PushDump.field(owner, name).get(null);
    }
    static Class<?> identifierClass;
    static Object identifier(String value) throws Exception {
        String owner = identifierClass.getName();
        for (String named : new String[] {"net.minecraft.resources.Identifier", "net.minecraft.resources.ResourceLocation"}) {
            if (clsOrNull(named) != identifierClass) continue;
            for (String factory : new String[] {"parse", "tryParse"}) {
                Method m = optional(named, factory, "java.lang.String");
                if (m != null) return Objects.requireNonNull(m.invoke(null, value), value);
            }
        }
        return identifierClass.getConstructor(String.class).newInstance(value);
    }
    static Class<?> clsOrNull(String name) throws Exception {
        try { return cls(name); } catch (ClassNotFoundException absent) { return null; }
    }
    static Object registry(String name) throws Exception {
        String owner = clsOrNull("net.minecraft.core.registries.BuiltInRegistries") != null
            ? "net.minecraft.core.registries.BuiltInRegistries" : "net.minecraft.core.Registry";
        return field(owner, name);
    }
    static String key(Object registry, Object value) throws Exception {
        return method("net.minecraft.core.Registry", "getKey", "java.lang.Object").invoke(registry, value).toString();
    }
    static String quote(String text) {
        return "\"" + text.replace("\\", "\\\\").replace("\"", "\\\"") + "\"";
    }

    @SuppressWarnings("unchecked")
    static Map<String, Map<String, Object>> readTags(Path root) throws Exception {
        Map<String, Map<String, Object>> tags = new TreeMap<>();
        if (!Files.isDirectory(root)) return tags;
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Method parse = gson.getClass().getMethod("fromJson", Reader.class, Class.class);
        try (Stream<Path> paths = Files.walk(root)) {
            for (Path path : (Iterable<Path>) paths.filter(p -> p.toString().endsWith(".json"))::iterator) {
                String name = "minecraft:" + root.relativize(path).toString().replace(File.separatorChar, '/');
                name = name.substring(0, name.length() - 5);
                try (Reader reader = Files.newBufferedReader(path)) {
                    tags.put(name, (Map<String, Object>) parse.invoke(gson, reader, Map.class));
                }
            }
        }
        return tags;
    }

    @SuppressWarnings("unchecked")
    static Set<Object> resolveTag(String name, Map<String, Map<String, Object>> raw,
        Map<String, Set<Object>> resolved, Set<String> visiting, Map<String, Object> elements) {
        if (resolved.containsKey(name)) return resolved.get(name);
        if (!visiting.add(name)) throw new IllegalStateException("Cyclic vanilla tag " + name);
        Map<String, Object> document = raw.get(name);
        if (document == null) throw new IllegalStateException("Missing required vanilla tag " + name);
        Set<Object> values = new LinkedHashSet<>();
        for (Object entry : (List<Object>) document.get("values")) {
            boolean required = true;
            String id;
            if (entry instanceof String) id = (String) entry;
            else {
                Map<String, Object> object = (Map<String, Object>) entry;
                id = (String) object.get("id");
                required = !Boolean.FALSE.equals(object.get("required"));
            }
            boolean tag = id.startsWith("#");
            if (tag) id = id.substring(1);
            if (!id.contains(":")) id = "minecraft:" + id;
            if (tag) {
                if (raw.containsKey(id)) values.addAll(resolveTag(id, raw, resolved, visiting, elements));
                else if (required) throw new IllegalStateException("Missing required tag " + id + " in " + name);
            } else {
                Object element = elements.get(id);
                if (element != null) values.add(element);
                else if (required) throw new IllegalStateException("Missing required element " + id + " in " + name);
            }
        }
        visiting.remove(name);
        resolved.put(name, values);
        return values;
    }

    static void bindTags(Path classpath, Object blocks) throws Exception {
        Path root = classpath.resolve("data/minecraft/tags/block");
        if (!Files.isDirectory(root)) root = classpath.resolve("data/minecraft/tags/blocks");
        Map<String, Map<String, Object>> raw = readTags(root);
        if (raw.isEmpty()) {
            // Before tags, tool classes hold explicit effective-block/material sets.
            if (clsOrNull("net.minecraft.tags.BlockTags") != null)
                throw new IllegalStateException("No vanilla block tags at " + root);
            return;
        }
        Map<String, Object> elements = new HashMap<>();
        for (Object block : (Iterable<?>) blocks) elements.put(key(blocks, block), block);
        Map<String, Set<Object>> resolved = new TreeMap<>();
        for (String name : raw.keySet()) resolveTag(name, raw, resolved, new HashSet<>(), elements);

        Method tagCreate = optional("net.minecraft.tags.TagKey", "create",
            "net.minecraft.resources.ResourceKey," + (identifierClass == clsOrNull("net.minecraft.resources.Identifier")
                ? "net.minecraft.resources.Identifier" : "net.minecraft.resources.ResourceLocation"));
        if (tagCreate != null) {
            Object registryKey = method("net.minecraft.core.Registry", "key", "").invoke(blocks);
            Method holder = method("net.minecraft.world.level.block.Block", "builtInRegistryHolder", "");
            Map<Object, List<Object>> contents = new HashMap<>();
            for (Map.Entry<String, Set<Object>> tag : resolved.entrySet()) {
                List<Object> holders = new ArrayList<>();
                for (Object block : tag.getValue()) holders.add(holder.invoke(block));
                contents.put(tagCreate.invoke(null, registryKey, identifier(tag.getKey())), holders);
            }
            Method prepare = optional("net.minecraft.core.Registry", "prepareTagReload", "net.minecraft.tags.TagLoader$LoadResult");
            if (prepare != null) {
                Object load = cls("net.minecraft.tags.TagLoader$LoadResult")
                    .getDeclaredConstructor(cls("net.minecraft.resources.ResourceKey"), Map.class)
                    .newInstance(registryKey, contents);
                Object pending = prepare.invoke(blocks, load);
                method("net.minecraft.core.Registry$PendingTags", "apply", "").invoke(pending);
            } else {
                method("net.minecraft.core.Registry", "bindTags", "java.util.Map").invoke(blocks, contents);
            }
        } else {
            // Named tags use actual block values. Shears already read wool/leaves tags in 1.14.
            Map<Object, Object> tags = new HashMap<>();
            Method tagOf = optional("net.minecraft.tags.Tag", "fromSet", "java.util.Set");
            for (Map.Entry<String, Set<Object>> tag : resolved.entrySet()) {
                Object id = identifier(tag.getKey());
                Object value;
                if (tagOf != null) value = tagOf.invoke(null, tag.getValue());
                else {
                    Object entry = cls("net.minecraft.tags.Tag$ValuesEntry").getConstructor(Collection.class).newInstance(tag.getValue());
                    value = cls("net.minecraft.tags.Tag").getConstructor(identifierClass, Collection.class, boolean.class)
                        .newInstance(id, Collections.singletonList(entry), false);
                }
                tags.put(id, value);
            }
            if (clsOrNull("net.minecraft.tags.StaticTagHelper") != null) {
                Object helper = field("net.minecraft.tags.BlockTags", "HELPER");
                Object wrappers = PushDump.field("net.minecraft.tags.StaticTagHelper", "wrappers").get(helper);
                Method rebind = method("net.minecraft.tags.StaticTagHelper$Wrapper", "rebind", "java.util.function.Function");
                java.util.function.Function<Object, Object> lookup = tags::get;
                for (Object wrapper : (Iterable<?>) wrappers) rebind.invoke(wrapper, lookup);
            } else {
                java.util.function.Function<Object, Optional<Object>> lookup = id -> Optional.ofNullable(elements.get(id.toString()));
                Object collection = cls("net.minecraft.tags.TagCollection")
                    .getConstructor(java.util.function.Function.class, String.class, boolean.class, String.class)
                    .newInstance(lookup, "tags/blocks", false, "blocks");
                PushDump.field("net.minecraft.tags.TagCollection", "tags").set(collection, tags);
                method("net.minecraft.tags.BlockTags", "reset", "net.minecraft.tags.TagCollection").invoke(null, collection);
            }
        }
        System.out.println("bound vanilla block tags=" + resolved.size());
    }

    static void initializeComponents() throws Exception {
        Class<?> initializers = clsOrNull("net.minecraft.core.component.DataComponentInitializers");
        if (initializers == null) return;
        Method lookup = optional("net.minecraft.data.registries.VanillaRegistries", "createWorldLookup", "");
        if (lookup == null) lookup = method("net.minecraft.data.registries.VanillaRegistries", "createLookup", "");
        Object context = lookup.invoke(null);
        Object pending = method("net.minecraft.core.component.DataComponentInitializers", "build", "net.minecraft.core.HolderLookup$Provider")
            .invoke(field("net.minecraft.core.registries.BuiltInRegistries", "DATA_COMPONENT_INITIALIZERS"), context);
        for (Object components : (Iterable<?>) pending)
            method("net.minecraft.core.component.DataComponentInitializers$PendingComponents", "apply", "").invoke(components);
        System.out.println("initialized delayed default components");
    }

    public static void main(String[] args) throws Exception {
        PushDump.loader = MiningDump.class.getClassLoader();
        if (!args[0].equals("none")) PushDump.parse(Paths.get(args[0]));
        identifierClass = clsOrNull("net.minecraft.resources.Identifier");
        if (identifierClass == null) identifierClass = cls("net.minecraft.resources.ResourceLocation");
        Method detect = optional("net.minecraft.SharedConstants", "tryDetectVersion", "");
        if (detect != null) detect.invoke(null);
        method("net.minecraft.server.Bootstrap", "bootStrap", "").invoke(null);
        Object blocks = registry("BLOCK");
        Object items = registry("ITEM");
        bindTags(Paths.get(args[1]), blocks);
        initializeComponents();

        Object mapper = field("net.minecraft.world.level.block.Block", "BLOCK_STATE_REGISTRY");
        Method size = method("net.minecraft.core.IdMapper", "size", "");
        Method byId = method("net.minecraft.core.IdMapper", "byId", "int");
        int count = (Integer) size.invoke(mapper);
        Object[] states = new Object[count];
        Object getter = field("net.minecraft.world.level.EmptyBlockGetter", "INSTANCE");
        Object origin = field("net.minecraft.core.BlockPos", "ZERO");
        Method hardness = PushDump.stateMethod("getDestroySpeed", "net.minecraft.world.level.BlockGetter,net.minecraft.core.BlockPos");
        Method requiresTool = PushDump.stateMethod("requiresCorrectToolForDrops", "");
        Method material = requiresTool == null ? PushDump.stateMethod("getMaterial", "") : null;
        Method alwaysDestroyable = requiresTool == null ? method("net.minecraft.world.level.material.Material", "isAlwaysDestroyable", "") : null;
        Method getBlock = PushDump.stateMethod("getBlock", "");
        StringBuilder output = new StringBuilder("{\"states\":{\n");
        for (int id = 0; id < count; id++) {
            Object state = Objects.requireNonNull(byId.invoke(mapper, id), "state " + id);
            states[id] = state;
            float h = (Float) hardness.invoke(state, getter, origin);
            if (!Float.isFinite(h)) throw new IllegalStateException("Non-finite hardness at " + id);
            boolean required = requiresTool != null ? (Boolean) requiresTool.invoke(state)
                : !(Boolean) alwaysDestroyable.invoke(material.invoke(state));
            if (id != 0) output.append(',').append('\n');
            output.append(quote(Integer.toString(id))).append(":[")
                .append(quote(key(blocks, getBlock.invoke(state)))).append(',').append(Float.toString(h))
                .append(',').append(required).append(']');
        }
        output.append("\n},\"items\":{\n");
        Method speed = method("net.minecraft.world.item.ItemStack", "getDestroySpeed", "net.minecraft.world.level.block.state.BlockState");
        Method correct = optional("net.minecraft.world.item.ItemStack", "isCorrectToolForDrops", "net.minecraft.world.level.block.state.BlockState");
        if (correct == null) correct = method("net.minecraft.world.item.ItemStack", "canDestroySpecial", "net.minecraft.world.level.block.state.BlockState");
        Method defaultStack = optional("net.minecraft.world.item.Item", "getDefaultInstance", "");
        Constructor<?> stackConstructor = defaultStack == null
            ? cls("net.minecraft.world.item.ItemStack").getConstructor(cls("net.minecraft.world.level.ItemLike")) : null;
        int itemCount = 0;
        for (Object item : (Iterable<?>) items) {
            String name = key(items, item);
            Object stack = defaultStack != null ? defaultStack.invoke(item) : stackConstructor.newInstance(item);
            if (itemCount++ != 0) output.append(',').append('\n');
            output.append(quote(name)).append(":{");
            boolean first = true;
            for (int id = 0; id < count; id++) {
                float s = (Float) speed.invoke(stack, states[id]);
                boolean c = (Boolean) correct.invoke(stack, states[id]);
                if (!Float.isFinite(s) || s < 0) throw new IllegalStateException("Invalid speed for " + name + " at " + id);
                if (s == 1.0F && !c) continue;
                if (!first) output.append(',');
                first = false;
                output.append(quote(Integer.toString(id))).append(":[").append(Float.toString(s)).append(',').append(c).append(']');
            }
            output.append('}');
        }
        output.append("\n}}\n");
        Files.write(Paths.get(args[2]), output.toString().getBytes(StandardCharsets.UTF_8));
        System.out.println("mining states=" + count + " items=" + itemCount + " -> " + args[2]);
    }
}
