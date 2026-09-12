using Umpk.Client.Chat;
using Umpk.Client.Events;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="ChatStandingRule"/> classifies chat consistently for each host. Every non-<see cref="ChatVerification.NotApplicable"/> verification maps straight across, <see cref="ChatVerification.NotApplicable"/> resolves through <see cref="ChatCategory"/> (server-voiced becomes <see cref="ChatStanding.ServerVoiced"/>, everything else, including legacy chat and a player message with no verification, becomes <see cref="ChatStanding.None"/>), and <see cref="ChatStandingRule.IsHidable"/> is true for exactly one standing.</summary>
public sealed class ChatStandingRuleTests
{
    private static ChatMessageReceived Message(ChatCategory category, ChatVerification verification) =>
        new(Component.Text("hello"), category, SenderName: null, SenderId: null, IsOverlay: false, verification);

    [Theory]
    [InlineData(ChatVerification.Verified, ChatStanding.Verified)]
    [InlineData(ChatVerification.Failed, ChatStanding.Rejected)]
    [InlineData(ChatVerification.Unverified, ChatStanding.Unverified)]
    [InlineData(ChatVerification.Insecure, ChatStanding.Insecure)]
    public void Classify_MapsEachVerificationToItsStanding(ChatVerification verification, ChatStanding expected)
    {
        // Category is irrelevant for every verification except NotApplicable.
        Assert.Equal(expected, ChatStandingRule.Classify(verification, ChatCategory.Player));
    }

    [Theory]
    [InlineData(ChatCategory.System)]
    [InlineData(ChatCategory.Disguised)]
    public void Classify_NotApplicable_SystemOrDisguised_IsServerVoiced(ChatCategory category)
    {
        Assert.Equal(
            ChatStanding.ServerVoiced,
            ChatStandingRule.Classify(ChatVerification.NotApplicable, category));
    }

    [Fact]
    public void Classify_NotApplicable_LegacyChat_IsNone()
    {
        // A pre-1.19 player message: legacy chat, position-tagged, carrying no signature by design.
        Assert.Equal(
            ChatStanding.None,
            ChatStandingRule.Classify(ChatVerification.NotApplicable, ChatCategory.Legacy));
    }

    [Fact]
    public void Classify_NotApplicable_PlayerCategory_IsNone()
    {
        Assert.Equal(
            ChatStanding.None,
            ChatStandingRule.Classify(ChatVerification.NotApplicable, ChatCategory.Player));
    }

    [Theory]
    [InlineData(ChatStanding.None, false)]
    [InlineData(ChatStanding.Verified, false)]
    [InlineData(ChatStanding.Rejected, true)]
    [InlineData(ChatStanding.Unverified, false)]
    [InlineData(ChatStanding.Insecure, false)]
    [InlineData(ChatStanding.ServerVoiced, false)]
    public void IsHidable_IsTrueOnlyForRejected(ChatStanding standing, bool expected)
    {
        Assert.Equal(expected, ChatStandingRule.IsHidable(standing));
    }

    [Theory]
    [InlineData(ChatCategory.Legacy, ChatVerification.NotApplicable)]
    [InlineData(ChatCategory.Player, ChatVerification.Verified)]
    [InlineData(ChatCategory.Player, ChatVerification.Failed)]
    [InlineData(ChatCategory.Player, ChatVerification.Unverified)]
    [InlineData(ChatCategory.Player, ChatVerification.Insecure)]
    [InlineData(ChatCategory.System, ChatVerification.NotApplicable)]
    [InlineData(ChatCategory.Disguised, ChatVerification.NotApplicable)]
    public void Classify_MessageOverload_AgreesWithTheEnumOverload(ChatCategory category, ChatVerification verification)
    {
        ChatMessageReceived message = Message(category, verification);
        Assert.Equal(ChatStandingRule.Classify(verification, category), ChatStandingRule.Classify(message));
    }
}
