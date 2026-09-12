using Umpk.Geometry;

namespace Umpk.Client.Events;

/// <summary>Raised when a boss bar is added, updated, or removed.</summary>
public sealed record BossBarChanged(Guid Id) : IClientEvent;

/// <summary>Raised when the tab list header/footer changes.</summary>
public sealed record TabListHeaderFooterChanged : IClientEvent;

/// <summary>Raised when a scoreboard objective or score changes.</summary>
public sealed record ScoreboardChanged : IClientEvent;

/// <summary>Raised when a team is added, updated, or removed.</summary>
public sealed record TeamChanged(string Name) : IClientEvent;

/// <summary>Raised when the server opens the sign editor at a position.</summary>
public sealed record SignEditorOpened(BlockPos Position, bool IsFrontText) : IClientEvent;

/// <summary>Raised when advancements are updated.</summary>
public sealed record AdvancementsChanged : IClientEvent;

/// <summary>Raised when map item data arrives.</summary>
public sealed record MapDataReceived(int MapId) : IClientEvent;

/// <summary>Raised for a per-item cooldown update.</summary>
public sealed record ItemCooldownChanged(int ItemId, int CooldownTicks) : IClientEvent;

/// <summary>Raised when the server shows a dialog (1.21.6+). Read the body from <c>ClientState.Dialogs</c>; <paramref name="RegistryId"/> is set instead of a parsed body when the server sent a dialog-registry reference.</summary>
/// <param name="RegistryId">The dialog registry index, when the server sent a reference.</param>
public sealed record DialogShown(int? RegistryId) : IClientEvent;

/// <summary>Raised when the shown dialog is cleared (1.21.6+), by the server or by session teardown.</summary>
public sealed record DialogCleared : IClientEvent;
