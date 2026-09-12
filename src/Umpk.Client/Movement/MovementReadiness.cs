namespace Umpk.Client.Movement;

/// <summary>Owns the per-world transition from placement to usable local movement.</summary>
internal sealed class MovementReadiness
{
    private const int PlayerLoadedIntroduced = 769;
    private const int PlayerLoadFallbackEra = 770;
    private const int LevelLoadTrackerEra = 771;

    private int _placedTicks;
    private bool _ready;
    private bool _announcementFinished;

    public void Reset()
    {
        _placedTicks = 0;
        _ready = false;
        _announcementFinished = false;
    }

    /// <summary>Advances readiness once for a placed-player tick. The 1.21.5 client has a specific 60-tick fallback which releases movement without sending a late player-loaded packet. Newer load-tracker clients keep readiness once the initial usable column has arrived; older clients continue to consult the current column on every tick.</summary>
    public ReadinessTick Advance(int protocol, bool placed, bool localColumnLoaded)
    {
        if (!placed)
            return new ReadinessTick(false, false);

        _placedTicks++;

        if (!_ready && localColumnLoaded)
            _ready = true;

        if (protocol == PlayerLoadFallbackEra && !_ready && _placedTicks >= 60)
        {
            _ready = true;
            _announcementFinished = true;
        }

        bool announce = protocol >= PlayerLoadedIntroduced && _ready && !_announcementFinished;
        if (announce)
            _announcementFinished = true;

        bool movementEligible = protocol >= LevelLoadTrackerEra
            ? _ready
            : protocol == PlayerLoadFallbackEra
                ? _ready
                : localColumnLoaded;

        return new ReadinessTick(movementEligible, announce);
    }
}

internal readonly record struct ReadinessTick(bool MovementEligible, bool AnnouncePlayerLoaded);
