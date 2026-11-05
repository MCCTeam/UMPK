using System.Reflection;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.IntegrationTests;

/// <summary>Read-only diagnostic snapshots for opt-in live legs; these observations never replace packet or native-server assertions.</summary>
internal static class VanillaInteractionAcceptanceDiagnostics
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static object Read(UmpkClient client, int protocol, BlockPos clicked, BlockPos destination)
    {
        ClientState state = client.State;
        IBlockInteractionSource data = JavaGameData.BlockInteractionData(protocol);
        ItemStack held = state.Inventory.GetSlot(36 + state.Self.HeldSlot);
        object membership = typeof(ClientState).GetProperty("EntityMembership", Fields)!.GetValue(state)!;
        object geometry = typeof(ClientState).GetProperty("EntityGeometry", Fields)!.GetValue(state)!;
        object? owner = typeof(UmpkClient).GetField("_physics", Fields)!.GetValue(client);
        var physics = (PhysicsState?)owner?.GetType().GetProperty("EngineState", Fields)!.GetValue(owner);
        return new
        {
            diagnosticOnly = true,
            state.Self.GameMode,
            state.Self.Sneaking,
            state.Self.EntityId,
            state.Self.Position,
            state.Self.OnGround,
            state.Self.HeldSlot,
            heldItem = held.Item.Id.ToString(),
            held.Count,
            held.Components.HasNoPatch,
            cooldown = state.Self.ItemCooldowns.GetValueOrDefault(held.Item.NetworkId),
            itemRule = data.TryGetItem(held.Item.Id, out ItemUseOnInfo use) ? use.ToString() : null,
            physicsBox = physics?.BoundingBox,
            clicked = Block(clicked),
            destination = Block(destination),
            entities = state.Entities.All.Select(entity => new
            {
                entity.Id,
                type = entity.Type.Id.ToString(),
                entity.Position,
                entity.Pose,
                vehicle = entity.Vehicle?.Id,
                nonObstructing = data.IsAlwaysNonObstructingEntity(entity.Type.Id),
                member = membership.GetType().GetMethod("IsWorldQueryMember", Fields)!.Invoke(membership, [entity]),
                fixedDimensions = data.TryGetFixedEntityDimensions(entity.Type.Id, out float width, out float height) ? new { width, height } : null,
                // Calling EntityGeometryState.TryGet would refresh state. Read the stored tables instead.
                storedGeometry = new[] { "_entities", "_legacyPlayers", "_shulkers" }.ToDictionary(name => name, name => TrackedValue(geometry, name, entity)),
            }).ToArray(),
        };

        object Block(BlockPos position)
        {
            var block = state.World.GetBlock(position);
            var lower = state.World.GetBlock(position.Below());
            return new
            {
                position,
                columnLoaded = state.World.GetColumn(position) is not null,
                block.IsValid,
                block.IsAir,
                block.StateId,
                id = block.Block.Id.ToString(),
                rule = data.TryGetBlock(block.Block.Id, out BlockInteractionInfo info) ? info.ToString() : null,
                replacement = data.TryGetCubeReplacement(block.StateId, lower.IsValid ? lower.StateId : null, out BlockReplacementInfo replacement) ? replacement.ToString() : null,
            };
        }
    }

    private static object? TrackedValue(object geometry, string field, object entity)
    {
        object table = geometry.GetType().GetField(field, Fields)!.GetValue(geometry)!;
        object?[] args = [entity, null];
        bool found = (bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!;
        return found ? SnapshotFields(args[1]!, 3) : null;
    }

    private static object? SnapshotFields(object? value, int depth)
    {
        if (value is null)
            return null;
        Type type = value.GetType();
        if (type.IsPrimitive || value is string or decimal || type.IsEnum)
            return value;
        if (value is Identifier or Vec3d or Aabb)
            return value.ToString();
        if (depth == 0 || type.Namespace is null || !type.Namespace.StartsWith("Umpk.", StringComparison.Ordinal))
            return type.FullName;
        return type.GetFields(Fields).ToDictionary(field => field.Name, field => SnapshotFields(field.GetValue(value), depth - 1));
    }
}
