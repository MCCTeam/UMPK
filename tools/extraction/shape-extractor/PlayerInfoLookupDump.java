import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Invokes unchanged native spectator lookups with owned singleton, connection, player and info fields. Constructors and entity/world ticks are not executed. */
public final class PlayerInfoLookupDump {
    static final String ENTITY = "net.minecraft.world.entity.Entity", PLAYER = "net.minecraft.world.entity.player.Player";
    static final String REMOTE = "net.minecraft.client.player.RemotePlayer", ABSTRACT = "net.minecraft.client.player.AbstractClientPlayer";
    static final String MINECRAFT = "net.minecraft.client.Minecraft", LISTENER = "net.minecraft.client.multiplayer.ClientPacketListener";
    static final String LOCAL = "net.minecraft.client.player.LocalPlayer", INFO = "net.minecraft.client.multiplayer.PlayerInfo", MODE = "net.minecraft.world.level.GameType";
    static Object unsafe;
    static Method allocate;
    static UUID id = UUID.fromString("0b13cdb1-d62a-3dd4-a677-595a0ac87822");
    static Object profile, survival, spectator;
    static Field cached, mode;
    static Object object(String name) throws Exception { return allocate.invoke(unsafe, MiningDump.cls(name)); }
    static Object actor() throws Exception {
        Object actor = object(REMOTE);
        PushDump.field(ENTITY, "uuid").set(actor, id);
        int count = 0;
        for (Class<?> type = actor.getClass(); type != null; type = type.getSuperclass()) {
            for (Field field : type.getDeclaredFields()) {
                if (!Modifier.isStatic(field.getModifiers()) && field.getType() == profile.getClass()) {
                    field.setAccessible(true); field.set(actor, profile); count++;
                }
            }
        }
        if (count != 1) throw new IllegalStateException("Ambiguous native player profile fields: " + count);
        return actor;
    }
    static Object info(Object gameMode) throws Exception { Object info = object(INFO); mode.set(info, gameMode); return info; }
    static String tag(Object value, Object first, Object second, Object third) {
        if (value == null) return "null";
        if (value == first) return "first";
        if (value == second) return "second";
        if (value == third) return "third";
        throw new IllegalStateException("Unexpected cached native info object");
    }
    static void observe(List<Object> rows, String label, Object actor, Method query, Object first, Object second, Object third) throws Exception {
        Map<String,Object> row = new LinkedHashMap<>();
        row.put("operation", label); row.put("before_cache", tag(cached.get(actor), first, second, third));
        row.put("spectator", query.invoke(actor)); row.put("after_cache", tag(cached.get(actor), first, second, third)); rows.add(row);
    }
    public static void main(String[] args) throws Exception {
        EntityPlacementProbe.initialize(args[0]);
        Class<?> unsafeClass = Class.forName("sun.misc.Unsafe"); Field singleton = unsafeClass.getDeclaredField("theUnsafe"); singleton.setAccessible(true); unsafe = singleton.get(null);
        allocate = unsafeClass.getMethod("allocateInstance", Class.class);
        Object minecraft = object(MINECRAFT), listener = object(LISTENER), local = object(LOCAL);
        PushDump.field(MINECRAFT, "instance").set(null, minecraft);
        PushDump.field(LOCAL, "connection").set(local, listener);
        PushDump.field(MINECRAFT, "player").set(minecraft, local);
        if (MiningDump.method(MINECRAFT, "getConnection", "").invoke(minecraft) != listener) throw new IllegalStateException("Native connection lookup did not use the owned listener");
        Map<UUID,Object> entries = new HashMap<>(); PushDump.field(LISTENER, "playerInfoMap").set(listener, entries);
        profile = Class.forName("com.mojang.authlib.GameProfile").getConstructor(UUID.class, String.class).newInstance(id, "GeometryPeer");
        survival = MiningDump.field(MODE, "SURVIVAL"); spectator = MiningDump.field(MODE, "SPECTATOR");
        cached = PushDump.field(ABSTRACT, "playerInfo"); mode = PushDump.field(INFO, "gameMode");
        Method query = MiningDump.method(ENTITY, "isSpectator", ""), lookup = MiningDump.method(LISTENER, "getPlayerInfo", "java.util.UUID");
        Object actor = actor(), first = info(survival), second = info(spectator), third = info(survival);
        Map<String,String> owners = new LinkedHashMap<>();
        owners.put("spectator", EntityPlacementProbe.named(EntityPlacementProbe.implementation(actor.getClass(), query).getDeclaringClass()));
        owners.put("info_lookup", EntityPlacementProbe.named(EntityPlacementProbe.implementation(listener.getClass(), lookup).getDeclaringClass()));
        Method cachedLookup = MiningDump.method(ABSTRACT, "getPlayerInfo", "");
        owners.put("cached_lookup", EntityPlacementProbe.named(EntityPlacementProbe.implementation(actor.getClass(), cachedLookup).getDeclaringClass()));
        List<Object> rows = new ArrayList<>();
        observe(rows, "missing", actor, query, first, second, third);
        entries.put(id, first); if (lookup.invoke(listener, id) != first) throw new IllegalStateException("Native lookup did not return the supplied info");
        observe(rows, "add_survival", actor, query, first, second, third);
        entries.put(id, second); observe(rows, "replace_with_spectator", actor, query, first, second, third);
        entries.remove(id); observe(rows, "remove_current", actor, query, first, second, third);
        mode.set(first, spectator); observe(rows, "mutate_detached_first", actor, query, first, second, third);
        entries.put(id, third); observe(rows, "add_new_survival", actor, query, first, second, third);
        actor = actor(); observe(rows, "fresh_actor", actor, query, first, second, third);
        mode.set(third, spectator); observe(rows, "mutate_current_to_spectator", actor, query, first, second, third);
        entries.clear(); actor = actor(); observe(rows, "fresh_actor_missing", actor, query, first, second, third);
        entries.put(id, second); observe(rows, "retry_missing_with_spectator", actor, query, first, second, third);
        Map<String,Object> result = new LinkedHashMap<>(); result.put("actor", REMOTE); result.put("method_owners", owners); result.put("queries", rows);
        result.put("scope", "Unchanged native predicates, cached lookup and listener map lookup. Owned objects are allocated without constructors; only UUID/profile, singleton/connection, map, cached-info and game-mode fields supply inputs. No player/entity/world ticks or networking.");
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Files.write(Paths.get(args[2]), ((String)gson.getClass().getMethod("toJson", Object.class).invoke(gson, result)).getBytes(StandardCharsets.UTF_8));
        System.out.println("native info queries=" + rows.size());
    }
}
