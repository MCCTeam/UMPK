namespace Umpk.Client.Bedrock.State.Actors;

internal readonly record struct BedrockRuntimeEntityId(ulong Value);

internal readonly record struct BedrockUniqueEntityId(long Value);

internal readonly record struct BedrockActorHandle(int WorldGeneration, ulong RuntimeEntityId, long UniqueEntityId, bool IsLocalPlayer);
