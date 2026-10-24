namespace Umpk.Client.Movement;

/// <summary>Owns native local-player tick eligibility and the transition to usable local movement.</summary>
internal sealed class MovementReadiness
{
    private const int UnconditionalPlayerTickEra = 768;
    private const int PlayerLoadedIntroduced = 769;
    private const int PlayerLoadFallbackLastEra = 773;

    private int _playerTicks;
    private bool _ready;
    private bool _announcementFinished;

    internal bool PlayerTickEligible { get; private set; }

    public void Reset()
    {
        _playerTicks = 0;
        _ready = false;
        _announcementFinished = false;
        PlayerTickEligible = false;
    }

    /// <summary>Advances once per native local-player lifetime tick, including ticks before its initial position packet. Protocol 768 ticks unconditionally; 769–773 have the native 60-tick fallback; later load-tracker clients retain readiness after usable terrain, while older clients consult the current native column.</summary>
    public ReadinessTick Advance(int protocol, bool placed, bool localColumnLoaded, bool? playerExists = null)
    {
        if (!(playerExists ?? placed))
        {
            PlayerTickEligible = false;
            return new ReadinessTick(false, false);
        }

        _playerTicks++;
        if (!_ready && localColumnLoaded)
            _ready = true;

        if (protocol is >= PlayerLoadedIntroduced and <= PlayerLoadFallbackLastEra && !_ready && _playerTicks >= 60)
        {
            _ready = true;
            _announcementFinished = true;
        }

        bool announce = placed && protocol >= PlayerLoadedIntroduced && _ready && !_announcementFinished;
        if (announce)
            _announcementFinished = true;

        PlayerTickEligible = protocol >= PlayerLoadedIntroduced
            ? _ready
            : protocol == UnconditionalPlayerTickEra || localColumnLoaded;

        return new ReadinessTick(placed && PlayerTickEligible, announce);
    }
}

internal readonly record struct ReadinessTick(bool MovementEligible, bool AnnouncePlayerLoaded);
