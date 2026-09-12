// NbtLab builds an NBT tag, writes it in both wire shapes, reads it back, and round trips through SNBT. It needs no server. Everything runs offline.
//
// Run it like this:
//
//   dotnet run --project samples/NbtLab
//
// NBT is the binary tag format behind items, block entities, and chunks. The same tree has three framings on the wire, and picking the wrong one gives you bytes that decode into garbage instead of an error.
using Umpk.Nbt;
using Umpk.Nbt.Snbt;

// Build a small chest tag. Compounds hold named fields. Lists hold one type. This mirrors what a block entity looks like without needing a world.
var tag = new NbtCompound();
tag.PutString("id", "minecraft:chest");
tag.PutInt("x", 12);
tag.PutInt("y", 64);
tag.PutInt("z", -3);
tag.PutBool("locked", false);

var items = new NbtList();
var slot = new NbtCompound();
slot.PutByte("Slot", 0);
slot.PutString("id", "minecraft:diamond");
slot.PutByte("Count", 3);
items.Add(slot);
tag.Put("Items", items);

Console.WriteLine("Built:");
Console.WriteLine($"  id={tag.GetString("id")} at ({tag.GetInt("x")}, {tag.GetInt("y")}, {tag.GetInt("z")})");
Console.WriteLine($"  items={tag.GetList("Items")?.Count ?? 0}");
Console.WriteLine();

// Disk and pre 1.20.2 network use the named root: type byte, root name, body. Network from 1.20.2 uses the unnamed root: type byte, body, no name. Both carry the same tree. Only the framing differs.
byte[] onDisk = NbtWriter.ToArray(tag, NbtWireFormat.JavaNamedRoot);
byte[] onWire = NbtWriter.ToArray(tag, NbtWireFormat.JavaUnnamedRoot);
Console.WriteLine("Framing:");
Console.WriteLine($"  named root bytes:   {onDisk.Length}");
Console.WriteLine($"  unnamed root bytes: {onWire.Length}");
Console.WriteLine();

// Always decode under a quota when the bytes came from elsewhere. CreateDefault gives you the vanilla network budget and depth cap. Overruns raise NbtSizeLimitException or NbtDepthLimitException. Malformed bytes raise NbtFormatException.
NbtTag decoded = NbtReader.Read(onWire, NbtWireFormat.JavaUnnamedRoot, NbtAccounter.CreateDefault());
var back = (NbtCompound)decoded;
Console.WriteLine("Decoded:");
Console.WriteLine($"  id={back.GetString("id")}");
Console.WriteLine($"  first item id={((NbtCompound)back.GetList("Items")![0]).GetString("id")}");
Console.WriteLine();

// When a tag sits inside a larger frame, use the overload that reports bytesRead so you know where the tag ends and the next field begins.
int bytesRead;
NbtReader.Read(onWire, NbtWireFormat.JavaUnnamedRoot, NbtAccounter.CreateDefault(), out bytesRead);
Console.WriteLine($"  tag length inside its frame: {bytesRead} bytes");
Console.WriteLine();

// SNBT is the text form you see in commands. ParseCompound expects a compound. ParseValue accepts any tag. Print turns a tag back into text.
NbtCompound fromText = SnbtParser.ParseCompound("{id:\"minecraft:furnace\",x:1,BurnTime:200s}");
Console.WriteLine("SNBT:");
Console.WriteLine($"  parsed id={fromText.GetString("id")} burn={fromText.GetShort("BurnTime")}");
Console.WriteLine($"  printed: {SnbtPrinter.Print(fromText)}");
Console.WriteLine($"  original reprinted: {SnbtPrinter.Print(tag)}");
Console.WriteLine();

// Typed getters are lenient by design. A missing int reads as 0. A missing string reads as empty. A missing compound or list reads as null. Use ContainsKey or TryGet when absent and zero mean different things.
Console.WriteLine("Missing keys:");
Console.WriteLine($"  absent int reads as: {tag.GetInt("nope")}");
Console.WriteLine($"  has id: {tag.ContainsKey("id")}, has nope: {tag.ContainsKey("nope")}");
if (tag.TryGet("id", out NbtTag? found))
{
    Console.WriteLine($"  TryGet found: {found}");
}

return 0;
