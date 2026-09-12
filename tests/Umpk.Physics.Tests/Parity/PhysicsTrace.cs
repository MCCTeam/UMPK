using System.Globalization;
using System.Text;
using Umpk.Geometry;

namespace Umpk.Physics.Tests.Parity;

/// <summary>
/// The parity/characterization trace format. A trace is an input script plus the per-tick position/velocity/pose the engine produced. The file format is a small line-oriented text format so a future vanilla-recorded trace from a manual instrumented session drops straight into <see cref="TraceReplay"/> without code changes.
///
/// <para>Format (UTF-8, LF):</para>
/// <list type="bullet">
/// <item><c># header</c> lines start with <c>#</c> and are ignored.</item>
/// <item><c>meta &lt;key&gt; &lt;value&gt;</c> lines carry named metadata (profile, start pose).</item>
/// <item><c>start &lt;x&gt; &lt;y&gt; &lt;z&gt; &lt;yaw&gt; &lt;pitch&gt;</c> is the spawn state.</item>
/// <item><c>tick &lt;inputBits&gt; &lt;px&gt; &lt;py&gt; &lt;pz&gt; &lt;vx&gt; &lt;vy&gt; &lt;vz&gt; &lt;pose&gt; &lt;onGround&gt;</c>
/// is one recorded tick: the input applied and the resulting state.</item>
/// </list>
/// All doubles are written round-trippable (<c>R</c>) and invariant-culture.
/// </summary>
public sealed class PhysicsTrace
{
    public required string Profile { get; init; }

    public required Vec3d StartPosition { get; init; }

    public required float StartYaw { get; init; }

    public required float StartPitch { get; init; }

    public required IReadOnlyList<TraceTick> Ticks { get; init; }

    /// <summary>One recorded tick: input encoded as bits, plus the resulting state.</summary>
    public readonly record struct TraceTick(
        int InputBits,
        Vec3d Position,
        Vec3d Velocity,
        int Pose,
        bool OnGround);

    /// <summary>Encodes a <see cref="MovementInput"/> into the trace's compact bit field.</summary>
    public static int EncodeInput(in MovementInput input)
    {
        int bits = 0;
        if (input.Forward)
            bits |= 1 << 0;

        if (input.Back)
            bits |= 1 << 1;

        if (input.Left)
            bits |= 1 << 2;

        if (input.Right)
            bits |= 1 << 3;

        if (input.Jump)
            bits |= 1 << 4;

        if (input.Sneak)
            bits |= 1 << 5;

        if (input.Sprint)
            bits |= 1 << 6;

        if (input.AutoJump)
            bits |= 1 << 7;

        return bits;
    }

    /// <summary>Decodes the trace's bit field back into a <see cref="MovementInput"/>.</summary>
    public static MovementInput DecodeInput(int bits) => new()
    {
        Forward = (bits & (1 << 0)) != 0,
        Back = (bits & (1 << 1)) != 0,
        Left = (bits & (1 << 2)) != 0,
        Right = (bits & (1 << 3)) != 0,
        Jump = (bits & (1 << 4)) != 0,
        Sneak = (bits & (1 << 5)) != 0,
        Sprint = (bits & (1 << 6)) != 0,
        AutoJump = (bits & (1 << 7)) != 0,
    };

    /// <summary>Serializes the trace to the text format.</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append("# UMPK physics characterization trace\n");
        sb.Append(CultureInfo.InvariantCulture, $"meta profile {Profile}\n");
        sb.Append(CultureInfo.InvariantCulture, $"start {D(StartPosition.X)} {D(StartPosition.Y)} {D(StartPosition.Z)} {F(StartYaw)} {F(StartPitch)}\n");
        foreach (TraceTick t in Ticks)
            sb.Append(CultureInfo.InvariantCulture,
                $"tick {t.InputBits} {D(t.Position.X)} {D(t.Position.Y)} {D(t.Position.Z)} {D(t.Velocity.X)} {D(t.Velocity.Y)} {D(t.Velocity.Z)} {t.Pose} {(t.OnGround ? 1 : 0)}\n");

        return sb.ToString();
    }

    /// <summary>Parses a trace from the text format.</summary>
    public static PhysicsTrace Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string profile = "unknown";
        Vec3d start = Vec3d.Zero;
        float yaw = 0, pitch = 0;
        var ticks = new List<TraceTick>();

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "meta" when parts.Length >= 3 && parts[1] == "profile":
                    profile = parts[2];
                    break;
                case "start":
                    start = new Vec3d(P(parts[1]), P(parts[2]), P(parts[3]));
                    yaw = (float)P(parts[4]);
                    pitch = (float)P(parts[5]);
                    break;
                case "tick":
                    ticks.Add(new TraceTick(
                        int.Parse(parts[1], CultureInfo.InvariantCulture),
                        new Vec3d(P(parts[2]), P(parts[3]), P(parts[4])),
                        new Vec3d(P(parts[5]), P(parts[6]), P(parts[7])),
                        int.Parse(parts[8], CultureInfo.InvariantCulture),
                        parts[9] == "1"));
                    break;
                default:
                    break;
            }
        }

        return new PhysicsTrace
        {
            Profile = profile,
            StartPosition = start,
            StartYaw = yaw,
            StartPitch = pitch,
            Ticks = ticks,
        };
    }

    private static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static double P(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
