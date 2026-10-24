import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Reads default Tool components through the vanilla registry-aware JSON codec, retaining tag selectors and rule order. Arguments: mappings-or-none, exploded-classpath, output.json. */
public final class ItemInteractionDump {
    public static void main(String[] args) throws Exception {
        PushDump.loader = ItemInteractionDump.class.getClassLoader();
        if (!args[0].equals("none")) PushDump.parse(Paths.get(args[0]));
        MiningDump.identifierClass = MiningDump.clsOrNull("net.minecraft.resources.Identifier");
        if (MiningDump.identifierClass == null)
            MiningDump.identifierClass = MiningDump.cls("net.minecraft.resources.ResourceLocation");
        Method detect = MiningDump.optional("net.minecraft.SharedConstants", "tryDetectVersion", "");
        if (detect != null) detect.invoke(null);
        MiningDump.method("net.minecraft.server.Bootstrap", "bootStrap", "").invoke(null);
        Object blocks = MiningDump.registry("BLOCK");
        MiningDump.bindTags(Paths.get(args[1]), blocks);
        MiningDump.initializeComponents();

        Object provider = MiningDump.method("net.minecraft.core.RegistryAccess", "fromRegistryOfRegistries", "net.minecraft.core.Registry")
            .invoke(null, MiningDump.field("net.minecraft.core.registries.BuiltInRegistries", "REGISTRY"));
        Object jsonOps = Class.forName("com.mojang.serialization.JsonOps").getField("INSTANCE").get(null);
        Object context = MiningDump.method("net.minecraft.resources.RegistryOps", "create", "com.mojang.serialization.DynamicOps,net.minecraft.core.HolderLookup$Provider")
            .invoke(null, jsonOps, provider);
        Object codec = MiningDump.field("net.minecraft.world.item.component.Tool", "CODEC");
        Method encode = Class.forName("com.mojang.serialization.Encoder").getMethod("encodeStart", Class.forName("com.mojang.serialization.DynamicOps"), Object.class);
        Method result = Class.forName("com.mojang.serialization.DataResult").getMethod("getOrThrow");
        Method components = MiningDump.method("net.minecraft.world.item.Item", "components", "");
        String getterOwner = MiningDump.clsOrNull("net.minecraft.core.component.DataComponentGetter") == null
            ? "net.minecraft.core.component.DataComponentMap" : "net.minecraft.core.component.DataComponentGetter";
        Method get = MiningDump.method(getterOwner, "get", "net.minecraft.core.component.DataComponentType");
        Object toolKey = MiningDump.field("net.minecraft.core.component.DataComponents", "TOOL");
        Object items = MiningDump.registry("ITEM");
        StringBuilder output = new StringBuilder("{\n");
        int count = 0;
        for (Object item : (Iterable<?>) items) {
            String name = MiningDump.key(items, item);
            Object tool = get.invoke(components.invoke(item), toolKey);
            if (count++ != 0) output.append(",\n");
            output.append(MiningDump.quote(name)).append(':');
            if (tool == null) output.append("null");
            else output.append(result.invoke(encode.invoke(codec, context, tool)).toString());
        }
        output.append("\n}\n");
        Files.write(Paths.get(args[2]), output.toString().getBytes(StandardCharsets.UTF_8));
        System.out.println("default item interactions=" + count);
    }
}
