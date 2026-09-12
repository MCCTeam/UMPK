---
title: "The packet pipeline"
description: "How a Minecraft frame becomes a typed packet, client state, and events, including the checks that keep era bindings honest."
sidebar:
  order: 4
---

A frame passes through four layers:

1. A generated descriptor resolves the wire id to a packet identifier for one protocol.
2. A hand-authored packet timeline resolves that identifier to an era codec.
3. The codec decodes a frame-exact typed record.
4. Client appliers fold the record into state and publish events.

The first two layers are intentionally separate. The dataset knows the server's registration table; the codec tree owns the interpretation of a packet body.

## Descriptor registration

`data/java/<protocol>/` records the mapping from `(phase, flow, wire id)` to an identifier such as `minecraft:level_chunk_with_light`. DataGen emits each protocol descriptor from that data. It does not emit a codec choice or use a `codecKey` to make one.

At descriptor construction, `PacketRegistrar.Register` joins the generated row with `PacketBindings`. Its inputs are the phase, flow, wire id, and identifier. It no longer accepts a `codecKey`. The binding table is assembled once from nine family indexes in `src/Umpk.Protocol.Java/Registration/`. A family index is deliberately small: it calls each packet's `Declare<Packet>` method, which lives next to the corresponding record and `.Wire.cs` codec under `src/Umpk.Protocol.Java/Families/<family>/<packet>/`.

That puts one packet's cross-version story beside its wire implementation instead of burying it in a large registration file. The generated row provides the current wire id; the family-local timeline provides the codec.

## Timelines and markers

A timeline is an ordered set of `From` steps. For protocol `P`, resolution chooses the step with the greatest `fromProtocol <= P`.

```csharp
bindings.Packet(WorldPackets.Clientbound.LightUpdate)
    .From(JavaProtocols.V1_14, WorldStateCodecs.LightUpdateV1_14)
    .From(JavaProtocols.V1_16, WorldStateCodecs.LightUpdateV1_16)
    .From(JavaProtocols.V1_17, WorldStateCodecs.LightUpdateV1_17)
    .From(JavaProtocols.V1_20, WorldStateCodecs.LightUpdateV1_20);
```

The final step governs every later supported protocol until another step is added. This is forward timeline resolution, not language-level C# inheritance. A new protocol with an unchanged body simply uses the previous band. A moved layout requires a new step at that protocol.

Timelines remain hand-authored at bind time. Codec bodies do not compare protocol numbers to select a layout. The timeline resolves the era codec first, so each codec body can describe one wire form and the protocol comparison stays in the binding declaration.

Duplicate packet identities and duplicate timeline boundaries throw while the table is built. A timeline may also deliberately resolve to a marker. Markers are not an accident: declared absences and marker steps carry exact ranges and reasons, and `IntentionalMarkers.cs` verifies that the allowlist still matches reality.

## Codecs are frame-exact

`BoundPacketCodec.Decode` owns the frame-exactness rule. After the decoder returns, unread bytes are a protocol violation. A codec that reads only the beginning of a changed packet cannot look successful by leaving a tail behind.

Codec tests should decode real or byte-annotated frames, assert their values, and assert that encoding the result returns the original bytes. A zero-byte or all-zero smoke test is useful for pinning fixed framing, but it is not sufficient evidence that two eras differ.

## Why there are several pins

One protocol descriptor can be self-consistent and still choose the wrong era codec. The conformance fixtures inspect that risk from several directions.

- `fixtures/registration/<protocol>.txt` records whether each generated row resolved to a codec or a marker.
- `fixtures/codec-identity/<protocol>.txt` records the bind-site identity, zero-probe behavior, `shape:` token, and `via:` identifier when an alias fired. The wire-shape token comes from the codec object and its era data, not from the bind-site expression, so a pure rename cannot change it.
- `fixtures/timelines/bands.txt` transposes those per-protocol rows into each packet's contiguous codec bands. It makes a release boundary visible as a local band change.
- `fixtures/witness/<protocol>.txt` renders the result of authored, value-carrying witness payloads. Their `reject:` clauses prove that a neighbouring band cannot successfully read the same payload, except for explicitly justified pairs in `IntentionalTwins.cs`.

The corpus has captures for all 49 protocols. The witness directory still has one rendered file per protocol, but its authored witnesses cover only 20 packet families. Band, witness, and shape pins are separate checks with different blind spots, not a full conformance proof.

The fixture renderings are mechanical and regenerated only through their environment-gated test paths. The witness payload catalog, intentional-marker list, and intentional-twin list are hand-authored; they must not be regenerated to make a failure disappear.

No collection of these checks claims full conformance. The layout oracle is intentionally weaker than a proof: it compares parsed release layouts, not UMPK inheritance or C# codec bodies, and reports packets it cannot parse. Packet bytes that exercise each changed boundary remain the primary wire evidence.

## Appliers and events

`Umpk.Client` applies decoded records on the session loop in packet order. Each applier owns a slice of client state, such as connection, chat, world, entity, or inventory. Some are feature-gated, so a disabled subsystem does not perform work only to discard it.

Appliers then publish the public event surface: examples include `JoinedGame`, `ChatMessageReceived`, `EntitySpawned`, `PositionCorrected`, and `PacketReceived`. The latter retains the phase, real wire id, decoded packet, and payload length for consumers that need protocol detail.
