import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Generates an owned world adapter for legacy native geometry measurements. The constructor is never run; geometry reads use the explicit state map, and unsupported abstract world operations fail. Arguments: mining-symbols.json, output/LegacyWorldAdapter.java. */
public final class LegacyWorldSource {
    static String type(Class<?> value) { return value.getCanonicalName(); }
    static String parameters(Class<?>[] types) {
        List<String> parameters = new ArrayList<>();
        for (int index = 0; index < types.length; index++) parameters.add(type(types[index]) + " p" + index);
        return String.join(",", parameters);
    }
    static String zero(Class<?> value) {
        if (!value.isPrimitive()) return "null";
        if (value == boolean.class) return "false";
        if (value == char.class) return "'\\0'";
        if (value == long.class) return "0L";
        if (value == float.class) return "0.0F";
        if (value == double.class) return "0.0";
        return "(" + type(value) + ")0";
    }

    @SuppressWarnings("unchecked")
    public static void main(String[] args) throws Exception {
        Path output = Paths.get(args[1]);
        if (!output.getFileName().toString().equals("LegacyWorldAdapter.java")) throw new IllegalArgumentException("Output must be named LegacyWorldAdapter.java");
        Object gson = Class.forName("com.google.gson.Gson").getConstructor().newInstance();
        Map<String, Object> symbols;
        try (Reader reader = Files.newBufferedReader(Paths.get(args[0]))) {
            symbols = (Map<String, Object>) gson.getClass().getMethod("fromJson", Reader.class, Class.class).invoke(gson, reader, Map.class);
        }
        Class.forName((String) symbols.get("bootstrap")).getMethod((String) symbols.get("boot")).invoke(null);
        List<String> hardness = (List<String>) symbols.get("hardness_args");
        Class<?> world = Class.forName(hardness.get(hardness.size() - 2));
        Class<?> position = Class.forName(hardness.get(hardness.size() - 1));
        Class<?> state = Class.forName((String) symbols.get("state"));
        List<Method> stateAccessors = new ArrayList<>();
        for (Class<?> view : world.getInterfaces())
            for (Method method : view.getMethods())
                if (method.getReturnType() == state && Arrays.equals(method.getParameterTypes(), new Class<?>[] {position}))
                    stateAccessors.add(method);
        if (stateAccessors.size() != 1) throw new IllegalStateException("Ambiguous world-view state accessors: " + stateAccessors);
        Method getState = world.getMethod(stateAccessors.get(0).getName(), position);
        List<Method> entityAccessors = new ArrayList<>();
        for (Class<?> view : world.getInterfaces())
            for (Method method : view.getMethods())
                if (method.getReturnType() != state && !method.getReturnType().isPrimitive()
                    && Arrays.equals(method.getParameterTypes(), new Class<?>[] {position})) entityAccessors.add(method);
        if (entityAccessors.size() != 1) throw new IllegalStateException("Ambiguous world-view block-entity accessors: " + entityAccessors);
        Method getEntity = world.getMethod(entityAccessors.get(0).getName(), position);
        List<Field> clientFields = new ArrayList<>();
        for (Field field : world.getFields())
            if (field.getType() == boolean.class && Modifier.isFinal(field.getModifiers()) && !Modifier.isStatic(field.getModifiers()))
                clientFields.add(field);
        if (clientFields.size() != 1) throw new IllegalStateException("Ambiguous client-side world fields: " + clientFields);
        List<Constructor<?>> constructors = new ArrayList<>();
        for (Constructor<?> constructor : world.getDeclaredConstructors())
            if (!Modifier.isPrivate(constructor.getModifiers())) constructors.add(constructor);
        if (constructors.size() != 1) throw new IllegalStateException("Ambiguous legacy world constructors: " + constructors);
        List<String> arguments = new ArrayList<>();
        for (Class<?> parameter : constructors.get(0).getParameterTypes()) arguments.add(zero(parameter));
        StringBuilder source = new StringBuilder("// Generated extraction adapter; no vanilla class is changed.\npublic final class LegacyWorldAdapter extends ")
            .append(type(world)).append(" {\n")
            .append(" public static final String clientSideField = \"").append(clientFields.get(0).getName()).append("\";\n")
            .append(" public static Object air;\n public static final java.util.Map<Object,Object> blocks = new java.util.HashMap<>();\n")
            .append(" public static final java.util.Map<Object,Object> blockEntities = new java.util.HashMap<>();\n")
            .append(" public static final java.util.List<String> reads = new java.util.ArrayList<>();\n")
            .append(" private LegacyWorldAdapter() throws Throwable { super(").append(String.join(",", arguments)).append("); }\n")
            .append(" @Override public ").append(type(state)).append(' ').append(getState.getName()).append('(').append(type(position)).append(" position) {\n")
            .append("  reads.add(position.toString()); return (").append(type(state)).append(")blocks.getOrDefault(position,air);\n }\n")
            .append(" @Override public ").append(type(getEntity.getReturnType())).append(' ').append(getEntity.getName()).append('(').append(type(position)).append(" position) {\n")
            .append("  reads.add(\"entity \" + position); return (").append(type(getEntity.getReturnType())).append(")blockEntities.get(position);\n }\n");
        Map<String, Method> inherited = new TreeMap<>();
        for (Class<?> current = world; current != null; current = current.getSuperclass())
            for (Method method : current.getDeclaredMethods())
                inherited.putIfAbsent(method.getName() + Arrays.toString(method.getParameterTypes()), method);
        for (Method method : world.getMethods())
            inherited.putIfAbsent(method.getName() + Arrays.toString(method.getParameterTypes()), method);
        for (Method method : inherited.values()) {
            if (!Modifier.isAbstract(method.getModifiers()) || method.equals(getState)) continue;
            source.append(" @Override ").append(Modifier.isPublic(method.getModifiers()) ? "public " : "protected ")
                .append(type(method.getReturnType())).append(' ').append(method.getName()).append('(').append(parameters(method.getParameterTypes()))
                .append(") { throw new UnsupportedOperationException(\"Unsupported geometry world operation: ").append(method.getName()).append("\"); }\n");
        }
        source.append("}\n");
        Files.createDirectories(output.toAbsolutePath().getParent());
        Files.write(output, source.toString().getBytes(StandardCharsets.UTF_8));
        System.out.println("world adapter=" + world.getName() + " state getter=" + getState + " -> " + output);
    }
}
