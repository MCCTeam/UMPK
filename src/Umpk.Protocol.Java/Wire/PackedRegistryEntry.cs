using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

// Shared payload records

/// <summary>One packed registry entry as carried in <c>registry_data</c>: the entry id and an optional inline NBT tag holding its data (absent when the entry is defaulted from a known pack).</summary>
public sealed record PackedRegistryEntry(Identifier Id, Umpk.Nbt.NbtTag? Data);
