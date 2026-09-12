---
title: Using AI
description: Rules for using AI assistance without weakening evidence, reviews, or secret handling.
sidebar:
  order: 4
---

AI can help with repetitive edits, test ideas, and documentation. It does not replace review or the evidence required for a protocol claim. Treat generated text and code as an untrusted draft.

## Before you use an AI tool

1. Remove credentials, access tokens, session material, private server addresses, raw packet captures, and downloaded game artifacts from the prompt.
2. State the target behavior and protocol versions.
3. Give the tool only the files it needs.
4. Keep the generated result separate from unrelated work.

Do not paste authentication caches, device-code prompts, packet dumps, or user data into an external service. Redaction is safer than an assumption about a tool's retention policy.

## Review the result

1. Read every changed line.
2. Check public API changes against the applicable `PublicAPI.Unshipped.txt` file.
3. Check protocol claims against official artifacts, vanilla source, or real captured frames.
4. Check that generated files did not receive manual edits.
5. Run the focused tests before you run the complete validation gate.

Do not accept an AI citation as protocol evidence. Record the source version, artifact hash, and observed bytes when a change depends on a wire boundary. A plausible packet shape is not evidence.

## Repository rules still apply

Never edit a `.g.cs` file by hand. Change the dataset or generator, then regenerate the output. Do not weaken a test, remove a pin, or change an expected count only to make generated code pass.

AI can propose an implementation. A maintainer owns the final change, its evidence, and its validation. Read [Development](/contributing/development) and [Testing](/contributing/testing) before you open a pull request.
