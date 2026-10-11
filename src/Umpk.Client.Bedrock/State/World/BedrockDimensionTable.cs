namespace Umpk.Client.Bedrock.State.World;

// Dimension heights follow the pinned dimension table with explicit minimum heights and section counts.
internal static class BedrockDimensionTable
{
    internal static bool TryGet(int dimension, out int minY, out int height)
    {
        switch (dimension)
        {
            case 0:
                minY = -64;
                height = 384;
                return true;
            case 1:
                minY = 0;
                height = 128;
                return true;
            case 2:
                minY = 0;
                height = 256;
                return true;
            default:
                minY = 0;
                height = 0;
                return false;
        }
    }

    internal static int MinSectionY(int minY) => minY >= 0 ? minY / 16 : (minY - 15) / 16;

    internal static int SectionCount(int height) => height / 16;
}
