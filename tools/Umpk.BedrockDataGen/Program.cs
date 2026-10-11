using System.Text.Json;
using Umpk.BedrockDataGen;
using Umpk.BedrockDataGen.Schema;

return BedrockDataGenCli.Run(args, Console.Out, Console.Error);

namespace Umpk.BedrockDataGen
{
    internal static class BedrockDataGenCli
    {
        public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            if (args.Count == 0 || args[0] is "--help" or "-h")
            {
                output.WriteLine("usage: Umpk.BedrockDataGen <import|normalize|verify|diff|generate|verify-generated|generate-skin|verify-skin-generated> [options]");
                return 0;
            }

            try
            {
                var options = ParseOptions(args.Skip(1));
                switch (args[0])
                {
                    case "import":
                        BedrockDataPipeline.Import(Require(options, "--source-root"), Require(options, "--data-root"));
                        break;
                    case "normalize":
                        BedrockDataPipeline.Normalize(Require(options, "--data-root"));
                        break;
                    case "verify":
                        BedrockDataPipeline.Verify(Require(options, "--data-root"));
                        break;
                    case "diff":
                        SchemaDiff diff = BedrockDataPipeline.Diff(Require(options, "--base"), Require(options, "--target"));
                        output.WriteLine(JsonSerializer.Serialize(diff, BedrockDataJsonContext.Default.SchemaDiff));
                        break;
                    case "generate":
                        BedrockDataPipeline.Generate(
                            Require(options, "--data-root"),
                            Require(options, "--protocol-out"),
                            Require(options, "--data-out"));
                        break;
                    case "verify-generated":
                        BedrockDataPipeline.VerifyGenerated(
                            Require(options, "--data-root"),
                            Require(options, "--protocol-out"),
                            Require(options, "--data-out"));
                        break;
                    case "generate-skin":
                        BedrockDataPipeline.GenerateSkin(
                            Require(options, "--data-root"),
                            Require(options, "--skin-out"));
                        break;
                    case "verify-skin-generated":
                        BedrockDataPipeline.VerifySkinGenerated(
                            Require(options, "--data-root"),
                            Require(options, "--skin-out"));
                        break;
                    default:
                        error.WriteLine($"unknown command: {args[0]}");
                        return 2;
                }
                return 0;
            }
            catch (Exception exception) when (exception is SchemaException or IOException or JsonException or ArgumentException)
            {
                error.WriteLine(exception.Message);
                return 2;
            }
        }

        private static Dictionary<string, string> ParseOptions(IEnumerable<string> arguments)
        {
            string[] values = arguments.ToArray();
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Length; index += 2)
            {
                if (index + 1 >= values.Length || !values[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Option '{values[index]}' requires a value.");
                if (!options.TryAdd(values[index], values[index + 1]))
                    throw new ArgumentException($"Option '{values[index]}' was provided more than once.");
            }
            return options;
        }

        private static string Require(IReadOnlyDictionary<string, string> options, string name)
        {
            if (!options.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Required option {name} was not provided.");
            return value;
        }
    }
}
