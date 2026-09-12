namespace Umpk.Client.Events;

/// <summary>Raised when a container window opens.</summary>
public sealed record ContainerOpened(int WindowId) : IClientEvent;

/// <summary>Raised when a container window closes.</summary>
public sealed record ContainerClosed(int WindowId) : IClientEvent;

/// <summary>Raised when a container's contents change (set-slot or full-content sync).</summary>
public sealed record ContainerContentChanged(int WindowId) : IClientEvent;

/// <summary>Raised when a click prediction is contradicted by an authoritative server packet and rolled back.</summary>
public sealed record PredictionCorrected(int WindowId, int StateId) : IClientEvent;

/// <summary>Raised when merchant/villager trade offers arrive.</summary>
public sealed record TradeOffersReceived(int WindowId) : IClientEvent;

/// <summary>Raised when a container property (furnace progress, enchant levels, ...) changes.</summary>
public sealed record ContainerPropertyChanged(int WindowId, int PropertyId, int Value) : IClientEvent;

/// <summary>Raised when the recipe registry is updated.</summary>
public sealed record RecipesUpdated(int Revision) : IClientEvent;

/// <summary>Raised when the recipe book changes: an unlock init/add/remove on any era, or a settings update.</summary>
/// <param name="UnlockedCount">How many recipes the unlocked sets now name. On 1.21.2+ this excludes additions whose entry payload stayed opaque; see <see cref="Umpk.Client.State.RecipeState.OpaqueAdditions"/>.</param>
/// <param name="Revision">The number of recipe-book changes applied so far.</param>
public sealed record RecipeBookChanged(int UnlockedCount, int Revision) : IClientEvent;
