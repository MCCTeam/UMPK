using Umpk.BedrockDataGen.Schema;

namespace Umpk.BedrockDataGen;

internal sealed record ConcreteCodecBinding(
    string Key,
    int PacketId,
    string PacketName,
    string Direction,
    string RegistryGeneration,
    string AdapterName,
    string DecodeInvocation);

internal static class ConcreteCodecBindings
{
    private static readonly IReadOnlyDictionary<string, ConcreteCodecBinding> s_bindings =
        new Dictionary<string, ConcreteCodecBinding>(StringComparer.Ordinal)
        {
            ["bootstrap.login"] = new("bootstrap.login", 1, "login", "serverbound", "none", "DecodeLogin", "BedrockBootstrapCodecs.DecodeLogin(payload)"),
            ["bootstrap.play-status"] = new("bootstrap.play-status", 2, "play_status", "clientbound", "none", "DecodePlayStatus", "BedrockBootstrapCodecs.DecodePlayStatus(payload)"),
            ["bootstrap.server-to-client-handshake"] = new("bootstrap.server-to-client-handshake", 3, "server_to_client_handshake", "clientbound", "none", "DecodeServerToClientHandshake", "BedrockBootstrapCodecs.DecodeServerToClientHandshake(payload)"),
            ["bootstrap.client-to-server-handshake"] = new("bootstrap.client-to-server-handshake", 4, "client_to_server_handshake", "serverbound", "none", "DecodeClientToServerHandshake", "BedrockBootstrapCodecs.DecodeClientToServerHandshake(payload)"),
            ["bootstrap.disconnect"] = new("bootstrap.disconnect", 5, "disconnect", "clientbound", "none", "DecodeDisconnect", "BedrockBootstrapCodecs.DecodeDisconnect(payload)"),
            ["join.resource-packs-info"] = new("join.resource-packs-info", 6, "resource_packs_info", "clientbound", "none", "DecodeResourcePacksInfo", "BedrockJoinCodecs.DecodeResourcePacksInfo(payload)"),
            ["join.resource-pack-stack"] = new("join.resource-pack-stack", 7, "resource_pack_stack", "clientbound", "none", "DecodeResourcePackStack", "BedrockJoinCodecs.DecodeResourcePackStack(payload)"),
            ["join.resource-pack-response"] = new("join.resource-pack-response", 8, "resource_pack_client_response", "serverbound", "none", "DecodeResourcePackClientResponse", "BedrockJoinCodecs.DecodeResourcePackClientResponse(payload)"),
            ["join.start-game"] = new("join.start-game", 11, "start_game", "clientbound", "none", "DecodeStartGame", "BedrockJoinCodecs.DecodeStartGame(payload)"),
            ["join.entity-identifiers"] = new("join.entity-identifiers", 119, "available_entity_identifiers", "clientbound", "entity-identifiers", "DecodeAvailableEntityIdentifiers", "BedrockJoinCodecs.DecodeAvailableEntityIdentifiers(payload)"),
            ["join.biome-list"] = new("join.biome-list", 122, "biome_definition_list", "clientbound", "none", "DecodeBiomeDefinitionList", "BedrockJoinCodecs.DecodeBiomeDefinitionList(payload)"),
            ["join.violation-warning"] = new("join.violation-warning", 156, "packet_violation_warning", "clientbound", "none", "DecodePacketViolationWarning", "BedrockJoinCodecs.DecodePacketViolationWarning(payload)"),
            ["world.level-chunk"] = new("world.level-chunk", 58, "level_chunk", "clientbound", "none", "DecodeLevelChunk", "BedrockWorldCodecs.DecodeLevelChunk(payload, context.Limits)"),
            ["world.update-block"] = new("world.update-block", 21, "update_block", "clientbound", "none", "DecodeUpdateBlock", "BedrockUpdateBlockCodecs.DecodeUpdateBlock(payload, context.Limits)"),
            ["world.update-subchunk-blocks"] = new("world.update-subchunk-blocks", 172, "update_subchunk_blocks", "clientbound", "none", "DecodeUpdateSubChunkBlocks", "BedrockUpdateSubChunkBlocksCodecs.Decode(payload, context.Limits)"),
            ["world.subchunk"] = new("world.subchunk", 174, "subchunk", "clientbound", "none", "DecodeSubChunk", "BedrockSubChunkCodecs.DecodeResponse(payload, context.Limits)"),
            ["world.subchunk-request"] = new("world.subchunk-request", 175, "subchunk_request", "serverbound", "none", "DecodeSubChunkRequest", "BedrockSubChunkCodecs.DecodeRequest(payload, context.Limits)"),
            ["bootstrap.request-chunk-radius"] = new("bootstrap.request-chunk-radius", 69, "request_chunk_radius", "serverbound", "none", "DecodeRequestChunkRadius", "BedrockBootstrapCodecs.DecodeRequestChunkRadius(payload)"),
            ["bootstrap.chunk-radius-update"] = new("bootstrap.chunk-radius-update", 70, "chunk_radius_update", "clientbound", "none", "DecodeChunkRadiusUpdate", "BedrockBootstrapCodecs.DecodeChunkRadiusUpdate(payload)"),
            ["bootstrap.set-local-player-as-initialized"] = new("bootstrap.set-local-player-as-initialized", 113, "set_local_player_as_initialized", "serverbound", "none", "DecodeSetLocalPlayerAsInitialized", "BedrockBootstrapCodecs.DecodeSetLocalPlayerAsInitialized(payload)"),
            ["bootstrap.client-cache-status"] = new("bootstrap.client-cache-status", 129, "client_cache_status", "serverbound", "none", "DecodeClientCacheStatus", "BedrockBootstrapCodecs.DecodeClientCacheStatus(payload)"),
            ["bootstrap.network-settings"] = new("bootstrap.network-settings", 143, "network_settings", "clientbound", "none", "DecodeNetworkSettings", "BedrockBootstrapCodecs.DecodeNetworkSettings(payload)"),
            ["registry.item"] = new("registry.item", 162, "item_registry", "clientbound", "item-registry", "DecodeItemRegistry", "BedrockItemRegistryCodec.Decode(payload, context.Limits)"),
            ["bootstrap.request-network-settings"] = new("bootstrap.request-network-settings", 193, "request_network_settings", "serverbound", "none", "DecodeRequestNetworkSettings", "BedrockBootstrapCodecs.DecodeRequestNetworkSettings(payload)"),
            ["actor.add-player"] = new("actor.add-player", 12, "add_player", "clientbound", "none", "DecodeAddPlayer", "BedrockActorCodecs.DecodeAddPlayer(payload, context.Limits)"),
            ["actor.add-entity"] = new("actor.add-entity", 13, "add_entity", "clientbound", "none", "DecodeAddEntity", "BedrockActorCodecs.DecodeAddEntity(payload, context.Limits)"),
            ["actor.remove-entity"] = new("actor.remove-entity", 14, "remove_entity", "clientbound", "none", "DecodeRemoveEntity", "BedrockActorCodecs.DecodeRemoveEntity(payload)"),
            ["actor.add-item-entity"] = new("actor.add-item-entity", 15, "add_item_entity", "clientbound", "none", "DecodeAddItemEntity", "BedrockActorCodecs.DecodeAddItemEntity(payload, context.Limits)"),
            ["actor.move-player"] = new("actor.move-player", 19, "move_player", "bidirectional", "none", "DecodeMovePlayer", "BedrockActorCodecs.DecodeMovePlayer(payload, context.Limits)"),
            ["actor.set-entity-data"] = new("actor.set-entity-data", 39, "set_entity_data", "bidirectional", "none", "DecodeSetEntityData", "BedrockActorCodecs.DecodeSetEntityData(payload, context.Limits)"),
            ["actor.set-entity-motion"] = new("actor.set-entity-motion", 40, "set_entity_motion", "bidirectional", "none", "DecodeSetEntityMotion", "BedrockActorCodecs.DecodeSetEntityMotion(payload)"),
            ["actor.mob-effect"] = new("actor.mob-effect", 28, "mob_effect", "clientbound", "none", "DecodeMobEffect", "BedrockObservationCodecs.DecodeMobEffect(payload, context.Limits)"),
            ["actor.update-attributes"] = new("actor.update-attributes", 29, "update_attributes", "clientbound", "none", "DecodeUpdateAttributes", "BedrockObservationCodecs.DecodeUpdateAttributes(payload, context.Limits)"),
            ["actor.mob-equipment"] = new("actor.mob-equipment", 31, "mob_equipment", "bidirectional", "none", "DecodeMobEquipment", "BedrockObservationCodecs.DecodeMobEquipment(payload, context.Limits)"),
            ["actor.mob-armor-equipment"] = new("actor.mob-armor-equipment", 32, "mob_armor_equipment", "clientbound", "none", "DecodeMobArmorEquipment", "BedrockObservationCodecs.DecodeMobArmorEquipment(payload, context.Limits)"),
            ["actor.set-entity-link"] = new("actor.set-entity-link", 41, "set_entity_link", "clientbound", "none", "DecodeSetEntityLink", "BedrockObservationCodecs.DecodeSetEntityLink(payload, context.Limits)"),
            ["actor.player-location"] = new("actor.player-location", 326, "player_location", "clientbound", "none", "DecodePlayerLocation", "BedrockActorCodecs.DecodePlayerLocation(payload)"),
            ["inventory.content"] = new("inventory.content", 49, "inventory_content", "bidirectional", "none", "DecodeInventoryContent", "BedrockInventoryCodecs.DecodeContent(payload, context.Limits)"),
            ["inventory.slot"] = new("inventory.slot", 50, "inventory_slot", "bidirectional", "none", "DecodeInventorySlot", "BedrockInventoryCodecs.DecodeSlot(payload, context.Limits)"),
            ["inventory.container-open"] = new("inventory.container-open", 46, "container_open", "clientbound", "none", "DecodeContainerOpen", "BedrockInventoryCodecs.DecodeContainerOpen(payload, context.Limits)"),
            ["inventory.container-close"] = new("inventory.container-close", 47, "container_close", "bidirectional", "none", "DecodeContainerClose", "BedrockInventoryCodecs.DecodeContainerClose(payload, context.Limits)"),
            ["inventory.player-hotbar"] = new("inventory.player-hotbar", 48, "player_hotbar", "bidirectional", "none", "DecodePlayerHotbar", "BedrockInventoryCodecs.DecodePlayerHotbar(payload, context.Limits)"),
            ["inventory.item-stack-request"] = new("inventory.item-stack-request", 147, "item_stack_request", "serverbound", "none", "DecodeItemStackRequest", "BedrockItemStackCodecs.DecodeRequest(payload, context.Limits)"),
            ["inventory.item-stack-response"] = new("inventory.item-stack-response", 148, "item_stack_response", "clientbound", "none", "DecodeItemStackResponse", "BedrockItemStackCodecs.DecodeResponse(payload, context.Limits)"),
            ["inventory.transaction"] = new("inventory.transaction", 30, "inventory_transaction", "bidirectional", "none", "DecodeInventoryTransaction", "BedrockInventoryTransactionCodecs.Decode(payload, context.Limits)"),
            ["inventory.creative-content"] = new("inventory.creative-content", 145, "creative_content", "clientbound", "none", "DecodeCreativeContent", "BedrockCreativeContentCodecs.Decode(payload, context.Limits)"),
        };

    internal static ConcreteCodecBinding Require(RequiredPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (string.IsNullOrWhiteSpace(packet.Codec) || !s_bindings.TryGetValue(packet.Codec, out ConcreteCodecBinding? binding))
            throw new SchemaException($"Verified binding for packet '{packet.Name}' has no concrete codec '{packet.Codec ?? "<null>"}'.");
        if (binding.PacketId != packet.Id || !binding.PacketName.Equals(packet.Name, StringComparison.Ordinal))
            throw new SchemaException($"Concrete codec '{binding.Key}' does not match packet {packet.Id} '{packet.Name}'.");
        if (!binding.Direction.Equals(packet.Direction, StringComparison.Ordinal))
            throw new SchemaException($"Concrete codec '{binding.Key}' direction '{binding.Direction}' does not match '{packet.Direction}'.");
        if (!binding.RegistryGeneration.Equals(packet.RegistryGeneration, StringComparison.Ordinal))
            throw new SchemaException($"Concrete codec '{binding.Key}' registry generation '{binding.RegistryGeneration}' does not match '{packet.RegistryGeneration}'.");
        return binding;
    }

    internal static void Validate(RequiredPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (string.IsNullOrWhiteSpace(packet.Owner))
            throw new SchemaException($"Packet '{packet.Name}' has no owner.");
        if (string.IsNullOrWhiteSpace(packet.Capability))
            throw new SchemaException($"Packet '{packet.Name}' has no capability metadata.");
        if (packet.RegistryGeneration is not ("none" or "item-registry" or "entity-identifiers"))
            throw new SchemaException($"Packet '{packet.Name}' has unsupported registry generation '{packet.RegistryGeneration}'.");

        switch (packet.Binding)
        {
            case "unselected":
                if (packet.Codec is not null || packet.BlockReason is not null || packet.RegistryGeneration != "none")
                    throw new SchemaException($"Unselected binding for packet '{packet.Name}' cannot select a codec, block reason, or registry generation.");
                break;
            case "verified":
                if (!packet.SchemaVerified)
                    throw new SchemaException($"Verified binding for packet '{packet.Name}' requires a verified schema and direction.");
                if (packet.BlockReason is not null)
                    throw new SchemaException($"Verified binding for packet '{packet.Name}' cannot have a block reason.");
                Require(packet);
                break;
            case "blocked":
                if (packet.Codec is not null)
                    throw new SchemaException($"Blocked binding for packet '{packet.Name}' cannot select a codec.");
                if (string.IsNullOrWhiteSpace(packet.BlockReason))
                    throw new SchemaException($"Blocked binding for packet '{packet.Name}' requires a reason.");
                if (packet.RegistryGeneration != "none")
                    throw new SchemaException($"Blocked binding for packet '{packet.Name}' cannot publish a registry generation.");
                break;
            default:
                throw new SchemaException($"Packet '{packet.Name}' has unsupported binding status '{packet.Binding}'.");
        }
    }
}
