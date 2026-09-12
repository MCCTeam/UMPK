namespace Umpk.Protocol.Java.Tests;

/// <summary>Maps a dataset codec-era key to a representative protocol number that carries it. Registration resolution is by protocol number, so a registration-wiring test that wants to exercise a given era must build its descriptor at a protocol where that era's packets exist. This is the first protocol each key labels.</summary>
internal static class CodecKeyProtocols
{
    private static readonly Dictionary<string, int> Map = new(StringComparer.Ordinal)
    {
        ["V1_8"] = 47,
        ["V1_9"] = 107,
        ["V1_9_2"] = 109,
        ["V1_9_4"] = 110,
        ["V1_12"] = 335,
        ["V1_12_2"] = 340,
        ["V1_13"] = 393,
        ["V1_13_2"] = 404,
        ["V1_14"] = 477,
        ["V1_14_4"] = 498,
        ["V1_15"] = 573,
        ["V1_16"] = 735,
        ["V1_16_2"] = 751,
        ["V1_17"] = 755,
        ["V1_17_1"] = 756,
        ["V1_18"] = 757,
        ["V1_19"] = 759,
        ["V1_19_1"] = 760,
        ["V1_19_3"] = 761,
        ["V1_19_4"] = 762,
        ["V1_20"] = 763,
        ["V1_20_2"] = 764,
        ["V1_20_3"] = 765,
        ["V1_20_5"] = 766,
        ["V1_21"] = 767,
        ["V1_21_2"] = 768,
        ["V1_21_4"] = 769,
        ["V1_21_5"] = 770,
        ["V1_21_6"] = 771,
        ["V1_21_7"] = 772,
        ["V1_21_9"] = 773,
        ["V1_21_11"] = 774,
        ["V26_1"] = 775,
        ["V26_2"] = 776,
    };

    /// <summary>The representative protocol number for a codec-era key.</summary>
    public static int Of(string key) => Map[key];
}
