namespace Umpk.Protocol.Java;

/// <summary>Identifiers the version datasets register that this library deliberately does not bind, and the reason for each. They have no packet record and no codec, so they cannot live beside one: without this file the only trace of the decision is the silence where a binding would be, which reads exactly like a registration line somebody forgot to write.</summary>
/// <remarks>Not the whole marker set. The great majority of markers are an honest backlog and stay undeclared until somebody works on them; what is written down here is the part that is a DECISION, and <c>IntentionalMarkerAllowlistTests</c> holds every entry against the allowlist's own reason and band. An entry leaves this file the day its packet gains a codec, and the timeline it grows takes the declaration with it if the absence survives on an older era.</remarks>
internal static class DeclaredAbsences
{
    /// <summary>Adds every declared absence to the binding table.</summary>
    internal static void Register(PacketBindings bindings)
    {
        bindings.Absent(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("bundle_delimiter"))
            .MarkerFrom(
                JavaProtocols.V1_20_3,
                MarkerReason.DatasetArtifact,
                "Vanilla has no serverbound configuration bundle delimiter: the bundler wraps clientbound play traffic only. The dataset row is a curation artifact, so a marker is the correct outcome and binding anything here would invent a packet.");

        bindings.Absent(ProtocolPhase.Login, PacketFlow.Serverbound, Identifier.Minecraft("bundle_delimiter"))
            .MarkerFrom(
                JavaProtocols.V1_19_4,
                MarkerReason.DatasetArtifact,
                "Vanilla has no serverbound login bundle delimiter either, and 762 and 763 are the only datasets carrying the row. A marker is correct; the two rows are what should go.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("custom_payload"))
            .MarkerFrom(
                JavaProtocols.V1_8,
                MarkerReason.HandledElsewhere,
                "The play plugin message is served by the raw-frame path rather than by a codec: PluginChannelManager matches the observed wire id and reads channel plus body itself, and the pre-1.20.2 server brand is read straight off the observed payload. Both paths run on every session, so a codec here would duplicate working code.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("debug/block_value"))
            .MarkerFrom(
                JavaProtocols.V1_21_9,
                MarkerReason.NoConsumerWorthWriting,
                "One of the 1.21.9 debug-subscription payloads. A vanilla server sends it only to a client that subscribed through minecraft:debug_subscription_request, and nothing here subscribes, so the frame never arrives.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("debug/chunk_value"))
            .MarkerFrom(
                JavaProtocols.V1_21_9,
                MarkerReason.NoConsumerWorthWriting,
                "A 1.21.9 debug-subscription payload carrying per-chunk developer values, delivered only to a subscriber. Nothing here subscribes, so there is no consumer worth writing for it.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("debug/entity_value"))
            .MarkerFrom(
                JavaProtocols.V1_21_9,
                MarkerReason.NoConsumerWorthWriting,
                "A 1.21.9 debug-subscription payload carrying per-entity developer values, delivered only to a subscriber. Nothing here subscribes, so nothing would ever read a decoded one.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("debug/event"))
            .MarkerFrom(
                JavaProtocols.V1_21_9,
                MarkerReason.NoConsumerWorthWriting,
                "A 1.21.9 debug-subscription payload carrying one-off developer events, delivered only to a subscriber. Nothing here subscribes, so the frame never arrives at all.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("debug_sample"))
            .MarkerFrom(
                JavaProtocols.V1_20_5,
                MarkerReason.NoConsumerWorthWriting,
                "The 1.20.5 tick-timing debug sample, sent only in reply to minecraft:debug_sample_subscription. Nothing here subscribes, so it never arrives and a decoded sample would have no reader.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("game_test_highlight_pos"))
            .MarkerFrom(
                JavaProtocols.V1_21_9,
                MarkerReason.NoConsumerWorthWriting,
                "A game-test harness packet a vanilla server emits only while running the developer test framework. It has no gameplay meaning, so nothing would read a decoded value.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("move_entity"))
            .MarkerFrom(
                JavaProtocols.V1_9,
                MarkerReason.NoConsumerWorthWriting,
                "Vanilla's zero-delta base entity move, which carries an entity id and nothing else and whose own handler applies no state from it, so discarding it costs nothing observable. The 1.8 spelling minecraft:entity is bound at 47.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("test_instance_block_status"))
            .MarkerFrom(
                JavaProtocols.V1_21_5,
                MarkerReason.NoConsumerWorthWriting,
                "Part of the 1.21.5 game-test instance-block tooling, which only a developer running the test framework drives, so no gameplay consumer would read it.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("custom_payload"))
            .MarkerFrom(
                JavaProtocols.V1_8,
                MarkerReason.HandledElsewhere,
                "The send half of the play plugin message. PluginChannelManager writes the channel and body into a raw frame and sends it by wire id, and the REGISTER and UNREGISTER channel announcements go out on that path, so a codec here would duplicate a path that already runs on every session.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("debug_sample_subscription"))
            .MarkerFrom(
                JavaProtocols.V1_20_5,
                MarkerReason.NoConsumerWorthWriting,
                "The subscribe request for the 1.20.5 debug samples. Nothing here wants the samples, so sending one would only create traffic nobody reads.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("debug_subscription_request"))
            .MarkerFrom(
                JavaProtocols.V1_21_9,
                MarkerReason.NoConsumerWorthWriting,
                "The 1.21.9 replacement for minecraft:debug_sample_subscription, and the gate for the whole debug payload family. Nothing subscribes, so none of those payloads ever arrive.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("set_test_block"))
            .MarkerFrom(
                JavaProtocols.V1_21_5,
                MarkerReason.NoConsumerWorthWriting,
                "Part of the 1.21.5 game-test block tooling, driven only by a developer running the test framework, and nothing a client session would ever send.");

        bindings.Absent(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("test_instance_block_action"))
            .MarkerFrom(
                JavaProtocols.V1_21_5,
                MarkerReason.NoConsumerWorthWriting,
                "The client half of the 1.21.5 game-test instance-block tooling. Only a developer running the test framework drives it, so nothing a client session would send.");
    }
}
