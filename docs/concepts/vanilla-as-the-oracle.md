---
title: "Vanilla as the oracle"
description: "How UMPK verifies behavior against Mojang releases and records maintainable conclusions."
sidebar:
  order: 2
---

Every protocol library must answer the same question many times: what does the game actually do here? UMPK answers it with evidence from Mojang releases and real packet captures. A wiki or another library can help locate a question, but neither is the final authority for wire behavior, physics constants, or version boundaries.

The evidence and the implementation serve different readers:

- Change reviews record the detailed research, release versions, artifacts, and reproduction steps.
- Tests preserve byte-level examples and independent boundary expectations.
- Source comments explain the resulting behavior, invariant, compatibility rule, or design reason.

Keeping raw research locations out of source comments avoids brittle paths and obfuscated names while retaining the information maintainers need beside the code.

## Start with observable behavior

State the claim before looking for an implementation. For a packet, define its field order, optional fields, and the exact releases where the layout changes. For physics, define the calculation and its boundary. Then gather evidence on both sides of every claimed boundary.

For example, the light-update packet has four observable wire forms:

- Protocols 477 to 578 use integer masks without a trust-edges flag.
- Protocols 735 to 754 add the trust-edges flag.
- Protocols 755 to 762 use bit-set masks and counted array lists while retaining the flag.
- Protocol 763 and later remove the flag.

Captured frames from protocols 762 and 763 exercise both sides of the final boundary. The maintained code comment only needs to describe those forms and why the boundary matters. The review evidence can retain the artifact locations and inspection details.

## Inspect release artifacts when needed

The extraction tools can use parsed source trees, server data reports, mappings, and release jars. Coverage varies by version, so do not treat one input as universally available. When a parsed source tree is absent, inspect the release jar with the JDK selected by the integration harness:

```bash
javap -p -c -cp server.jar <class-name>
```

Mojang's published mappings help interpret obfuscated names. Distinctive constants can also identify a method across releases. For example, the depth-strider water-speed blend contains a stable floating-point constant that makes its surrounding calculation easy to compare.

Release jars and libraries are not redistributable and must stay in the gitignored extraction workspace. Do not copy their paths, obfuscated symbols, or line numbers into implementation comments. Record those details with the change review and keep the source comment focused on the verified result.

## Require independent tests

A codec test must assert decoded values from a real or byte-annotated frame and require byte-identical encoding of the result. A successful decode by itself is insufficient because a wrong layout can still produce plausible values.

An era test must state its expectation independently of the dataset. Reading a feature value and then asserting that the engine copied the same value only tests the adapter. Use a literal table that covers every supported protocol and places the boundary explicitly. [Era gating](era-gating.md) describes that pattern.

## Write durable comments

A useful implementation comment answers one of these questions:

- What non-obvious behavior does this code preserve?
- Which protocol range or capability changes the rule?
- Which invariant would a simpler-looking implementation violate?
- Why is a test or allocation boundary necessary?

Avoid implementation history, research paths, tool transcripts, and source-symbol citations. Those details become stale quickly and distract from the maintained contract.

When evidence is incomplete, say so in the change review and add the strongest available test. Do not turn uncertainty into a confident source comment. The [development guide](../contributing/development.md) lists the complete validation gate.
