# UMPK Java jar-driven extractors

Two programs live here and they share one mechanism: parse the official `server_mappings.txt` and reach every Minecraft type by REFLECTION, so an UNMODIFIED obfuscated server jar can be driven with no remap, no ASM and no compile-time binding.

- `ShapeDump.java` dumps per-state collision AABBs (the rest of this README).
- `PushDump.java` dumps the four per-state values `PistonBaseBlock.isPushable` and `PistonStructureResolver.resolve` read: `getPistonPushReaction()`, `getDestroySpeed()`, `hasBlockEntity()` / `Block.isEntityBlock()`, and `isAir()`. `tools/extraction/extract_push_reactions.py` folds its output into `data/java/<protocol>/block-push.json`. The mapping parser is duplicated between the two on purpose: both are standalone single-file programs compiled and run by hand, and nothing in the gate would catch a refactor of the verified one.

`PushDump` needs two things `ShapeDump` does not:

- **1.14.4 and 1.15.2**: compile with `javac --release 11` as well as running under `openjdk@11`, or the class file is too new for that JVM.
- **1.17 and 1.17.1**: `log4j-shim/LogManager.java`, compiled into a directory placed AHEAD of the server classes. 1.17's server calls `LogManager.getLogger()` with no argument and every log4j-api resolves the caller by a stack walk that returns null when the class is initialized under a current JVM's reflective invoke, so `Bootstrap` throws before any registry exists. Substituting a newer log4j-api (2.25.2) does NOT fix it. The stub does, and neither dumper ever logs.

**Which versions can be driven at all**: Mojang publishes `server_mappings.txt` only from 1.14.4 on, and 26.1+ ship unobfuscated (pass `none` as the mappings argument). Of UMPK's 38 flattened protocols, 17 have a mappings file offline: 498, 578, 755, 756, 759, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775,
776. The other 21 need one download each before they can be measured.

# Block shape extractor

The Mojang-derived shape extractor: a small Java program run against the official server jar that walks the server's own block-state registry and dumps, per block state, the collision-shape AABB list.

This replaces third-party collision-shape snapshots with a direct vanilla-derived measurement.

## Status

`ShapeDump.java` parses `server_mappings.txt` and reaches each required type by reflection. It needs no remap, ASM pass, or compile-time binding, and one source file covers 1.14.4 through 26.x.

What it does NOT yet dump: `friction`, `speedFactor`, `jumpFactor`, `blocksMotion` and fluid state per state. Those still live as material-level toggles in `shared/block-attributes.json`. Adding them is more reflection in the same loop, not a new mechanism.

## Requirements

- A compatible JDK with `java` and `javac` on `PATH`.
- The version's `server.jar` and its `server_mappings.txt`. Mojang publishes mappings from 1.14.4 on, so 1.13 - 1.14.3 cannot be driven this way.
- The server classes and their libraries on the classpath, UNSIGNED. Some shipped library jars are signed and the JVM refuses to mix them with the unsigned server classes ("signer information does not match"), so explode everything into one directory and drop `META-INF/*.SF|DSA|RSA`.

## Running it

```bash
JAVA=$(command -v java)
DL=<...>/MinecraftOfficial/downloads/1.21.4

# 1. explode server + libraries into one unsigned classpath directory.
#    1.18+ jars are bundlers: the real jar and the libs are inside META-INF.
mkdir -p /tmp/cp && cd /tmp/cp
unzip -qo "$DL/server.jar" 'META-INF/versions/*' 'META-INF/libraries/*' -d /tmp/_b
for j in $(find /tmp/_b -name '*.jar'); do
  unzip -qo "$j" -x 'META-INF/*.SF' 'META-INF/*.DSA' 'META-INF/*.RSA'
done

# 2. dump
"$JAVA" -cp <this-dir>/classes:/tmp/cp ShapeDump "$DL/server_mappings.txt" shapes-1.21.4.json
```

Pre-1.18 server jars are already fat, so step 1 is a single `unzip` of `server.jar`. 1.14.4 and 1.15.2 need an older runtime (openjdk@11). For 1.17.x, compile `log4j-shim/LogManager.java` and place its output ahead of the server classes. Replacing the shipped log4j jars does not solve the reflective caller lookup failure described above.

## What it is for

Cross-checking the committed shape dataset against vanilla. The committed modern tables are a `curated-shape-bootstrap`: one repository-local modern snapshot re-indexed positionally onto each band's own state order. That is exact while a block's state schema has not changed since the snapshot, and silently wrong where it has. Measured against this dumper:

| protocol | version | states | wrong | share |
| --- | --- | --- | --- | --- |
| 498 | 1.14.4 | 11271 | 875 | 7.76% |
| 578 | 1.15.2 | 11337 | 861 | 7.59% |
| 756 | 1.17.1 | 20342 | 25 | 0.12% |
| 765 | 1.20.4 | 26644 | 7 | 0.03% |
| 766 | 1.20.6 | 26684 | 7 | 0.03% |
| 769 | 1.21.4 | 27866 | 7 | 0.03% |
| 770 | 1.21.5 | 27914 | 6 | 0.02% |
| 773 | 1.21.9 | 29671 | 0 | 0.00% |

(Those seven and six are the renamed blocks, since fixed. The 1.14/1.15 mass is schema drift: walls carried 64 states then and 324 from 1.16, so the positional read takes the first 64 of the wrong list. The 1.17 residue is the wall skulls, which gained `powered` at 1.20.3.)

The remaining drift is only fixable by dumping each band from its own jar, which is what this program is for. It was not done in the run that measured the table above because only eight of the flattened bands have `server_mappings.txt` present offline.
