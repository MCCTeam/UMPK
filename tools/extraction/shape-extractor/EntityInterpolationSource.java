import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;

/** Extends only the owned extraction adapters with controlled collision/tick inputs and an explicit legacy tick boundary. No native bytecode is changed. */
public final class EntityInterpolationSource {
    static String override(Method method, String body) {
        if (Modifier.isFinal(method.getModifiers())) throw new IllegalStateException("Native probe method became final: " + method);
        return " @Override public " + EntityWorldSource.type(method.getReturnType()) + " " + method.getName() + "(" + EntityWorldSource.parameters(method.getParameterTypes()) + ") { " + body + " }\n";
    }
    static void append(Path output, String methods) throws Exception {
        String original = new String(Files.readAllBytes(output),StandardCharsets.UTF_8);
        int end = original.lastIndexOf('}');
        Files.write(output,(original.substring(0,end) + methods + "}\n").getBytes(StandardCharsets.UTF_8));
    }
    public static void main(String[] args) throws Exception {
        EntityWorldSource.main(args);
        Path world = Paths.get(args[1]);
        String methods = "";
        Method collision = MiningDump.optional("net.minecraft.world.level.CollisionGetter","noCollision","net.minecraft.world.entity.Entity,net.minecraft.world.phys.AABB");
        if (collision != null) methods += override(collision,"try { return EntityInterpolationDump.collision(p1); } catch (ReflectiveOperationException error) { throw new IllegalStateException(error); } catch (Exception error) { throw new IllegalStateException(error); }");
        Method speed = MiningDump.optional("net.minecraft.world.level.Level","getRelativeTickSpeed","");
        if (speed != null) methods += override(speed,"return EntityInterpolationDump.relativeTickSpeed;");
        append(world,methods);
        Method velocity = MiningDump.method("net.minecraft.world.entity.Entity","setDeltaMovement","net.minecraft.world.phys.Vec3");
        append(world.resolveSibling("EntityPlayerAdapter.java"),override(velocity,"if (EntityInterpolationDump.armed) throw new EntityInterpolationDump.TickBoundary(); super." + velocity.getName() + "(p0);"));
    }
}
