using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Umpk.Client.Bedrock.Authentication;

public sealed class BedrockDefaultSkinTests
{
    [Fact]
    public void DefaultSkinMatchesPinnedSteveProfile()
    {
        using JsonDocument steve = JsonDocument.Parse(File.ReadAllBytes(FindSteveJson()));
        BedrockClientSkinProfile profile = BedrockDefaultSkin.Profile;

        Assert.Equal(GetString(steve, "SkinId"), profile.SkinId);
        Assert.Equal(GetString(steve, "SkinData"), profile.SkinData);
        Assert.Equal(int.Parse(GetString(steve, "SkinImageWidth"), CultureInfo.InvariantCulture), profile.SkinImageWidth);
        Assert.Equal(int.Parse(GetString(steve, "SkinImageHeight"), CultureInfo.InvariantCulture), profile.SkinImageHeight);
        Assert.Equal(GetString(steve, "SkinResourcePatch"), profile.SkinResourcePatch);
        Assert.Equal(GetString(steve, "SkinGeometryData"), profile.SkinGeometryData);
        Assert.Equal(GetString(steve, "SkinGeometryDataEngineVersion"), profile.SkinGeometryDataEngineVersion);
        Assert.Equal(GetString(steve, "CapeData"), profile.CapeData);
        Assert.Equal(int.Parse(GetString(steve, "CapeImageWidth"), CultureInfo.InvariantCulture), profile.CapeImageWidth);
        Assert.Equal(int.Parse(GetString(steve, "CapeImageHeight"), CultureInfo.InvariantCulture), profile.CapeImageHeight);
        Assert.Equal(bool.Parse(GetString(steve, "CapeOnClassicSkin")), profile.CapeOnClassicSkin);
        Assert.Equal(bool.Parse(GetString(steve, "PersonaSkin")), profile.PersonaSkin);
        Assert.Equal(bool.Parse(GetString(steve, "PremiumSkin")), profile.PremiumSkin);
        Assert.Equal(GetString(steve, "ArmSize"), profile.ArmSize);
        Assert.Equal(GetString(steve, "CapeId"), profile.CapeId);
        Assert.Equal(GetString(steve, "SkinAnimationData"), profile.SkinAnimationData);
        Assert.Equal(GetString(steve, "SkinColor"), profile.SkinColor);
        AssertAnimatedImageData(steve, profile);
        AssertPersonaPieces(steve, profile);
        AssertPieceTintColors(steve, profile);
    }

    [Fact]
    public void DefaultSkinSourceHashMatchesPinnedInput()
    {
        byte[] bytes = File.ReadAllBytes(FindSteveJson());

        Assert.Equal(BedrockDefaultSkin.SourceSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    [Fact]
    public void DefaultSkinImageMatchesItsDimensions()
    {
        BedrockClientSkinProfile profile = BedrockDefaultSkin.Profile;

        Assert.Equal(profile.SkinImageWidth * profile.SkinImageHeight * 4, Convert.FromBase64String(profile.SkinData).Length);
    }

    private static void AssertAnimatedImageData(JsonDocument steve, BedrockClientSkinProfile profile)
    {
        JsonElement.ArrayEnumerator animations = steve.RootElement.GetProperty("AnimatedImageData").EnumerateArray();
        var expected = new List<JsonElement>();
        foreach (JsonElement animation in animations)
            expected.Add(animation);
        Assert.Equal(expected.Count, profile.AnimatedImageData.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].GetProperty("AnimationExpression").GetInt32(), profile.AnimatedImageData[index].AnimationExpression);
            Assert.Equal(expected[index].GetProperty("Frames").GetInt32(), profile.AnimatedImageData[index].Frames);
            Assert.Equal(expected[index].GetProperty("Image").GetString(), profile.AnimatedImageData[index].Image);
            Assert.Equal(expected[index].GetProperty("ImageHeight").GetInt32(), profile.AnimatedImageData[index].ImageHeight);
            Assert.Equal(expected[index].GetProperty("ImageWidth").GetInt32(), profile.AnimatedImageData[index].ImageWidth);
            Assert.Equal(expected[index].GetProperty("Type").GetInt32(), profile.AnimatedImageData[index].Type);
        }
    }

    private static void AssertPersonaPieces(JsonDocument steve, BedrockClientSkinProfile profile)
    {
        JsonElement.ArrayEnumerator pieces = steve.RootElement.GetProperty("PersonaPieces").EnumerateArray();
        var expected = new List<JsonElement>();
        foreach (JsonElement piece in pieces)
            expected.Add(piece);
        Assert.Equal(expected.Count, profile.PersonaPieces.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].GetProperty("IsDefault").GetBoolean(), profile.PersonaPieces[index].IsDefault);
            Assert.Equal(expected[index].GetProperty("PackId").GetString(), profile.PersonaPieces[index].PackId);
            Assert.Equal(expected[index].GetProperty("PieceId").GetString(), profile.PersonaPieces[index].PieceId);
            Assert.Equal(expected[index].GetProperty("PieceType").GetString(), profile.PersonaPieces[index].PieceType);
            Assert.Equal(expected[index].GetProperty("ProductId").GetString(), profile.PersonaPieces[index].ProductId);
        }
    }

    private static void AssertPieceTintColors(JsonDocument steve, BedrockClientSkinProfile profile)
    {
        JsonElement.ArrayEnumerator tints = steve.RootElement.GetProperty("PieceTintColors").EnumerateArray();
        var expected = new List<JsonElement>();
        foreach (JsonElement tint in tints)
            expected.Add(tint);
        Assert.Equal(expected.Count, profile.PieceTintColors.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            var colors = new List<string>();
            foreach (JsonElement color in expected[index].GetProperty("Colors").EnumerateArray())
                colors.Add(color.GetString()!);
            Assert.Equal(colors, profile.PieceTintColors[index].Colors);
            Assert.Equal(expected[index].GetProperty("PieceType").GetString(), profile.PieceTintColors[index].PieceType);
        }
    }

    private static string FindSteveJson()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 10 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory, "data", "bedrock", "2169", "imported", "steve.json");
            if (File.Exists(candidate))
                return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("The pinned steve.json input was not found above the test directory.");
    }

    private static string GetString(JsonDocument document, string name)
    {
        if (!document.RootElement.TryGetProperty(name, out JsonElement element))
            throw new InvalidDataException($"The pinned skin field '{name}' is missing.");
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? throw new InvalidDataException($"The pinned skin field '{name}' is null."),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => throw new InvalidDataException($"The pinned skin field '{name}' has an unsupported JSON kind."),
        };
    }
}
