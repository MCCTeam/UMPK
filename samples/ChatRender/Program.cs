// ChatRender builds chat components by hand and reads them back. It needs no server and no account. Everything here runs offline.
//
// Run it like this:
//
//   dotnet run --project samples/ChatRender
//
// You will see the same message printed four ways: as plain text, as modern JSON, as legacy JSON, and as section sign text. The point is that one tree has several wire shapes, and the era decides.
using Umpk.Data.Lang;
using Umpk.Text;
using Umpk.Text.Serialization;

// A chat message is a tree. This one says "Welcome, Steve" where the name carries color, bold, and a click action. The parent holds the shared style and the child holds the name. Children inherit what they do not override.
var message = new Component(
    new TextContent("Welcome, "),
    new Style { Color = TextColor.Gray },
    [
        new Component(
            new TextContent("Steve"),
            new Style
            {
                Color = TextColor.Gold,
                Bold = true,
                ClickEvent = new ClickEvent(ClickEventAction.SuggestCommand, "/msg Steve "),
                HoverEvent = new HoverShowText(Component.Text("Click to message Steve")),
            }),
    ]);

// Plain text drops all styling. This is what you log or print to a terminal.
Console.WriteLine("Plain:");
Console.WriteLine($"  {message.ToPlainText()}");
Console.WriteLine();

// JSON has two eras. Modern is 1.21.5 and later. Legacy is 1.21.4 and earlier. The shapes differ for click and hover events, so passing the wrong era produces JSON a server will reject or misread. When you talk to an old server, pass Legacy explicitly. The default is Modern.
string modern = ComponentJson.ToJsonString(message, ComponentWireEra.Modern);
string legacy = ComponentJson.ToJsonString(message, ComponentWireEra.Legacy);
Console.WriteLine("Modern JSON (1.21.5 and later):");
Console.WriteLine($"  {modern}");
Console.WriteLine("Legacy JSON (1.21.4 and earlier):");
Console.WriteLine($"  {legacy}");
Console.WriteLine();

// A server MOTD arrives in two shapes. Old servers send a bare string. New servers send a component tree. Parse reads both, so you never branch.
Component motdString = ComponentJson.Parse("\"A Minecraft Server\"");
Component motdTree = ComponentJson.Parse("{\"text\":\"A \",\"extra\":[{\"text\":\"Minecraft Server\",\"color\":\"gold\"}]}");
Console.WriteLine("MOTD:");
Console.WriteLine($"  string shape reads as: {motdString.ToPlainText()}");
Console.WriteLine($"  tree shape reads as:   {motdTree.ToPlainText()}");
Console.WriteLine();

// Section sign text is the pre 1.7 form that plugins still emit. LegacyText turns it into a tree so the rest of your code sees one shape.
Component colored = LegacyText.Parse("§cRed §lbold");
Console.WriteLine("Legacy codes:");
Console.WriteLine($"  plain text reads as: {colored.ToPlainText()}");
Console.WriteLine($"  re-encoded reads as: {LegacyText.Encode(colored)}");
Console.WriteLine();

// Translatable components need a table to resolve. Without one you get the key or the fallback, which is fine for logs and wrong for humans. VanillaTranslations ships vanilla en_us per protocol, one table each, because argument counts drift across versions for the same key.
Component joinLine = Component.Translatable(
    "multiplayer.player.joined",
    Component.Text("Steve"));
Console.WriteLine("Translate:");
Console.WriteLine($"  without a table: {joinLine.ToPlainText()}");
Console.WriteLine($"  protocol 772:    {joinLine.ToPlainText(VanillaTranslations.ForProtocol(772))}");
Console.WriteLine($"  latest table:    {joinLine.ToPlainText(VanillaTranslations.Latest)}");
Console.WriteLine();

// Style booleans have three states. Null means inherit from the parent. False means explicitly off. This matters when a child sits under a bold parent.
var parent = new Component(
    new TextContent("hi"),
    new Style { Bold = true },
    [
        new Component(new TextContent(" there"), new Style { Bold = false }),
    ]);
Console.WriteLine("Inheritance:");
Console.WriteLine($"  flattened reads as: {parent.ToPlainText()}");
Console.WriteLine($"  runs: {ComponentFlattener.Flatten(parent).Count} styled runs");

return 0;
