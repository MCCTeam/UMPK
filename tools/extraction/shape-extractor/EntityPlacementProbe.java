import java.lang.reflect.*;
import java.lang.invoke.MethodHandles;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.stream.Stream;

/** Diagnostic native entity construction and placement geometry. Failed factories remain explicit; this output alone is not a canonical dataset. Arguments: mappings-or-none, exploded-classpath, output.json. */
public final class EntityPlacementProbe {
    interface RegistryLookup { Object get(Object key) throws Exception; }
    static Class<?> cls(String name) throws Exception { return MiningDump.cls(name); }
    static Method method(String owner, String name, String args) throws Exception { return MiningDump.method(owner, name, args); }
    static String named(Class<?> type) {
        for (Map.Entry<String, String> entry : PushDump.classN2O.entrySet()) if (entry.getValue().equals(type.getName())) return entry.getKey();
        return type.getName();
    }
    static Method implementation(Class<?> runtime, Method base) throws Exception {
        for (Class<?> current = runtime; current != null; current = current.getSuperclass()) {
            try { return current.getDeclaredMethod(base.getName(), base.getParameterTypes()); }
            catch (NoSuchMethodException inherited) { }
        }
        throw new NoSuchMethodException(base.toString());
    }
    static Object dimension(Object value, String name) throws Exception {
        String owner = "net.minecraft.world.entity.EntityDimensions";
        Method accessor = MiningDump.optional(owner, name, "");
        return accessor == null ? PushDump.field(owner, name).get(value) : accessor.invoke(value);
    }
    static String error(Throwable error) {
        while (error instanceof InvocationTargetException && error.getCause() != null) error = error.getCause();
        return error.toString();
    }
    static final Map<String, Object> worldValues = new HashMap<>();
    static final Map<Integer, Object> worldEntities = new HashMap<>();
    static int entityId;
    public static int nextEntityId() { return ++entityId; }
    public static Object entityById(int id) { return worldEntities.get(id); }
    public static Object worldValue(String name) {
        if (!worldValues.containsKey(name)) throw new UnsupportedOperationException("Unmeasured entity world query: " + name);
        return worldValues.get(name);
    }
    public static void initialize(String mappings) throws Exception {
        PushDump.loader = EntityPlacementProbe.class.getClassLoader();
        if (!mappings.equals("none")) PushDump.parse(Paths.get(mappings));
        MiningDump.identifierClass = MiningDump.clsOrNull("net.minecraft.resources.Identifier");
        if (MiningDump.identifierClass == null) MiningDump.identifierClass = cls("net.minecraft.resources.ResourceLocation");
        Method detect = MiningDump.optional("net.minecraft.SharedConstants", "tryDetectVersion", "");
        if (detect != null) detect.invoke(null);
        method("net.minecraft.server.Bootstrap", "bootStrap", "").invoke(null);
        MiningDump.initializeComponents();
    }
    static void registries() throws Exception {
        Class<?> access = MiningDump.clsOrNull("net.minecraft.core.RegistryAccess");
        if (access == null) return;
        Method vanilla = MiningDump.optional("net.minecraft.data.registries.VanillaRegistries", "createLookup", "");
        if (vanilla == null) vanilla = MiningDump.optional("net.minecraft.data.registries.VanillaRegistries", "createWorldLookup", "");
        if (vanilla == null) return;
        Object lookup = vanilla.invoke(null);
        Map<Object, Object> values = new LinkedHashMap<>();
        Object builtins = MiningDump.field("net.minecraft.core.registries.BuiltInRegistries", "REGISTRY");
        Method registryKey = method("net.minecraft.core.Registry", "key", "");
        for (Object registry : (Iterable<?>) builtins) values.put(registryKey.invoke(registry), registry);
        Object lifecycle = Class.forName("com.mojang.serialization.Lifecycle").getMethod("stable").invoke(null);
        Method getLookup = method("net.minecraft.core.HolderLookup$Provider", "lookupOrThrow", "net.minecraft.resources.ResourceKey");
        Method optionalLookup = method("net.minecraft.core.HolderLookup$Provider", "lookup", "net.minecraft.resources.ResourceKey");
        Method elements = method("net.minecraft.core.HolderLookup", "listElements", "");
        Method key = method("net.minecraft.core.Holder$Reference", "key", ""), value = method("net.minecraft.core.Holder$Reference", "value", "");
        boolean registrationInfo = MiningDump.clsOrNull("net.minecraft.core.RegistrationInfo") != null;
        Method register = method("net.minecraft.core.MappedRegistry", "register", "net.minecraft.resources.ResourceKey,java.lang.Object," + (registrationInfo ? "net.minecraft.core.RegistrationInfo" : "com.mojang.serialization.Lifecycle"));
        Object registration = registrationInfo ? MiningDump.field("net.minecraft.core.RegistrationInfo", "BUILT_IN") : lifecycle;
        Constructor<?> constructor = cls("net.minecraft.core.MappedRegistry").getConstructor(cls("net.minecraft.resources.ResourceKey"), Class.forName("com.mojang.serialization.Lifecycle"));
        RegistryLookup nativeRegistry = registryId -> {
                if (values.containsKey(registryId)) return values.get(registryId);
                if (!((Optional<?>)optionalLookup.invoke(lookup, registryId)).isPresent()) return null;
                Object registry = constructor.newInstance(registryId, lifecycle);
                Object source = getLookup.invoke(lookup, registryId);
                try (Stream<?> entries = (Stream<?>) elements.invoke(source)) {
                    Iterator<?> entryIterator = entries.iterator();
                    while (entryIterator.hasNext()) {
                        Object holder = entryIterator.next();
                        register.invoke(registry, key.invoke(holder), value.invoke(holder), registration);
                    }
                }
                method("net.minecraft.core.Registry", "freeze", "").invoke(registry);
                values.put(registryId, registry);
                System.out.println("materialized native entity-world registry: " + registryId);
                return registry;
        };
        Object nativeAccess = Proxy.newProxyInstance(PushDump.loader, new Class<?>[] {access}, (proxy, called, parameters) -> {
            if (called.getDeclaringClass() == Object.class) {
                if (called.getName().equals("toString")) return "OwnedNativeEntityRegistries";
                if (called.getName().equals("hashCode")) return System.identityHashCode(proxy);
                if (called.getName().equals("equals")) return proxy == parameters[0];
            }
            if (called.isDefault()) return MethodHandles.privateLookupIn(called.getDeclaringClass(), MethodHandles.lookup()).unreflectSpecial(called, called.getDeclaringClass()).bindTo(proxy).invokeWithArguments(parameters == null ? Collections.emptyList() : Arrays.asList(parameters));
            if (called.getParameterCount() == 1 && called.getParameterTypes()[0] == cls("net.minecraft.resources.ResourceKey") && called.getReturnType() == Optional.class)
                return Optional.ofNullable(nativeRegistry.get(parameters[0]));
            throw new UnsupportedOperationException("Unmeasured entity registry query: " + called);
        });
        worldValues.put("registryAccess", nativeAccess);
        String identifier = MiningDump.identifierClass == MiningDump.clsOrNull("net.minecraft.resources.Identifier") ? "net.minecraft.resources.Identifier" : "net.minecraft.resources.ResourceLocation";
        Method entryKey = method("net.minecraft.resources.ResourceKey", "create", "net.minecraft.resources.ResourceKey," + identifier);
        Method holder = method("net.minecraft.core.HolderGetter", "getOrThrow", "net.minecraft.resources.ResourceKey");
        Object dimensionKey = MiningDump.field("net.minecraft.core.registries.Registries", "DIMENSION_TYPE");
        Object dimensionHolder = holder.invoke(getLookup.invoke(lookup, dimensionKey), entryKey.invoke(null, dimensionKey, MiningDump.identifier("minecraft:overworld")));
        Object dimension = method("net.minecraft.core.Holder", "value", "").invoke(dimensionHolder);
        worldValues.put("dimensionTypeRegistration", dimensionHolder); worldValues.put("dimensionType", dimension);
        worldValues.put("getMinY", method("net.minecraft.world.level.dimension.DimensionType", "minY", "").invoke(dimension));
        worldValues.put("getHeight", method("net.minecraft.world.level.dimension.DimensionType", "height", "").invoke(dimension));
        worldValues.put("dimension", MiningDump.field("net.minecraft.world.level.Level", "OVERWORLD"));
        Object biomeKey = MiningDump.field("net.minecraft.core.registries.Registries", "BIOME");
        worldValues.put("getBiome", holder.invoke(getLookup.invoke(lookup, biomeKey), entryKey.invoke(null, biomeKey, MiningDump.identifier("minecraft:plains"))));
        if (MiningDump.clsOrNull("net.minecraft.world.attribute.EnvironmentAttributeSystem") != null) {
            Object builder = method("net.minecraft.world.attribute.EnvironmentAttributeSystem", "builder", "").invoke(null);
            Object attributes = method("net.minecraft.world.level.dimension.DimensionType", "attributes", "").invoke(dimension);
            method("net.minecraft.world.attribute.EnvironmentAttributeSystem$Builder", "addConstantLayer", "net.minecraft.world.attribute.EnvironmentAttributeMap").invoke(builder, attributes);
            worldValues.put("environmentAttributes", method("net.minecraft.world.attribute.EnvironmentAttributeSystem$Builder", "build", "").invoke(builder));
        }
    }
    static Object player(Object world) throws Exception {
        Class<?> profile = Class.forName("com.mojang.authlib.GameProfile");
        Object properties = null;
        Class<?> propertyMap = Class.forName("com.mojang.authlib.properties.PropertyMap");
        try { properties = propertyMap.getField("EMPTY").get(null); }
        catch (NoSuchFieldException absent) { properties = propertyMap.getConstructor().newInstance(); }
        List<Constructor<?>> candidates = new ArrayList<>();
        for (Constructor<?> constructor : profile.getConstructors()) if (constructor.getParameterCount() >= 2 && constructor.getParameterTypes()[0] == UUID.class && constructor.getParameterTypes()[1] == String.class) candidates.add(constructor);
        Constructor<?> constructor = candidates.stream().max(Comparator.comparingInt(Constructor::getParameterCount)).orElseThrow();
        Object identity;
        if (constructor.getParameterCount() == 2) identity = constructor.newInstance(new UUID(0L, 1L), "OracleProbe");
        else if (constructor.getParameterCount() == 3 && constructor.getParameterTypes()[2] == propertyMap) identity = constructor.newInstance(new UUID(0L, 1L), "OracleProbe", properties);
        else throw new IllegalStateException("Unsupported GameProfile constructor: " + constructor);
        return Class.forName("EntityPlayerAdapter").getConstructor(cls("net.minecraft.world.level.Level"), profile).newInstance(world, identity);
    }
    static Object special(String key, Object world, Object owner) throws Exception {
        Class<?> level = cls("net.minecraft.world.level.Level"), player = cls("net.minecraft.world.entity.player.Player");
        if (key.equals("minecraft:player")) return player(world);
        if (key.equals("minecraft:lightning_bolt")) return cls("net.minecraft.world.entity.global.LightningBolt").getConstructor(level, double.class, double.class, double.class, boolean.class).newInstance(world, 1.5, 64.0, 1.5, true);
        if (key.equals("minecraft:fishing_bobber")) {
            Class<?> hook = MiningDump.clsOrNull("net.minecraft.world.entity.fishing.FishingHook");
            if (hook == null) hook = cls("net.minecraft.world.entity.projectile.FishingHook");
            return hook.getConstructor(player, level, int.class, int.class).newInstance(owner, world, 0, 0);
        }
        throw new IllegalStateException("Native factory returns null for unbound special type " + key);
    }
    static Object stone() throws Exception {
        return method("net.minecraft.world.level.block.Block", "defaultBlockState", "").invoke(MiningDump.field("net.minecraft.world.level.block.Blocks", "STONE"));
    }
    static Object spawn(Object value, Object nativeType, Object world, String key) throws Exception {
        String entity = "net.minecraft.world.entity.Entity", packet = "net.minecraft.network.protocol.game.ClientboundAddEntityPacket";
        Method recreate = MiningDump.optional(entity, "recreateFromPacket", packet);
        if (recreate == null) {
            if (key.equals("minecraft:falling_block")) {
                Constructor<?> constructor = value.getClass().getDeclaredConstructor(cls("net.minecraft.world.level.Level"), double.class, double.class, double.class, cls("net.minecraft.world.level.block.state.BlockState"));
                constructor.setAccessible(true);
                value = constructor.newInstance(world, 1.5, 64.0, 1.5, stone());
            }
            return value;
        }
        Class<?>[] parameters = {int.class, UUID.class, double.class, double.class, double.class, float.class, float.class, cls("net.minecraft.world.entity.EntityType"), int.class, cls("net.minecraft.world.phys.Vec3"), double.class};
        Constructor<?> constructor;
        try { constructor = cls(packet).getConstructor(parameters); }
        catch (NoSuchMethodException beforeHeadRotation) { constructor = cls(packet).getConstructor(Arrays.copyOf(parameters, 10)); }
        int data = key.equals("minecraft:falling_block") ? (Integer)method("net.minecraft.world.level.block.Block", "getId", "net.minecraft.world.level.block.state.BlockState").invoke(null, stone()) : key.equals("minecraft:painting") || key.endsWith("item_frame") ? 3 : 1;
        Object[] values = {nextEntityId(), new UUID(0L, 2L), 1.5, 64.0, 1.5, 0.0F, 0.0F, nativeType, data, MiningDump.field("net.minecraft.world.phys.Vec3", "ZERO"), 0.0};
        Object message = constructor.newInstance(Arrays.copyOf(values, constructor.getParameterCount()));
        recreate.invoke(value, message);
        return value;
    }
    static Object world() throws Exception {
        Class<?> unsafeClass = Class.forName("sun.misc.Unsafe");
        Field singleton = unsafeClass.getDeclaredField("theUnsafe"); singleton.setAccessible(true);
        Object unsafe = singleton.get(null);
        Object world = unsafeClass.getMethod("allocateInstance", Class.class).invoke(unsafe, Class.forName("EntityWorldAdapter"));
        PushDump.field("net.minecraft.world.level.Level", "isClientSide").setBoolean(world, true);
        Class<?> normalDimension = MiningDump.clsOrNull("net.minecraft.world.level.dimension.NormalDimension");
        if (normalDimension != null) {
            Object dimension = unsafeClass.getMethod("allocateInstance", Class.class).invoke(unsafe, normalDimension);
            PushDump.field("net.minecraft.world.level.Level", "dimension").set(world, dimension);
        }
        Object random;
        Class<?> randomClass = MiningDump.clsOrNull("net.minecraft.util.RandomSource");
        if (randomClass == null) random = new Random(0L);
        else random = method("net.minecraft.util.RandomSource", "create", "long").invoke(null, 0L);
        PushDump.field("net.minecraft.world.level.Level", "random").set(world, random);
        worldValues.put("getRandom", random);
        worldValues.put("getWorldBorder", cls("net.minecraft.world.level.border.WorldBorder").getConstructor().newInstance());
        worldValues.put("getDifficulty", Enum.valueOf((Class)cls("net.minecraft.world.Difficulty"), "NORMAL"));
        if (normalDimension != null) {
            worldValues.put("getMinY", 0); worldValues.put("getHeight", 256);
            worldValues.put("getMinBuildHeight", 0); worldValues.put("getMaxBuildHeight", 256);
        }
        worldValues.put("getSeaLevel", 63);
        worldValues.put("getDayTime", 0L); worldValues.put("getGameTime", 0L);
        worldValues.put("getScoreboard", cls("net.minecraft.world.scores.Scoreboard").getConstructor().newInstance());
        worldValues.put("players", Collections.emptyList());
        Class<?> profiler = cls("net.minecraft.util.profiling.InactiveProfiler");
        List<Field> profilers = new ArrayList<>();
        for (Field field : profiler.getDeclaredFields()) if (Modifier.isStatic(field.getModifiers()) && field.getType() == profiler) profilers.add(field);
        if (profilers.size() != 1) throw new IllegalStateException("Ambiguous inactive profiler singleton");
        profilers.get(0).setAccessible(true);
        worldValues.put("getProfiler", profilers.get(0).get(null));
        worldValues.put("getSharedSpawnPos", MiningDump.field("net.minecraft.core.BlockPos", "ZERO"));
        if (MiningDump.clsOrNull("net.minecraft.world.flag.FeatureFlags") != null) worldValues.put("enabledFeatures", MiningDump.field("net.minecraft.world.flag.FeatureFlags", "VANILLA_SET"));
        registries();
        Object air = method("net.minecraft.world.level.block.Block", "defaultBlockState", "").invoke(MiningDump.field("net.minecraft.world.level.block.Blocks", "AIR"));
        worldValues.put("getBlockState", air);
        worldValues.put("getBlockEntity", null);
        Method fluid = PushDump.stateMethod("getFluidState", "");
        if (fluid != null) worldValues.put("getFluidState", fluid.invoke(air));
        return world;
    }
    public static void main(String[] args) throws Exception {
        initialize(args[0]);
        Object world = args.length > 3 ? world() : null;
        Object owner = world == null ? null : player(world);
        if (owner != null) {
            method("net.minecraft.world.entity.Entity", "setId", "int").invoke(owner, 1);
            worldEntities.put(1, owner);
        }
        String entity = "net.minecraft.world.entity.Entity", type = "net.minecraft.world.entity.EntityType", pose = "net.minecraft.world.entity.Pose";
        Object registry = MiningDump.registry("ENTITY_TYPE");
        Field factory = PushDump.field(type, "factory"), building = PushDump.field(entity, "blocksBuilding");
        Method create = method(type + "$EntityFactory", "create", type + ",net.minecraft.world.level.Level");
        Method dimensions = method(entity, "getDimensions", pose);
        Map<String, Method> methods = new LinkedHashMap<>();
        methods.put("getDimensions", dimensions);
        for (String name : new String[] {"getScale", "getAgeScale", "getBoundingBox"}) {
            Method optional = MiningDump.optional(entity, name, "");
            if (optional == null) optional = MiningDump.optional("net.minecraft.world.entity.LivingEntity", name, "");
            if (optional != null) methods.put(name, optional);
        }
        Method defaults = MiningDump.optional("net.minecraft.world.entity.LivingEntity", "getDefaultDimensions", pose);
        if (defaults != null) methods.put("getDefaultDimensions", defaults);
        Method bounds = MiningDump.optional(entity, "makeBoundingBox", "");
        if (bounds == null) bounds = MiningDump.optional(entity, "makeBoundingBox", "net.minecraft.world.phys.Vec3");
        if (bounds != null) methods.put("makeBoundingBox", bounds);
        Map<String, Object> rows = new TreeMap<>();
        int good = 0;
        for (Object nativeType : (Iterable<?>) registry) {
            String key = MiningDump.key(registry, nativeType);
            Map<String, Object> row = new LinkedHashMap<>();
            try {
                Object value = create.invoke(factory.get(nativeType), nativeType, world);
                if (value == null && world != null) value = special(key, world, owner);
                if (value == null) throw new IllegalStateException("Native factory returns null");
                row.put("constructor_blocks_building", building.getBoolean(value));
                if (world != null) value = spawn(value, nativeType, world, key);
                Class<?> runtime = value.getClass();
                row.put("class", named(runtime));
                row.put("origin", runtime.getProtectionDomain().getCodeSource().getLocation().toString());
                row.put("blocks_building", building.getBoolean(value));
                Map<String, String> owners = new LinkedHashMap<>();
                for (Map.Entry<String, Method> entry : methods.entrySet()) if (entry.getValue().getDeclaringClass().isInstance(value))
                    owners.put(entry.getKey(), named(implementation(runtime, entry.getValue()).getDeclaringClass()));
                row.put("method_owners", owners);
                Map<String, Object> shapes = new LinkedHashMap<>();
                for (Object nativePose : cls(pose).getEnumConstants()) {
                    Object size = dimensions.invoke(value, nativePose);
                    shapes.put(((Enum<?>)nativePose).name(), Arrays.asList(dimension(size, "width"), dimension(size, "height"), dimension(size, "fixed")));
                }
                row.put("dimensions", shapes);
                if (world != null && args.length > 4 && building.getBoolean(value)) row.put("geometry", EntityPlacementGeometry.measure(value));
                good++;
            } catch (ReflectiveOperationException | RuntimeException failure) {
                row.put("error", error(failure));
                Throwable cause = failure;
                while (cause instanceof InvocationTargetException && cause.getCause() != null) cause = cause.getCause();
                row.put("error_stack", Arrays.asList(cause.getStackTrace()).subList(0, Math.min(cause.getStackTrace().length, 5)).toString());
            }
            rows.put(key, row);
        }
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, rows)).getBytes(StandardCharsets.UTF_8));
        System.out.println("entity construction complete=" + good + "/" + rows.size());
    }
}
