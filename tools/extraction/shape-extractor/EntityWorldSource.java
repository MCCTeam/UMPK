import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Generates an owned client-side Level adapter for native entity construction. Its constructor is never run. Unsupported queries throw instead of supplying guessed values. Arguments: mappings-or-none, EntityWorldAdapter.java. */
public final class EntityWorldSource {
    static String type(Class<?> value) { return value.getCanonicalName(); }
    static String parameters(Class<?>[] types) {
        List<String> values = new ArrayList<>();
        for (int index = 0; index < types.length; index++) values.add(type(types[index]) + " p" + index);
        return String.join(",", values);
    }
    static String zero(Class<?> value) {
        if (!value.isPrimitive()) return "(" + type(value) + ")null";
        if (value == boolean.class) return "false";
        if (value == char.class) return "'\\0'";
        if (value == long.class) return "0L";
        if (value == float.class) return "0.0F";
        if (value == double.class) return "0.0";
        return "(" + type(value) + ")0";
    }
    static String signature(Method method) { return method.getName() + Arrays.toString(method.getParameterTypes()); }
    static void remember(Map<String, Method> methods, Method method) {
        String key = signature(method);
        Method previous = methods.get(key);
        if (previous == null || previous.getDeclaringClass() == method.getDeclaringClass() && previous.getReturnType().isAssignableFrom(method.getReturnType())) methods.put(key, method);
    }
    static void player(Path output) throws Exception {
        Class<?> player = MiningDump.cls("net.minecraft.world.entity.player.Player");
        Constructor<?> constructor = Arrays.stream(player.getDeclaredConstructors()).filter(value -> !Modifier.isPrivate(value.getModifiers())).reduce((a,b) -> { throw new IllegalStateException("Ambiguous Player constructors"); }).orElseThrow();
        List<String> arguments = new ArrayList<>();
        for (Class<?> parameter : constructor.getParameterTypes()) {
            if (parameter == MiningDump.cls("net.minecraft.world.level.Level")) arguments.add("world");
            else if (parameter.getName().equals("com.mojang.authlib.GameProfile")) arguments.add("profile");
            else if (parameter == MiningDump.cls("net.minecraft.core.BlockPos")) arguments.add("new " + type(parameter) + "(0,64,0)");
            else if (parameter == MiningDump.clsOrNull("net.minecraft.world.entity.player.ProfilePublicKey")) arguments.add("null");
            else if (parameter.isPrimitive()) arguments.add(zero(parameter));
            else throw new IllegalStateException("Unsupported Player constructor parameter: " + parameter);
        }
        StringBuilder source = new StringBuilder("// Owned survival player; all geometry remains inherited from vanilla.\nimport com.mojang.authlib.GameProfile;\npublic final class EntityPlayerAdapter extends ")
            .append(type(player)).append(" {\n public EntityPlayerAdapter(").append(type(MiningDump.cls("net.minecraft.world.level.Level"))).append(" world,GameProfile profile) { super(")
            .append(String.join(",", arguments)).append("); }\n");
        Map<String, Method> methods = new TreeMap<>();
        for (Class<?> current = player; current != null; current = current.getSuperclass()) for (Method method : current.getDeclaredMethods()) remember(methods, method);
        for (Method method : player.getMethods()) remember(methods, method);
        Method spectator = MiningDump.method("net.minecraft.world.entity.Entity", "isSpectator", "");
        Method creative = MiningDump.optional("net.minecraft.world.entity.player.Player", "isCreative", "");
        Class<?> gameType = MiningDump.cls("net.minecraft.world.level.GameType");
        for (Method method : methods.values()) {
            if (!Modifier.isAbstract(method.getModifiers())) continue;
            source.append(" @Override public ").append(type(method.getReturnType())).append(' ').append(method.getName()).append('(').append(parameters(method.getParameterTypes())).append(") { ");
            if (method.getParameterCount() == 0 && method.getReturnType() == gameType) source.append("return ").append(type(gameType)).append('.').append(PushDump.field("net.minecraft.world.level.GameType", "SURVIVAL").getName()).append(';');
            else if (signature(method).equals(signature(spectator)) || creative != null && signature(method).equals(signature(creative))) source.append("return false;");
            else source.append("throw new UnsupportedOperationException(\"Unmeasured player operation: ").append(method.getName()).append("\");");
            source.append(" }\n");
        }
        Files.write(output, source.append("}\n").toString().getBytes(StandardCharsets.UTF_8));
    }
    public static void main(String[] args) throws Exception {
        EntityPlacementProbe.initialize(args[0]);
        Class<?> world = MiningDump.cls("net.minecraft.world.level.Level");
        List<Constructor<?>> constructors = new ArrayList<>();
        for (Constructor<?> constructor : world.getDeclaredConstructors()) if (!Modifier.isPrivate(constructor.getModifiers())) constructors.add(constructor);
        if (constructors.size() != 1) throw new IllegalStateException("Ambiguous Level constructors: " + constructors);
        List<String> arguments = new ArrayList<>();
        for (Class<?> parameter : constructors.get(0).getParameterTypes()) arguments.add(zero(parameter));
        Map<String, Method> inherited = new TreeMap<>();
        for (Class<?> current = world; current != null; current = current.getSuperclass())
            for (Method method : current.getDeclaredMethods()) remember(inherited, method);
        for (Method method : world.getMethods()) remember(inherited, method);
        Map<String, String> overrides = new HashMap<>();
        String[] owners = {"net.minecraft.world.level.Level", "net.minecraft.world.level.LevelAccessor", "net.minecraft.world.level.LevelReader", "net.minecraft.world.level.LevelHeightAccessor", "net.minecraft.world.level.BlockGetter", "net.minecraft.world.level.CommonLevelAccessor", "net.minecraft.world.level.EntityGetter"};
        for (String semantic : new String[] {"enabledFeatures", "getDifficulty", "getRandom", "getWorldBorder", "registryAccess", "dimensionType", "dimensionTypeRegistration", "dimension", "getLevelData", "getGameRules", "getMinY", "getHeight", "getSeaLevel", "getMinBuildHeight", "getMaxBuildHeight", "getNextEntityId", "getProfiler", "getSharedSpawnPos", "getGameTime", "getDayTime", "getScoreboard", "players", "environmentAttributes"}) {
            for (String owner : owners) {
                Method method = MiningDump.optional(owner, semantic, "");
                if (method == null) continue;
                Method effective = inherited.get(signature(method));
                if (effective != null && !Modifier.isFinal(effective.getModifiers())) overrides.put(signature(effective), semantic);
            }
        }
        for (String semantic : new String[] {"getBlockState", "getFluidState", "getBlockEntity", "getBiome"}) {
            for (String owner : owners) {
                Method method = MiningDump.optional(owner, semantic, "net.minecraft.core.BlockPos");
                if (method == null) continue;
                Method effective = inherited.get(signature(method));
                if (effective != null && !Modifier.isFinal(effective.getModifiers())) overrides.put(signature(effective), semantic);
            }
        }
        Method getEntity = MiningDump.method("net.minecraft.world.level.Level", "getEntity", "int");
        overrides.put(signature(getEntity), "getEntity");
        StringBuilder source = new StringBuilder("// Owned extraction adapter. Vanilla class bytes remain unchanged.\npublic final class EntityWorldAdapter extends ")
            .append(type(world)).append(" {\n private EntityWorldAdapter() throws Throwable { super(")
            .append(String.join(",", arguments)).append("); }\n");
        for (Method method : inherited.values()) {
            int flags = method.getModifiers();
            String semantic = overrides.get(signature(method));
            boolean uuidBridge = Arrays.equals(method.getParameterTypes(), new Class<?>[] {java.util.UUID.class}) && method.getReturnType().isAssignableFrom(MiningDump.cls("net.minecraft.world.entity.Entity"));
            if (!Modifier.isAbstract(flags) && semantic == null && !uuidBridge) continue;
            if (Modifier.isStatic(flags) || Modifier.isPrivate(flags)) continue;
            source.append(" @Override ").append(Modifier.isPublic(flags) ? "public " : "protected ")
                .append(type(uuidBridge ? MiningDump.cls("net.minecraft.world.entity.Entity") : method.getReturnType())).append(' ').append(method.getName()).append('(').append(parameters(method.getParameterTypes())).append(") { ");
            if (semantic == null) source.append("throw new UnsupportedOperationException(\"Unmeasured entity world query: ").append(method.getName()).append("\");");
            else if (semantic.equals("getNextEntityId")) source.append("return EntityPlacementProbe.nextEntityId();");
            else if (semantic.equals("getEntity")) source.append("return (").append(type(method.getReturnType())).append(")EntityPlacementProbe.entityById(p0);");
            else {
                Class<?> returns = method.getReturnType();
                String cast = returns == int.class ? "java.lang.Integer" : returns == boolean.class ? "java.lang.Boolean" : type(returns);
                source.append("return (").append(cast).append(")EntityPlacementProbe.worldValue(\"").append(semantic).append("\");");
            }
            source.append(" }\n");
        }
        source.append("}\n");
        Path output = Paths.get(args[1]);
        Files.write(output, source.toString().getBytes(StandardCharsets.UTF_8));
        player(output.resolveSibling("EntityPlayerAdapter.java"));
        System.out.println("Entity world adapter emitted: " + overrides);
    }
}
