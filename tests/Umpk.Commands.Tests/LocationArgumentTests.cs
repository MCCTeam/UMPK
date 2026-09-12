using Umpk.Geometry;
using Xunit;

namespace Umpk.Commands.Tests;

public sealed class LocationArgumentTests
{
    private static async Task<(bool Success, CommandLocation Value)> RunAsync(string input, IArgumentType<CommandLocation> type)
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        CommandLocation captured = default;
        scope.Register(b => b.Literal("cmd", n =>
            n.ThenArgument("arg", type, a =>
                a.Executes(ctx =>
                {
                    captured = ctx.GetArgument<CommandLocation>("arg");
                    return new ValueTask<int>(1);
                }))));

        var result = await service.ExecuteAsync("cmd " + input, new TestSource());
        return (result.Success, captured);
    }

    private static async Task<List<string>> SuggestAsync(string input, int cursor)
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("loc", n =>
            n.ThenArgument("pos", Arguments.Location(), a =>
                a.Executes(_ => new ValueTask<int>(1)))));

        var result = await service.CompleteAsync(input, cursor, new TestSource());
        return result.Suggestions.Select(s => s.Text).ToList();
    }

    [Fact]
    public async Task Absolute_integer_coordinates_are_centred_like_vanilla()
    {
        var (ok, value) = await RunAsync("10 20 30", Arguments.Location());

        Assert.True(ok);
        Assert.Equal(10.5, value.X);
        // Y is never centre-corrected, so a bare Y integer stays exactly on the floor.
        Assert.Equal(20.0, value.Y);
        Assert.Equal(30.5, value.Z);
    }

    [Fact]
    public async Task Absolute_decimal_coordinates_are_not_centred()
    {
        var (ok, value) = await RunAsync("10.0 20.0 30.0", Arguments.Location());

        Assert.True(ok);
        Assert.Equal(10.0, value.X);
        Assert.Equal(20.0, value.Y);
        Assert.Equal(30.0, value.Z);
    }

    [Fact]
    public async Task Centre_correction_is_off_when_the_caller_asks_for_it()
    {
        var (ok, value) = await RunAsync("10 20 30", Arguments.Location(centerCorrect: false));

        Assert.True(ok);
        Assert.Equal(10.0, value.X);
        Assert.Equal(20.0, value.Y);
        Assert.Equal(30.0, value.Z);
    }

    [Fact]
    public async Task A_bare_tilde_is_a_zero_offset_on_that_axis()
    {
        var (ok, value) = await RunAsync("~ ~ ~", Arguments.Location());

        Assert.True(ok);
        Assert.True(value.IsRelativeX);
        Assert.True(value.IsRelativeY);
        Assert.True(value.IsRelativeZ);
        Assert.Equal(0.0, value.X);
        Assert.Equal(0.0, value.Y);
        Assert.Equal(0.0, value.Z);
    }

    [Fact]
    public async Task A_tilde_with_a_value_offsets_that_axis()
    {
        var (ok, value) = await RunAsync("~1 ~2 ~3", Arguments.Location());

        Assert.True(ok);
        Assert.True(value.IsRelativeX);
        Assert.True(value.IsRelativeY);
        Assert.True(value.IsRelativeZ);
        Assert.Equal(1.0, value.X);
        Assert.Equal(2.0, value.Y);
        Assert.Equal(3.0, value.Z);
    }

    [Fact]
    public async Task A_negative_tilde_offset_parses()
    {
        var (ok, value) = await RunAsync("~-5 ~ ~", Arguments.Location());

        Assert.True(ok);
        Assert.True(value.IsRelativeX);
        Assert.Equal(-5.0, value.X);
    }

    [Fact]
    public async Task A_fullwidth_tilde_is_accepted_as_a_tilde()
    {
        var (ok, value) = await RunAsync("～1 ～ ～", Arguments.Location());

        Assert.True(ok);
        Assert.True(value.IsRelativeX);
        Assert.True(value.IsRelativeY);
        Assert.True(value.IsRelativeZ);
        Assert.Equal(1.0, value.X);
    }

    [Fact]
    public async Task A_signed_plus_offset_after_a_tilde_is_accepted()
    {
        var (ok, value) = await RunAsync("~+5 ~ ~", Arguments.Location());

        Assert.True(ok);
        Assert.True(value.IsRelativeX);
        Assert.Equal(5.0, value.X);
    }

    [Fact]
    public async Task Caret_coordinates_parse_as_local()
    {
        var (ok, value) = await RunAsync("^1 ^2 ^3", Arguments.Location());

        Assert.True(ok);
        Assert.True(value.IsLocal);
        Assert.Equal(1.0, value.X); // left
        Assert.Equal(2.0, value.Y); // up
        Assert.Equal(3.0, value.Z); // forwards
    }

    [Fact]
    public async Task Mixing_a_caret_and_a_tilde_is_rejected()
    {
        var (ok, _) = await RunAsync("^1 ~2 ^3", Arguments.Location());

        Assert.False(ok);
    }

    [Fact]
    public async Task An_incomplete_caret_triple_is_rejected()
    {
        var (ok, _) = await RunAsync("^1 ^2", Arguments.Location());

        Assert.False(ok);
    }

    [Fact]
    public void Local_coordinates_resolve_against_the_players_facing()
    {
        // left=1, up=2, forwards=3; expected offsets use the forward, up, and left basis vectors derived from yaw and pitch in double precision.
        var local = new CommandLocation(1, 2, 3, IsLocal: true, Relativity: 0);

        // yaw=0, pitch=0: forward=(0,0,1), up=(0,1,0), left=(1,0,0) offset = forward*3 + up*2 + left*1 = (1, 2, 3)
        var atYaw0 = local.ToAbsolute(Vec3d.Zero, yaw: 0f, pitch: 0f);
        Assert.Equal(1.0, atYaw0.X, precision: 9);
        Assert.Equal(2.0, atYaw0.Y, precision: 9);
        Assert.Equal(3.0, atYaw0.Z, precision: 9);

        // yaw=90, pitch=0: forward=(-1,0,0), up=(0,1,0), left=(0,0,1) offset = forward*3 + up*2 + left*1 = (-3, 2, 1)
        var atYaw90 = local.ToAbsolute(Vec3d.Zero, yaw: 90f, pitch: 0f);
        Assert.Equal(-3.0, atYaw90.X, precision: 9);
        Assert.Equal(2.0, atYaw90.Y, precision: 9);
        Assert.Equal(1.0, atYaw90.Z, precision: 9);

        // yaw=180, pitch=0: forward=(0,0,-1), up=(0,1,0), left=(-1,0,0) offset = forward*3 + up*2 + left*1 = (-1, 2, -3)
        var atYaw180 = local.ToAbsolute(Vec3d.Zero, yaw: 180f, pitch: 0f);
        Assert.Equal(-1.0, atYaw180.X, precision: 9);
        Assert.Equal(2.0, atYaw180.Y, precision: 9);
        Assert.Equal(-3.0, atYaw180.Z, precision: 9);

        // yaw=0, pitch=-45: forward=(0, sqrt2/2, sqrt2/2), up=(0, sqrt2/2, -sqrt2/2), left=(1,0,0) offset = forward*3 + up*2 + left*1
        //        = (0, 2.1213203435596424, 2.1213203435596424) + (0, 1.4142135623730951, -1.4142135623730951) + (1, 0, 0)
        //        = (1, 3.5355339059327378, 0.7071067811865475)
        var atPitchNeg45 = local.ToAbsolute(Vec3d.Zero, yaw: 0f, pitch: -45f);
        Assert.Equal(1.0, atPitchNeg45.X, precision: 9);
        Assert.Equal(3.5355339059327378, atPitchNeg45.Y, precision: 9);
        Assert.Equal(0.7071067811865475, atPitchNeg45.Z, precision: 9);
    }

    [Fact]
    public void Resolving_a_local_triple_without_a_facing_throws()
    {
        var local = new CommandLocation(1, 2, 3, IsLocal: true, Relativity: 0);

        var ex = Assert.Throws<InvalidOperationException>(() => local.ToAbsolute(Vec3d.Zero));
        Assert.Contains("ToAbsolute(Vec3d, float, float)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToAbsolute_resolves_each_relative_axis_against_the_current_position()
    {
        var current = new Vec3d(100, 64, -200);
        // X and Z relative (bits 0b001 and 0b100), Y absolute.
        var location = new CommandLocation(1, -2, 3, IsLocal: false, Relativity: 0b101);

        var result = location.ToAbsolute(current);

        Assert.Equal(101.0, result.X);
        Assert.Equal(-2.0, result.Y);
        Assert.Equal(-197.0, result.Z);
    }

    [Fact]
    public void ToBlockPos_contains_the_resolved_position()
    {
        var current = new Vec3d(10.2, 64.9, -5.5);
        var location = new CommandLocation(0, 0, 0, IsLocal: false, Relativity: 0b111); // ~ ~ ~

        var block = location.ToBlockPos(current);

        Assert.Equal(new BlockPos(10, 64, -6), block);
    }

    [Fact]
    public async Task Suggestions_offer_the_three_tilde_forms_on_an_empty_token()
    {
        var texts = await SuggestAsync("loc ", 4);

        Assert.Contains("~", texts);
        Assert.Contains("~ ~", texts);
        Assert.Contains("~ ~ ~", texts);
    }

    [Fact]
    public async Task Suggestions_extend_a_partially_typed_coordinate()
    {
        var texts = await SuggestAsync("loc ~5", 6);

        Assert.Contains("~5 ~", texts);
        Assert.Contains("~5 ~ ~", texts);
    }

    [Fact]
    public async Task Suggestions_offer_the_local_form_after_a_caret()
    {
        // A single "^" is already one whole typed token, so - exactly like the "~5" case above - vanilla extends it with the remaining axes rather than re-offering "^" itself.
        var texts = await SuggestAsync("loc ^", 5);

        Assert.Contains("^ ^", texts);
        Assert.Contains("^ ^ ^", texts);
        Assert.DoesNotContain("~ ~ ~", texts);
    }
}
