using Umpk.Physics.Tests.Fixtures;

namespace Umpk.Physics.Tests.Parity;

/// <summary>A fixture voxel world reproducing the course sections that exercise physics (flat sprint, stair ascend, slime, ladder, crawl tunnel, water channel, goal) mapped onto the fixture <see cref="BlockKind"/> set. Origin matches the script: OX=26, OY=105, OZ=127.</summary>
public static class Pr3076Course
{
    public const int OX = 26;
    public const int OY = 105;
    public const int OZ = 127;
    public const int Z1 = OZ - 1; // 126
    public const int Z2 = OZ + 1; // 128

    /// <summary>Builds the full course world.</summary>
    public static FixtureWorld Build()
    {
        var w = new FixtureWorld();

        // S0 start platform + S1 flat sprint (glass/smooth_stone floor at OY).
        Floor(w, 12, 31, OY, BlockKind.Stone);
        Barrier(w, 12, 31, OY, OY + 2);

        // S2 stair ascend: 3 steps y=OY+1..OY+3.
        for (int i = 0; i < 3; i++)
        {
            int xs = 32 + i * 2, xe = xs + 1;
            int y = OY + 1 + i;
            Floor(w, xs, xe, y, BlockKind.Stone);
        }

        Floor(w, 38, 40, OY + 3, BlockKind.Stone);

        // S6 slime section: walk zone + bounce pit.
        Floor(w, 89, 90, OY, BlockKind.SlimeBlock);
        Floor(w, 91, 92, OY - 2, BlockKind.SlimeBlock); // pit bottom slime (bounce)
        Floor(w, 93, 94, OY, BlockKind.Stone);          // landing after bounce

        // S7 ladder climb: wall + ladder column at Z1.
        for (int ly = OY + 1; ly <= OY + 6; ly++)
        {
            w.Set(95, ly, Z1 - 1, BlockKind.Stone); // wall behind
            w.Set(95, ly, Z1, BlockKind.Ladder);
            w.Set(96, ly, Z1, BlockKind.Ladder);
            w.Set(97, ly, Z1, BlockKind.Ladder);
        }

        Floor(w, 95, 97, OY + 7, BlockKind.Stone); // landing at top

        // S8 crawl tunnel: floor + ceiling 1 block apart at y=OY+7.
        Floor(w, 102, 111, OY + 7, BlockKind.Glass);
        Floor(w, 102, 111, OY + 9, BlockKind.Glass); // ceiling leaves 1-block gap (feet OY+8)

        // S9 water channel: floor at OY-3, water fill OY-2..OY-1.
        Fill(w, 113, OY - 3, 119, OY - 3, BlockKind.Stone);
        Fill(w, 113, OY - 2, 119, OY - 1, BlockKind.Water);
        Floor(w, 121, 124, OY, BlockKind.Stone); // exit step up

        // S10 goal platform.
        Floor(w, 125, 129, OY, BlockKind.Stone);

        return w;
    }

    private static void Floor(FixtureWorld w, int x1, int x2, int y, BlockKind kind) =>
        w.Floor(x1, x2, Z1, Z2, y, kind);

    private static void Fill(FixtureWorld w, int x1, int y1, int x2, int y2, BlockKind kind) =>
        w.Fill(x1, y1, Z1, x2, y2, Z2, kind);

    private static void Barrier(FixtureWorld w, int x1, int x2, int y1, int y2)
    {
        w.Fill(x1, y1, Z1 - 1, x2, y2, Z1 - 1, BlockKind.Stone);
        w.Fill(x1, y1, Z2 + 1, x2, y2, Z2 + 1, BlockKind.Stone);
    }
}
