import java.io.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;

/**
 * Ground-truth block collision shape dumper: the jar-driven path this directory's
 * README describes, built.
 *
 * Runs against an UNMODIFIED obfuscated Mojang server jar. Every Minecraft type is
 * reached reflectively and every obfuscated name comes from the official proguard
 * server_mappings.txt shipped alongside the jar, so nothing here is guessed and no
 * remapped jar, ASM pass or compile-against-Mojang step is needed. It boots the
 * server registries, walks Block.BLOCK_STATE_REGISTRY, and asks each state for
 * getCollisionShape(EmptyBlockGetter.INSTANCE, BlockPos.ZERO).toAabbs().
 *
 * Output: JSON  { "<stateId>": [[minX,minY,minZ,maxX,maxY,maxZ], ...], ... }
 * in block-local 0..1 coordinates, one entry per block state id, in the same id
 * space as the server's own --reports blocks.json.
 *
 * Beware when comparing: VoxelShape.toAabbs() is ONE decomposition of a solid, and
 * a differently-decomposed list can describe the same solid (a top stair is
 * "top slab + bottom quarter" here and "full half + top half" in other datasets).
 * Compare occupied volume, not box lists.
 *
 * Usage: java ShapeDump &lt;server_mappings.txt&gt; &lt;out.json&gt;
 * See README.md in this directory for the classpath the jar needs.
 */
public final class ShapeDump {

    // named internal name ("net/minecraft/...") -> obf internal name
    static final Map<String, String> classN2O = new HashMap<>();
    // named class -> (named field -> obf field)
    static final Map<String, Map<String, String>> fieldN2O = new HashMap<>();
    // named class -> list of {namedMethod, namedArgTypes, obfMethod}
    static final Map<String, List<String[]>> methodsByClass = new HashMap<>();

    static void parse(Path mapFile) throws IOException {
        List<String> lines = Files.readAllLines(mapFile);
        // Pass 1: classes only (needed before descriptors can be resolved).
        for (String line : lines) {
            if (line.isEmpty() || line.startsWith("#") || line.startsWith(" ")) continue;
            if (!line.endsWith(":")) continue;
            String body = line.substring(0, line.length() - 1);
            int arrow = body.indexOf(" -> ");
            if (arrow < 0) continue;
            String named = body.substring(0, arrow).trim();
            String obf = body.substring(arrow + 4).trim();
            classN2O.put(named, obf);
        }
        // Pass 2: members.
        String current = null;
        for (String line : lines) {
            if (line.isEmpty() || line.startsWith("#")) continue;
            if (!line.startsWith(" ")) {
                if (!line.endsWith(":")) { current = null; continue; }
                String body = line.substring(0, line.length() - 1);
                int arrow = body.indexOf(" -> ");
                current = arrow < 0 ? null : body.substring(0, arrow).trim();
                continue;
            }
            if (current == null) continue;
            String t = line.trim();
            int arrow = t.lastIndexOf(" -> ");
            if (arrow < 0) continue;
            String left = t.substring(0, arrow).trim();
            String obf = t.substring(arrow + 4).trim();
            // strip "start:end:" line-number prefix on methods
            while (left.length() > 0 && Character.isDigit(left.charAt(0))) {
                int colon = left.indexOf(':');
                if (colon < 0) break;
                left = left.substring(colon + 1);
            }
            int space = left.indexOf(' ');
            if (space < 0) continue;
            String sig = left.substring(space + 1).trim();
            int paren = sig.indexOf('(');
            if (paren < 0) {
                fieldN2O.computeIfAbsent(current, k -> new HashMap<>()).put(sig, obf);
            } else {
                String name = sig.substring(0, paren);
                String args = sig.substring(paren + 1, sig.lastIndexOf(')'));
                methodsByClass.computeIfAbsent(current, k -> new ArrayList<>())
                        .add(new String[] { name, args, obf });
            }
        }
    }

    static ClassLoader loader;

    static Class<?> cls(String named) throws Exception {
        String obf = classN2O.getOrDefault(named, named);
        return Class.forName(obf, true, loader);
    }

    static Field field(String namedClass, String namedField) throws Exception {
        Map<String, String> m = fieldN2O.get(namedClass);
        String obf = m == null ? namedField : m.getOrDefault(namedField, namedField);
        Field f = cls(namedClass).getDeclaredField(obf);
        f.setAccessible(true);
        return f;
    }

    /** Resolve a method by named owner + named method name + named arg type list. */
    static Method method(String namedClass, String namedMethod, String namedArgs) throws Exception {
        List<String[]> list = methodsByClass.get(namedClass);
        String obf = namedMethod;
        if (list != null) {
            for (String[] e : list) {
                if (e[0].equals(namedMethod) && e[1].equals(namedArgs)) { obf = e[2]; break; }
            }
        }
        Class<?>[] params = argClasses(namedArgs);
        Class<?> owner = cls(namedClass);
        for (Method mm : owner.getDeclaredMethods()) {
            if (!mm.getName().equals(obf)) continue;
            if (!Arrays.equals(mm.getParameterTypes(), params)) continue;
            mm.setAccessible(true);
            return mm;
        }
        throw new NoSuchMethodException(namedClass + "." + namedMethod + "(" + namedArgs + ") obf=" + obf);
    }

    static Class<?>[] argClasses(String namedArgs) throws Exception {
        if (namedArgs.isEmpty()) return new Class<?>[0];
        String[] parts = namedArgs.split(",");
        Class<?>[] out = new Class<?>[parts.length];
        for (int i = 0; i < parts.length; i++) out[i] = typeClass(parts[i].trim());
        return out;
    }

    static Class<?> typeClass(String named) throws Exception {
        int arr = 0;
        while (named.endsWith("[]")) { arr++; named = named.substring(0, named.length() - 2); }
        Class<?> base;
        switch (named) {
            case "int": base = int.class; break;
            case "long": base = long.class; break;
            case "double": base = double.class; break;
            case "float": base = float.class; break;
            case "boolean": base = boolean.class; break;
            case "byte": base = byte.class; break;
            case "short": base = short.class; break;
            case "char": base = char.class; break;
            case "void": base = void.class; break;
            default: base = cls(named);
        }
        for (int i = 0; i < arr; i++) base = Array.newInstance(base, 0).getClass();
        return base;
    }

    public static void main(String[] args) throws Exception {
        loader = ShapeDump.class.getClassLoader();
        parse(Paths.get(args[0]));

        // Bootstrap the server registries.
        try {
            method("net.minecraft.SharedConstants", "tryDetectVersion", "").invoke(null);
        } catch (NoSuchMethodException pre117) {
            // 1.14/1.15 have no tryDetectVersion; bootStrap alone is enough there.
        }
        method("net.minecraft.server.Bootstrap", "bootStrap", "").invoke(null);

        Field reg = field("net.minecraft.world.level.block.Block", "BLOCK_STATE_REGISTRY");
        Object idMapper = reg.get(null);
        Method byId = method("net.minecraft.core.IdMapper", "byId", "int");
        Method sizeM = method("net.minecraft.core.IdMapper", "size", "");
        int size = (Integer) sizeM.invoke(idMapper);

        Object emptyGetter = field("net.minecraft.world.level.EmptyBlockGetter", "INSTANCE").get(null);
        Object origin = field("net.minecraft.core.BlockPos", "ZERO").get(null);

        // The owner of getCollisionShape moved between eras: BlockState (1.14-1.16),
        // BlockBehaviour$BlockStateBase (1.17+).
        String[] ownerCandidates = {
            "net.minecraft.world.level.block.state.BlockBehaviour$BlockStateBase",
            "net.minecraft.world.level.block.state.BlockState",
        };
        Method getCollision = null;
        for (String owner : ownerCandidates) {
            if (!classN2O.containsKey(owner)) continue;
            try {
                getCollision = method(owner, "getCollisionShape",
                        "net.minecraft.world.level.BlockGetter,net.minecraft.core.BlockPos");
                System.out.println("getCollisionShape owner=" + owner);
                break;
            } catch (NoSuchMethodException ignored) {
                // try the next era's owner
            }
        }
        if (getCollision == null) throw new IllegalStateException("no getCollisionShape owner resolved");
        Method toAabbs = method("net.minecraft.world.phys.shapes.VoxelShape", "toAabbs", "");

        Field minX = field("net.minecraft.world.phys.AABB", "minX");
        Field minY = field("net.minecraft.world.phys.AABB", "minY");
        Field minZ = field("net.minecraft.world.phys.AABB", "minZ");
        Field maxX = field("net.minecraft.world.phys.AABB", "maxX");
        Field maxY = field("net.minecraft.world.phys.AABB", "maxY");
        Field maxZ = field("net.minecraft.world.phys.AABB", "maxZ");

        StringBuilder sb = new StringBuilder("{\n");
        boolean first = true;
        int failures = 0;
        for (int id = 0; id < size; id++) {
            Object state = byId.invoke(idMapper, id);
            if (state == null) continue;
            List<?> boxes;
            try {
                Object shape = getCollision.invoke(state, emptyGetter, origin);
                boxes = (List<?>) toAabbs.invoke(shape);
            } catch (Throwable t) {
                failures++;
                continue;
            }
            if (!first) sb.append(",\n");
            first = false;
            sb.append('"').append(id).append("\":[");
            for (int i = 0; i < boxes.size(); i++) {
                Object b = boxes.get(i);
                if (i > 0) sb.append(',');
                sb.append('[')
                  .append(fmt(minX.getDouble(b))).append(',')
                  .append(fmt(minY.getDouble(b))).append(',')
                  .append(fmt(minZ.getDouble(b))).append(',')
                  .append(fmt(maxX.getDouble(b))).append(',')
                  .append(fmt(maxY.getDouble(b))).append(',')
                  .append(fmt(maxZ.getDouble(b))).append(']');
            }
            sb.append(']');
        }
        sb.append("\n}\n");
        Files.write(Paths.get(args[1]), sb.toString().getBytes("UTF-8"));
        System.out.println("states=" + size + " failures=" + failures + " -> " + args[1]);
    }

    static String fmt(double d) {
        double r = Math.round(d * 1000000.0) / 1000000.0;
        if (r == Math.rint(r) && !Double.isInfinite(r)) return String.valueOf((long) r) + ".0";
        return String.valueOf(r);
    }
}
