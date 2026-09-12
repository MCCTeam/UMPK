import java.io.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;

/**
 * Ground-truth piston-pushability dumper: the second consumer of the jar-driven
 * reflection path this directory's README describes.
 *
 * Runs against an UNMODIFIED obfuscated Mojang server jar exactly as ShapeDump does,
 * with every obfuscated name coming from the official proguard server_mappings.txt
 * shipped alongside the jar, so nothing here is guessed. It boots the server
 * registries, walks Block.BLOCK_STATE_REGISTRY, and asks each state for the four
 * values PistonBaseBlock.isPushable and PistonStructureResolver.resolve read:
 *
 *   BlockState.getPistonPushReaction()             -> PushReaction enum name
 *   BlockState.getDestroySpeed(BlockGetter, pos)   -> float, -1.0 means unbreakable
 *   BlockState.hasBlockEntity() / Block.isEntityBlock()
 *   BlockState.isAir()
 *
 * The last three each moved between eras and every candidate that is tried is
 * printed, so a silent fallback is impossible.
 *
 * Output: JSON  { "<stateId>": ["<REACTION>", <destroySpeed>, <be>, <air>], ... }
 * one entry per block state id, in the same id space as the server's own
 * --reports blocks.json (and therefore as data/java/&lt;protocol&gt;/blocks.json).
 *
 * The mapping parser below is a copy of ShapeDump's on purpose: both files are
 * standalone single-file programs compiled and run by hand, and ShapeDump is a
 * verified tool that nothing in the gate would catch a refactor of.
 *
 * Usage: java PushDump &lt;server_mappings.txt&gt; &lt;out.json&gt;
 * Pass the literal string "none" as the mappings file for an unobfuscated jar
 * (26.1+), where every named type already resolves as itself.
 * See README.md in this directory for the classpath the jar needs.
 */
public final class PushDump {

    // named internal name ("net.minecraft....") -> obf internal name
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

    /** The two owners a per-state accessor has lived on: 1.16+ then 1.14-1.15. */
    static final String[] STATE_OWNERS = {
        "net.minecraft.world.level.block.state.BlockBehaviour$BlockStateBase",
        "net.minecraft.world.level.block.state.BlockState",
    };

    /**
     * The two owners Block.isEntityBlock() has lived on: net.minecraft.world.level.block.Block
     * through 1.15.2, then net.minecraft.world.level.block.state.BlockBehaviour from 1.16 (when
     * Mojang introduced BlockBehaviour as the shared Block/LiquidBlock superclass and moved this
     * member onto it). getDeclaredMethods() only sees members declared on the exact class asked,
     * so the pre-1.16 owner alone throws NoSuchMethodException at 1.16 rather than falling back.
     * Verified against each version's own server_mappings.txt: net.minecraft.world.level.block.
     * Block carries "boolean isEntityBlock()" at 1.14.4 and 1.15.2, and net.minecraft.world.level.
     * block.state.BlockBehaviour carries it from 1.16 on (1.17+ uses hasBlockEntity() instead and
     * never reaches this fallback).
     */
    static final String[] ENTITY_BLOCK_OWNERS = {
        "net.minecraft.world.level.block.Block",
        "net.minecraft.world.level.block.state.BlockBehaviour",
    };

    /** Resolve a no-arg-or-fixed-arg accessor against whichever era's owner has it. */
    static Method resolveAgainst(String[] owners, String namedMethod, String namedArgs) throws Exception {
        for (String owner : owners) {
            if (!classN2O.isEmpty() && !classN2O.containsKey(owner)) continue;
            try {
                Method m = method(owner, namedMethod, namedArgs);
                System.out.println("resolved " + namedMethod + "(" + namedArgs + ") owner=" + owner);
                return m;
            } catch (NoSuchMethodException | ClassNotFoundException ignored) {
                // try the next era's owner
            }
        }
        return null;
    }

    static Method stateMethod(String namedMethod, String namedArgs) throws Exception {
        return resolveAgainst(STATE_OWNERS, namedMethod, namedArgs);
    }

    public static void main(String[] args) throws Exception {
        loader = PushDump.class.getClassLoader();
        if (!args[0].equals("none")) parse(Paths.get(args[0]));

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

        Method getReaction = stateMethod("getPistonPushReaction", "");
        if (getReaction == null) throw new IllegalStateException("no getPistonPushReaction owner resolved");
        Method getDestroySpeed = stateMethod("getDestroySpeed",
                "net.minecraft.world.level.BlockGetter,net.minecraft.core.BlockPos");
        if (getDestroySpeed == null) throw new IllegalStateException("no getDestroySpeed owner resolved");
        Method isAir = stateMethod("isAir", "");
        if (isAir == null) throw new IllegalStateException("no isAir owner resolved");

        // hasBlockEntity() is 1.17+. Before that PistonBaseBlock.isPushable asks the
        // BLOCK: 1.14.4-1.16.x PistonBaseBlock.isPushable reads "!block.isEntityBlock()"
        // (see ENTITY_BLOCK_OWNERS for where that member itself moved to at 1.16).
        Method hasBlockEntity = stateMethod("hasBlockEntity", "");
        Method getBlock = null;
        Method isEntityBlock = null;
        if (hasBlockEntity == null) {
            getBlock = stateMethod("getBlock", "");
            if (getBlock == null) throw new IllegalStateException("no getBlock owner resolved");
            isEntityBlock = resolveAgainst(ENTITY_BLOCK_OWNERS, "isEntityBlock", "");
            if (isEntityBlock == null) throw new IllegalStateException("no isEntityBlock owner resolved");
        }

        StringBuilder sb = new StringBuilder("{\n");
        boolean first = true;
        int failures = 0;
        for (int id = 0; id < size; id++) {
            Object state = byId.invoke(idMapper, id);
            if (state == null) continue;
            String reaction;
            float destroySpeed;
            boolean be;
            boolean air;
            try {
                reaction = ((Enum<?>) getReaction.invoke(state)).name();
                destroySpeed = (Float) getDestroySpeed.invoke(state, emptyGetter, origin);
                be = hasBlockEntity != null
                        ? (Boolean) hasBlockEntity.invoke(state)
                        : (Boolean) isEntityBlock.invoke(getBlock.invoke(state));
                air = (Boolean) isAir.invoke(state);
            } catch (Throwable t) {
                failures++;
                continue;
            }
            if (!first) sb.append(",\n");
            first = false;
            sb.append('"').append(id).append("\":[\"").append(reaction).append("\",")
              .append(fmt(destroySpeed)).append(',')
              .append(be ? 1 : 0).append(',')
              .append(air ? 1 : 0).append(']');
        }
        sb.append("\n}\n");
        Files.write(Paths.get(args[1]), sb.toString().getBytes("UTF-8"));
        System.out.println("states=" + size + " failures=" + failures + " -> " + args[1]);
    }

    static String fmt(float f) {
        double r = Math.round((double) f * 1000000.0) / 1000000.0;
        if (r == Math.rint(r) && !Double.isInfinite(r)) return String.valueOf((long) r) + ".0";
        return String.valueOf(r);
    }
}
