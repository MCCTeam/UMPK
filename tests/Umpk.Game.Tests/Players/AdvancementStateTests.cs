using Umpk.Game.Players;
using Umpk.Text;
using Xunit;

namespace Umpk.Game.Tests.Players;

public sealed class AdvancementStateTests
{
    private static Advancement Root() => new(
        Identifier.Minecraft("story/root"),
        null,
        new AdvancementDisplay(Component.Text("Minecraft"), Component.Text("Start"), 0, true, false, null),
        ["got_wood"],
        [["got_wood"]]);

    [Fact]
    public void Put_Get_Remove_Advancement()
    {
        var state = new AdvancementState();
        state.PutAdvancement(Root());

        Assert.True(state.TryGetAdvancement(Identifier.Minecraft("story/root"), out Advancement? adv));
        Assert.Equal("Minecraft", adv.Display!.Title.ToPlainText());
        Assert.Single(state.Advancements);

        Assert.True(state.RemoveAdvancement(Identifier.Minecraft("story/root")));
        Assert.Empty(state.Advancements);
    }

    [Fact]
    public void Criterion_Progress_Set_And_Read()
    {
        var state = new AdvancementState();
        var id = Identifier.Minecraft("story/root");
        state.PutAdvancement(Root());

        var when = DateTimeOffset.UnixEpoch;
        state.SetCriterionProgress(id, "got_wood", when);
        state.SetCriterionProgress(id, "pending", null);

        var progress = state.GetProgress(id);
        Assert.Equal(when, progress["got_wood"]);
        Assert.Null(progress["pending"]);
    }

    [Fact]
    public void SelectedTab_And_ShowFlag_Default()
    {
        var state = new AdvancementState();
        Assert.True(state.ShowAdvancements);
        Assert.Null(state.SelectedTab);

        state.SelectedTab = Identifier.Minecraft("story/root");
        state.ShowAdvancements = false;
        Assert.Equal(Identifier.Minecraft("story/root"), state.SelectedTab);
        Assert.False(state.ShowAdvancements);
    }
}
