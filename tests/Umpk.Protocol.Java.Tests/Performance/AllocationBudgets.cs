using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Umpk.Protocol.Java;
using Umpk.TestKit;

namespace Umpk.Protocol.Java.Tests.Performance;

/// <summary>One pinned per-frame allocation budget: the corpus frame it was measured on, the number measured when it was pinned, and the ceiling the suite enforces.</summary>
public sealed class AllocationBudget
{
    /// <summary>The capture protocol.</summary>
    public int Protocol { get; set; }

    /// <summary>The phase the frame resolves under.</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>The flow the frame travelled.</summary>
    public string Flow { get; set; } = string.Empty;

    /// <summary>The canonical packet identifier.</summary>
    public string Packet { get; set; } = string.Empty;

    /// <summary>The capture file name for the protocol corpus.</summary>
    public string Capture { get; set; } = string.Empty;

    /// <summary>The frame's index within the capture.</summary>
    public int Frame { get; set; }

    /// <summary>The frame's wire id, carried so a moved id is visible in the diff.</summary>
    public int WireId { get; set; }

    /// <summary>The recorded body length, carried so a reader can see what the number is per.</summary>
    public int BodyBytes { get; set; }

    /// <summary>The bytes per decode measured when this row was pinned.</summary>
    public long MeasuredBytesPerFrame { get; set; }

    /// <summary>The ceiling: the measurement plus the headroom rule.</summary>
    public long BudgetBytesPerFrame { get; set; }

    /// <summary>The theory key, which is also how a failure names the row.</summary>
    [JsonIgnore]
    public string Key => Format(Protocol, Phase, Flow, Packet);

    /// <summary>Renders the theory key for a row.</summary>
    public static string Format(int protocol, string phase, string flow, string packet) =>
        string.Create(CultureInfo.InvariantCulture, $"{protocol} {phase} {flow} {packet}");
}

/// <summary>The pinned budget file: the coverage note plus the rows.</summary>
public sealed class AllocationBudgetFile
{
    /// <summary>How many implemented bindings exist across the catalog, at pin time.</summary>
    public int ImplementedBindings { get; set; }

    /// <summary>How many of those the corpus exercises, at pin time.</summary>
    public int ExercisedBindings { get; set; }

    /// <summary>How many distinct packet families the corpus covers, which is the row count.</summary>
    public int CoveredFamilies { get; set; }

    /// <summary>The rows, ordered by phase, flow then packet.</summary>
    public List<AllocationBudget> Budgets { get; set; } = [];
}

/// <summary>Loads and writes the pinned per-family allocation ceilings enforced by <see cref="AllocationBudgetTests"/>.</summary>
/// <remarks>A budget is the measured number plus the greater of 8 bytes and 5%, so a legitimate model change (one more field on a record, a generated table that materialises one more array) has somewhere to land. Re-pinning requires a matching benchmark measurement for the changed model.</remarks>
public static class AllocationBudgets
{
    /// <summary>The environment variable that turns a run into a re-pin.</summary>
    public const string UpdateVariable = "UMPK_UPDATE_ALLOCATION_BUDGETS";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lock Gate = new();
    private static AllocationBudgetFile? s_pinned;
    private static bool s_loaded;

    /// <summary>The pinned allocation-budget file path.</summary>
    public static string Path =>
        System.IO.Path.Combine(FixturePaths.RepoRoot(), "engineering", "perf", "allocation_budgets.json");

    /// <summary>The pinned file, or null when it has never been written.</summary>
    public static AllocationBudgetFile? File
    {
        get
        {
            lock (Gate)
            {
                if (!s_loaded)
                {
                    s_pinned = Read();
                    s_loaded = true;
                }

                return s_pinned;
            }
        }
    }

    /// <summary>The pinned row for a key, or null when the key is not pinned.</summary>
    public static AllocationBudget? Get(string key) =>
        File?.Budgets.FirstOrDefault(b => b.Key == key);

    /// <summary>The budget for a measurement: the number itself plus the greater of 8 bytes and 5%, so a small model change does not fail the build and a reintroduced copy still does.</summary>
    public static long WithHeadroom(long measured) =>
        measured + Math.Max(8L, (long)Math.Ceiling(measured * 0.05));

    /// <summary>Writes the file, creating its parent directory when needed.</summary>
    public static void Write(AllocationBudgetFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        System.IO.File.WriteAllText(Path, JsonSerializer.Serialize(file, WriteOptions) + "\n");
        lock (Gate)
        {
            s_pinned = file;
            s_loaded = true;
        }
    }

    private static AllocationBudgetFile? Read() =>
        System.IO.File.Exists(Path)
            ? JsonSerializer.Deserialize<AllocationBudgetFile>(System.IO.File.ReadAllText(Path), ReadOptions)
            : null;
}
