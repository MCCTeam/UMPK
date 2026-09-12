package org.apache.logging.log4j;

import java.lang.reflect.Proxy;
import java.net.URI;
import org.apache.logging.log4j.message.MessageFactory;
import org.apache.logging.log4j.spi.LoggerContext;
import org.apache.logging.log4j.spi.LoggerContextFactory;

/**
 * Classpath shim that shadows log4j-api's own LogManager, needed to drive server
 * jars whose bundled log4j-api throws under a current JVM.
 *
 * 1.17.x's server declares its loggers as {@code LogManager.getLogger()} with NO
 * argument, and every log4j-api resolves the caller for that overload through a
 * stack walk. Under a current JVM the class is initialized from a reflective
 * invoke and that walk returns null, so log4j throws
 * "No class provided, and an appropriate one cannot be found" out of the server's
 * own static initializer, before any registry exists. Substituting a newer
 * log4j-api (2.25.2 was tried) does not change the result.
 *
 * 1.18.x-1.20.x hit a second, distinct failure once this class starts shadowing
 * the real one: once LogManager.getLogger() is safe, their bundled log4j-core
 * static initializers walk the REST of LogManager's public surface too
 * (getFactory(), getContext(boolean), and further getContext() overloads,
 * discovered one NoSuchMethodError at a time by running against each era's own
 * server jar). The full method list below is `javap -public
 * org.apache.logging.log4j.LogManager` against the actual bundled log4j-api jar
 * for 2.14.1 (1.18.1), 2.17.0 (1.18.2/1.19.2) and 2.19.0 (1.19.3-1.20.2) -- the
 * public surface is identical across all three, so one shim file covers all of
 * them. Every return value is a recursively-stubbed Proxy when the return type is
 * an interface (so e.g. getContext().getLogger(name) does not NoSuchMethodError
 * either), or a primitive/String default otherwise, since neither ShapeDump nor
 * PushDump ever logs and no real context/factory/logger is ever needed.
 *
 * Compile it against the target server's own classpath and put the OUTPUT
 * DIRECTORY ahead of the server classes:
 *
 *   javac -nowarn -cp /tmp/cp/1.17 -d /tmp/shim log4j-shim/LogManager.java
 *   java -cp classes:/tmp/shim:/tmp/cp/1.17 PushDump .../server_mappings.txt out.json
 *
 * The class must be PUBLIC and the file must therefore be named LogManager.java:
 * a package-private stub compiles, and then the server's own class fails with
 * "IllegalAccessError: failed to access class org.apache.logging.log4j.LogManager".
 */
public final class LogManager {

    @SuppressWarnings("unchecked")
    static <T> T stub(Class<T> iface) {
        return (T) Proxy.newProxyInstance(
                LogManager.class.getClassLoader(), new Class<?>[] { iface },
                (proxy, method, args) -> {
                    Class<?> returnType = method.getReturnType();
                    if (returnType == boolean.class) return Boolean.FALSE;
                    if (returnType == int.class) return 0;
                    if (returnType == long.class) return 0L;
                    if (returnType == void.class) return null;
                    if (returnType == String.class) return "stub";
                    if (returnType.isInterface()) return stub(returnType);
                    return null;
                });
    }

    private static final Logger LOGGER_STUB = stub(Logger.class);
    private static final LoggerContextFactory FACTORY_STUB = stub(LoggerContextFactory.class);

    /**
     * A REAL org.apache.logging.log4j.core.LoggerContext, not a Proxy: log4j-core's own
     * static methods (e.g. core.LoggerContext.getContext(boolean)) cast LogManager's
     * return value down to the concrete core class, which a Proxy against only the spi
     * interface cannot satisfy. log4j-core is always present on these server classpaths
     * (it is a real bundled dependency, not something this shim fakes), so this
     * constructs the genuine class; the fallback only matters if some future era ships
     * without it, in which case callers that need the interface alone still get a stub.
     */
    private static final LoggerContext CONTEXT_STUB = newRealContext();

    private static LoggerContext newRealContext() {
        try {
            Class<?> coreContext = Class.forName("org.apache.logging.log4j.core.LoggerContext");
            return (LoggerContext) coreContext.getConstructor(String.class).newInstance("shim");
        } catch (ReflectiveOperationException e) {
            return stub(LoggerContext.class);
        }
    }

    private LogManager() {
    }

    public static boolean exists(String name) { return false; }

    public static LoggerContext getContext() { return CONTEXT_STUB; }

    public static LoggerContext getContext(boolean currentContext) { return CONTEXT_STUB; }

    public static LoggerContext getContext(ClassLoader loader, boolean currentContext) { return CONTEXT_STUB; }

    public static LoggerContext getContext(ClassLoader loader, boolean currentContext, Object externalContext) {
        return CONTEXT_STUB;
    }

    public static LoggerContext getContext(ClassLoader loader, boolean currentContext, URI configLocation) {
        return CONTEXT_STUB;
    }

    public static LoggerContext getContext(
            ClassLoader loader, boolean currentContext, Object externalContext, URI configLocation) {
        return CONTEXT_STUB;
    }

    public static LoggerContext getContext(
            ClassLoader loader, boolean currentContext, Object externalContext, URI configLocation, String name) {
        return CONTEXT_STUB;
    }

    public static void shutdown() { }

    public static void shutdown(boolean currentContext) { }

    public static void shutdown(boolean currentContext, boolean allContexts) { }

    public static void shutdown(LoggerContext context) { }

    public static LoggerContextFactory getFactory() { return FACTORY_STUB; }

    public static void setFactory(LoggerContextFactory factory) { }

    public static Logger getFormatterLogger() { return LOGGER_STUB; }

    public static Logger getFormatterLogger(Class<?> owner) { return LOGGER_STUB; }

    public static Logger getFormatterLogger(Object owner) { return LOGGER_STUB; }

    public static Logger getFormatterLogger(String name) { return LOGGER_STUB; }

    public static Logger getLogger() { return LOGGER_STUB; }

    public static Logger getLogger(Class<?> owner) { return LOGGER_STUB; }

    public static Logger getLogger(Class<?> owner, MessageFactory messageFactory) { return LOGGER_STUB; }

    public static Logger getLogger(MessageFactory messageFactory) { return LOGGER_STUB; }

    public static Logger getLogger(Object owner) { return LOGGER_STUB; }

    public static Logger getLogger(Object owner, MessageFactory messageFactory) { return LOGGER_STUB; }

    public static Logger getLogger(String name) { return LOGGER_STUB; }

    public static Logger getLogger(String name, MessageFactory messageFactory) { return LOGGER_STUB; }

    public static Logger getRootLogger() { return LOGGER_STUB; }
}
