using Umpk.DataGen;

// Umpk.DataGen: validates the canonical dataset and emits Umpk.Data.Java sources. Commands:
//   verify   --data <dir>                                   validate all invariants (legacy + modern)
//   diff     --data <dir> <old> <new>                       structured version diff (protocol numbers)
//   layouts  --data <dir> <protocol> [--diff-oracle] [--against <p>] [--oracle <dir>]
//                                                            read packet layouts from an oracle tree;
//                                                            with
//                                                            --diff-oracle, print every packet whose
//                                                            layout differs from an earlier oracle
//                                                            baseline. Diagnostic only, never a gate
//   generate --data <dir> --out <dir> [--out-lang <dir>] [--protocol-out <dir>]
//                                                            emit C# + binary blobs (--out-lang
//                                                            additionally emits the vanilla lang tables,
//                                                            --protocol-out the per-era codec tables;
//                                                            absent, neither
//                                                            writes anything)
return DataGenCli.Run(args);

namespace Umpk.DataGen
{
    internal static class DataGenCli
    {
        public static int Run(string[] args)
        {
            if (args.Length == 0)
                return Usage();

            try
            {
                return args[0] switch
                {
                    "verify" => Verify(Options.Parse(args)),
                    "diff" => Diff(Options.Parse(args)),
                    "layouts" => Layouts(Options.Parse(args)),
                    "generate" => Generate(Options.Parse(args)),
                    _ => Usage(),
                };
            }
            catch (DatasetException ex)
            {
                Console.Error.WriteLine($"dataset error: {ex.Message}");
                return 1;
            }
        }

        private static int Verify(Options options)
        {
            Dataset dataset = DatasetLoader.Load(options.Data);
            IReadOnlyList<string> problems = Validator.Validate(dataset);
            if (problems.Count == 0)
            {
                Console.WriteLine($"verify: OK ({dataset.ByProtocol.Count} protocols)");
                return 0;
            }
            Console.Error.WriteLine($"verify: {problems.Count} problem(s):");
            foreach (string problem in problems)
                Console.Error.WriteLine($"  - {problem}");

            return 1;
        }

        private static int Diff(Options options)
        {
            if (options.Positional.Count != 2
                || !int.TryParse(options.Positional[0], out int older)
                || !int.TryParse(options.Positional[1], out int newer))
            {
                Console.Error.WriteLine("usage: diff --data <dir> <oldProtocol> <newProtocol>");
                return 2;
            }
            Dataset dataset = DatasetLoader.Load(options.Data);
            if (!dataset.ByProtocol.TryGetValue(older, out VersionData? o)
                || !dataset.ByProtocol.TryGetValue(newer, out VersionData? n))
            {
                Console.Error.WriteLine("diff: one or both protocols are not in the dataset");
                return 1;
            }
            Console.Write(Differ.Diff(o, n));
            return 0;
        }

        /// <summary>Prints the packet layouts inferred for a protocol. With <c>--diff-oracle</c>, it also prints layouts that differ from an earlier comparison baseline.</summary>
        /// <remarks>It exits 0 whenever it produced a report, including a report full of changes. Making it exit non-zero would turn a heuristic layout read into a gate, and the parse is not sound enough to carry that: see <see cref="LayoutOracle"/>.</remarks>
        private static int Layouts(Options options)
        {
            if (options.Positional.Count != 1 || !int.TryParse(options.Positional[0], out int protocol))
            {
                Console.Error.WriteLine("usage: layouts --data <dir> <protocol> [--diff-oracle] [--against <protocol>] [--oracle <dir>]");
                return 2;
            }

            if (options.Against is not null && !options.DiffOracle)
            {
                Console.Error.WriteLine("layouts: --against requires --diff-oracle.");
                return 2;
            }

            Dataset dataset = DatasetLoader.Load(options.Data);
            if (LayoutOracle.OracleRoot(options.Oracle) is not string root)
            {
                Console.Error.WriteLine("layouts: no decompiled root. Pass --oracle <dir> or set UMPK_ORACLE_ROOT.");
                return 1;
            }

            if (Tree(dataset, root, protocol) is not (string proposedTree, string proposedName))
                return 1;

            LayoutOracle.TreeLayouts proposed = LayoutOracle.Read(proposedTree);
            if (proposed.Packets.Count == 0)
            {
                Console.Error.WriteLine(
                    $"layouts: {proposedName}-decompiled declares no packet types. Vanilla only carries them "
                    + "from 1.20.5 (protocol 766) onward, so an older tree cannot be read this way.");
                return 1;
            }

            if (!options.DiffOracle)
            {
                Console.Write(LayoutOracle.Listing(proposed));
                return 0;
            }

            int baselineProtocol = options.Against
                ?? dataset.ByProtocol.Keys.Where(p => p < protocol).DefaultIfEmpty(-1).Max();
            if (baselineProtocol < 0)
            {
                Console.Error.WriteLine($"layouts: {protocol} has no earlier supported protocol to use as a source baseline.");
                return 1;
            }

            if (Tree(dataset, root, baselineProtocol) is not (string baselineTree, string baselineName))
                return 1;

            LayoutOracle.TreeLayouts baseline = LayoutOracle.Read(baselineTree);
            if (baseline.Packets.Count == 0)
            {
                Console.Error.WriteLine(
                    $"layouts: {baselineName}-decompiled has no recognized packet layouts. "
                    + "Refusing to treat an empty or unrecognized baseline as an all-new tree.");
                return 1;
            }

            Console.Write(LayoutOracle.Report(baseline, proposed));
            return 0;
        }

        private static (string Tree, string Version)? Tree(Dataset dataset, string root, int protocol)
        {
            if (!dataset.ByProtocol.TryGetValue(protocol, out VersionData? version))
            {
                Console.Error.WriteLine($"layouts: protocol {protocol} is not in the dataset.");
                return null;
            }

            if (LayoutOracle.TreeFor(root, version.VersionName) is not string tree)
            {
                Console.Error.WriteLine(
                    $"layouts: no {version.VersionName}-decompiled under {root}. Decompile that version, or name a "
                    + "different comparison with --against.");
                return null;
            }

            return (tree, version.VersionName);
        }

        private static int Generate(Options options)
        {
            Dataset dataset = DatasetLoader.Load(options.Data);
            IReadOnlyList<string> problems = Validator.Validate(dataset);
            if (problems.Count > 0)
            {
                Console.Error.WriteLine($"generate: refusing to emit; dataset has {problems.Count} problem(s). Run verify.");
                return 1;
            }

            var emitter = new Emitter(dataset);
            IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();

            if (options.Out is null)
                Console.WriteLine($"generate: {files.Count} file(s) would be emitted ({emitter.SharedTableCount} shared tables). Pass --out to write.");

            else
            {
                foreach (Emitter.EmittedFile file in files.Values)
                {
                    string path = Path.Combine(options.Out, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, file.Text);
                }
                Console.WriteLine($"generate: wrote {files.Count} file(s) to {options.Out} ({emitter.SharedTableCount} shared tables)");
            }

            // --protocol-out is independent of --out, same as --out-lang: the per-era tables it writes live beside the codecs that read them in a different package, and the freshness gate diffs both roots.
            if (options.ProtocolOut is not null)
            {
                IReadOnlyDictionary<string, Emitter.EmittedFile> protocolFiles = emitter.EmitProtocol();
                foreach (Emitter.EmittedFile file in protocolFiles.Values)
                {
                    string path = Path.Combine(options.ProtocolOut, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, file.Text);
                }
                Console.WriteLine($"generate: wrote {protocolFiles.Count} protocol file(s) to {options.ProtocolOut}");
            }

            // --out-lang is independent of --out: absent, no lang files are written.
            if (options.OutLang is not null)
            {
                IReadOnlyDictionary<string, Emitter.EmittedFile> langFiles = emitter.EmitLang();
                foreach (Emitter.EmittedFile file in langFiles.Values)
                {
                    string path = Path.Combine(options.OutLang, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, file.Text);
                }
                Console.WriteLine($"generate: wrote {langFiles.Count} lang file(s) to {options.OutLang}");
            }

            return 0;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("usage: umpk-datagen <verify|diff|layouts|generate> --data <dir> [--out <dir>] [--out-lang <dir>] [--protocol-out <dir>] [--oracle <dir>] [--diff-oracle] [--against <protocol>] [args]");
            return 2;
        }
    }

    internal sealed class Options
    {
        public required string Data { get; init; }
        public string? Out { get; init; }

        /// <summary>Destination for the generated vanilla-language tables. Absent by default, in which case <c>generate</c> never calls <see cref="Emitter.EmitLang"/> and no lang output is produced; existing <c>--out</c>-only invocations are unaffected.</summary>
        public string? OutLang { get; init; }

        /// <summary>Destination for the per-era codec tables (component ids, metadata serializers, particle ids, argument types). Absent by default, in which case <c>generate</c> never calls <see cref="Emitter.EmitProtocol"/>.</summary>
        public string? ProtocolOut { get; init; }

        /// <summary>Where the layout-oracle trees live for <c>layouts</c>. Absent falls back to <c>UMPK_ORACLE_ROOT</c> and then to the repository-local <c>MinecraftOfficial</c> directory.</summary>
        public string? Oracle { get; init; }

        /// <summary>Compare a protocol's inferred layouts against an earlier baseline.</summary>
        public bool DiffOracle { get; init; }

        /// <summary>The protocol to compare against, overriding the previous supported one.</summary>
        public int? Against { get; init; }

        public required IReadOnlyList<string> Positional { get; init; }

        public static Options Parse(string[] args)
        {
            string? data = null;
            string? outDir = null;
            string? outLangDir = null;
            string? protocolOutDir = null;
            string? oracleDir = null;
            bool diffOracle = false;
            int? against = null;
            List<string> positional = [];
            for (int i = 1; i < args.Length; i++)
                switch (args[i])
                {
                    case "--data":
                        data = Next(args, ref i);
                        break;
                    case "--out":
                        outDir = Next(args, ref i);
                        break;
                    case "--out-lang":
                        outLangDir = Next(args, ref i);
                        break;
                    case "--protocol-out":
                        protocolOutDir = Next(args, ref i);
                        break;
                    case "--oracle":
                        oracleDir = Next(args, ref i);
                        break;
                    case "--diff-oracle":
                        diffOracle = true;
                        break;
                    case "--against":
                        string value = Next(args, ref i);
                        against = int.TryParse(value, out int parsed)
                            ? parsed
                            : throw new DatasetException($"--against wants a protocol number, not \"{value}\"");
                        break;
                    default:
                        positional.Add(args[i]);
                        break;
                }

            return new Options
            {
                Data = data ?? throw new DatasetException("--data <dir> is required"),
                Out = outDir,
                OutLang = outLangDir,
                ProtocolOut = protocolOutDir,
                Oracle = oracleDir,
                DiffOracle = diffOracle,
                Against = against,
                Positional = positional,
            };
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
                throw new DatasetException($"missing value after {args[i]}");

            return args[++i];
        }
    }
}
