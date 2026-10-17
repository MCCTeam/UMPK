using Umpk.Client.State;
using Umpk.Game.Players;

namespace Umpk.Client.Actions;

/// <summary>Client-side block input boundaries established by the matching vanilla client artifacts.</summary>
internal readonly record struct BlockInteractionProfile(bool InitialSwingBeforeStart, bool InstantBreakUsesAbilities, bool ExponentialMiningFatigue, bool HasDestroyDirectionAction, bool ItemToolComponents, bool UseSwingIsLocal, bool VoxelPlacementObstruction, bool DiscardThinObstructionShapes, bool SpectatorUseSwings, bool ChargedPiercingAttack, bool TeleportStopsDestroying)
{
    private const int OffHandIntroduced = 107;
    private const int InstantBuildCriterionIntroduced = 770;
    private const int ExponentialMiningFatigueIntroduced = 777;
    private const int DestroyDirectionActionIntroduced = 777;
    private const int ItemToolComponentIntroduced = 766;
    private const int LocalPredictedUseSwingIntroduced = 777;
    private const int VoxelPlacementObstructionIntroduced = 393;
    private const int EmptyThinShapesIntroduced = 755;
    private const int SpectatorConsumeResultIntroduced = 768;
    private const int ChargedPiercingAttackIntroduced = 774;
    private const int TeleportStopsDestroyingIntroduced = 777;

    public static BlockInteractionProfile ForProtocol(int protocol)
        => new(protocol < OffHandIntroduced, protocol >= InstantBuildCriterionIntroduced, protocol >= ExponentialMiningFatigueIntroduced, protocol >= DestroyDirectionActionIntroduced, protocol >= ItemToolComponentIntroduced, protocol >= LocalPredictedUseSwingIntroduced, protocol >= VoxelPlacementObstructionIntroduced, protocol >= EmptyThinShapesIntroduced, protocol < SpectatorConsumeResultIntroduced, protocol >= ChargedPiercingAttackIntroduced, protocol >= TeleportStopsDestroyingIntroduced);

    // 26.3 inserts CHANGE_DESTROY_DIRECTION after START; every following wire enum ordinal moves by one. The packet's public Action remains the actual wire ordinal.
    public int AbortDestroyAction => HasDestroyDirectionAction ? 2 : 1;
    public int StopDestroyAction => HasDestroyDirectionAction ? 3 : 2;
    public int StabAction => HasDestroyDirectionAction ? 8 : 7;

    public bool CanBreakInstantly(SelfState player)
        => InstantBreakUsesAbilities ? player.InstantBuild : player.GameMode == GameMode.Creative;
}
