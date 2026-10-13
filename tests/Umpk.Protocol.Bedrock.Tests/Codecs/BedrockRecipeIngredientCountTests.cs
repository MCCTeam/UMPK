using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockRecipeIngredientCountTests
{
    // Synthetic schema-derived vectors: lu16 Count belongs to every RecipeIngredient2 branch, before the next action.
    private const string Header = "0105020B0DAC020101";
    private const string Tail = "101000FFFFFFFF";

    [Fact]
    public void NameCount258LeavesFollowingActionIntact()
    {
        byte[] bytes = Convert.FromHexString("0105020B0DAC02010101010F6D696E6563726166743A737469636B010201101000FFFFFFFF");

        BedrockItemStackRequestPacket packet = BedrockItemStackCodecs.DecodeRequest(bytes);

        BedrockItemStackRequest request = Assert.Single(packet.Requests);
        Assert.Equal(-3, request.RequestId);
        Assert.Equal(-1, request.Cause);
        Assert.Empty(request.CustomNames);
        Assert.Equal(2, request.Actions.Length);
        BedrockCraftRecipeAutoPayload auto = Assert.IsType<BedrockCraftRecipeAutoPayload>(request.Actions[0].Payload);
        Assert.Equal(300, auto.RecipeNetworkId);
        Assert.Equal(1, auto.TimesCrafted);
        BedrockRecipeIngredient ingredient = Assert.Single(auto.Ingredients);
        Assert.Equal(BedrockRecipeIngredientKind.Name, ingredient.Kind);
        Assert.Equal("minecraft:stick", ingredient.Name);
        Assert.Equal(-1, ingredient.Metadata);
        Assert.Equal((ushort)258, ingredient.Count);
        Assert.Equal(BedrockItemStackActionType.NonImplemented, request.Actions[1].Type);
        Assert.Equal(16, request.Actions[1].LegacyTypeId);
        Assert.Equal(bytes, BedrockItemStackCodecs.Encode(packet));
    }

    [Theory]
    [InlineData("00070000", 0, 0)]
    [InlineData("00070201", 0, 258)]
    [InlineData("0007FFFF", 0, 65535)]
    [InlineData("010702C3A9010000", 1, 0)]
    [InlineData("010702C3A9010201", 1, 258)]
    [InlineData("010702C3A901FFFF", 1, 65535)]
    [InlineData("02070178FEFF0000", 2, 0)]
    [InlineData("02070178FEFF0201", 2, 258)]
    [InlineData("02070178FEFFFFFF", 2, 65535)]
    [InlineData("030702C3A90000", 3, 0)]
    [InlineData("030702C3A90201", 3, 258)]
    [InlineData("030702C3A9FFFF", 3, 65535)]
    public void EveryDescriptorCarriesUnsignedLittleEndianCount(string descriptor, int kindValue, int expectedCount)
    {
        var kind = (BedrockRecipeIngredientKind)kindValue;
        byte[] bytes = Convert.FromHexString(Header + descriptor + Tail);

        BedrockItemStackRequestPacket packet = BedrockItemStackCodecs.DecodeRequest(bytes);

        BedrockCraftRecipeAutoPayload auto = Assert.IsType<BedrockCraftRecipeAutoPayload>(packet.Requests[0].Actions[0].Payload);
        BedrockRecipeIngredient ingredient = Assert.Single(auto.Ingredients);
        Assert.Equal(kind, ingredient.Kind);
        Assert.Equal(7, ingredient.LegacyType);
        Assert.Equal((ushort)expectedCount, ingredient.Count);
        switch (kind)
        {
            case BedrockRecipeIngredientKind.Name:
                Assert.Equal("é", ingredient.Name);
                Assert.Equal(-1, ingredient.Metadata);
                break;
            case BedrockRecipeIngredientKind.Molang:
                Assert.Equal("x", ingredient.Expression);
                Assert.Equal((short)-2, ingredient.Version);
                break;
            case BedrockRecipeIngredientKind.ItemTag:
                Assert.Equal("é", ingredient.Tag);
                break;
        }
        Assert.Equal(BedrockItemStackActionType.NonImplemented, packet.Requests[0].Actions[1].Type);
        Assert.Equal(bytes, BedrockItemStackCodecs.Encode(packet));
    }

    [Fact]
    public void UnknownCountCannotBeEncodedAsAnImplicitDefault()
    {
        var ingredient = new BedrockRecipeIngredient(BedrockRecipeIngredientKind.Name, 1, "minecraft:stick", -1, null, null, null);
        Assert.Null(ingredient.Count);
        var packet = new BedrockItemStackRequestPacket(
            [new BedrockItemStackRequest(-3, [new BedrockItemStackAction(BedrockItemStackActionType.CraftRecipeAuto, 13, new BedrockCraftRecipeAutoPayload(300, 1, [ingredient]))], [], -1)]);

        BedrockCodecException error = Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.Encode(packet));

        Assert.Contains("ingredient count is missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsecutiveIngredientsKeepSeparateCountsAndFollowingAction()
    {
        byte[] bytes = Convert.FromHexString("0105020B0DAC020102000700000107017301FFFF" + Tail);

        BedrockItemStackRequestPacket packet = BedrockItemStackCodecs.DecodeRequest(bytes);

        BedrockCraftRecipeAutoPayload auto = Assert.IsType<BedrockCraftRecipeAutoPayload>(packet.Requests[0].Actions[0].Payload);
        Assert.Equal(2, auto.Ingredients.Length);
        Assert.Equal(BedrockRecipeIngredientKind.Invalid, auto.Ingredients[0].Kind);
        Assert.Equal((ushort)0, auto.Ingredients[0].Count);
        Assert.Equal("s", auto.Ingredients[1].Name);
        Assert.Equal(-1, auto.Ingredients[1].Metadata);
        Assert.Equal(ushort.MaxValue, auto.Ingredients[1].Count);
        Assert.Equal(BedrockItemStackActionType.NonImplemented, packet.Requests[0].Actions[1].Type);
        Assert.Equal(bytes, BedrockItemStackCodecs.Encode(packet));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingCountBytesFailAtIngredientBoundary(int countBytes)
    {
        byte[] incomplete = Convert.FromHexString(Header + "0007" + (countBytes == 1 ? "02" : ""));

        BedrockCodecException error = Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.DecodeRequest(incomplete));

        Assert.Contains("Truncated input", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CountBearingPayloadRejectsEveryTruncationAndTrailingBytes()
    {
        byte[] bytes = Convert.FromHexString(Header + "02070178FEFF0201" + Tail);

        for (int length = 0; length < bytes.Length; length++)
            Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.DecodeRequest(bytes.AsSpan(0, length)));
        Assert.Throws<BedrockCodecException>(() => BedrockItemStackCodecs.DecodeRequest([.. bytes, 0]));
    }

    [Fact]
    public void EqualityDistinguishesUnknownZeroAndOtherKnownCounts()
    {
        var unknown = new BedrockRecipeIngredient(BedrockRecipeIngredientKind.Invalid, 7, null, null, null, null, null);
        var zero = new BedrockRecipeIngredient(BedrockRecipeIngredientKind.Invalid, 7, null, null, null, null, null, 0);
        var other = new BedrockRecipeIngredient(BedrockRecipeIngredientKind.Invalid, 7, null, null, null, null, null, 258);
        var equal = new BedrockRecipeIngredient(BedrockRecipeIngredientKind.Invalid, 7, null, null, null, null, null, 258);

        Assert.Null(unknown.Count);
        Assert.Equal((ushort)0, zero.Count);
        Assert.NotEqual(unknown, zero);
        Assert.NotEqual(zero, other);
        Assert.Equal(other, equal);
        Assert.Equal(other.GetHashCode(), equal.GetHashCode());
        Assert.Equal(3, new HashSet<BedrockRecipeIngredient> { unknown, zero, other, equal }.Count);
    }

    [Fact]
    public void EmptyIngredientArrayNeedsNoCountField()
    {
        byte[] bytes = Convert.FromHexString("0105020B0DAC020100" + Tail);

        BedrockItemStackRequestPacket packet = BedrockItemStackCodecs.DecodeRequest(bytes);

        BedrockCraftRecipeAutoPayload auto = Assert.IsType<BedrockCraftRecipeAutoPayload>(packet.Requests[0].Actions[0].Payload);
        Assert.Empty(auto.Ingredients);
        Assert.Equal(BedrockItemStackActionType.NonImplemented, packet.Requests[0].Actions[1].Type);
        Assert.Equal(bytes, BedrockItemStackCodecs.Encode(packet));
    }
}
