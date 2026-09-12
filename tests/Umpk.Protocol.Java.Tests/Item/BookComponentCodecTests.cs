using System.Buffers;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>
/// Byte-anchored tests for the two book components. The expected bytes are hand-built field by field, never by running the encoder, so an encoder that merely agrees with itself cannot satisfy them.
/// <para>Writable book content is a collection of filterable strings, each written as <c>raw</c> then <c>optional(filtered)</c>. So a writable book is: VarInt(pageCount), then per page string(1024) + bool + optional string(1024). Written book content is: a filterable string title (cap 32), utf8 author, VarInt generation, collection of <c>Filterable&lt;Component&gt;</c> pages, bool resolved. A component page is written as network NBT (<c>JavaUnnamedRoot</c>), and a pure literal with no style collapses to a bare TAG_String.</para>
/// </summary>
public sealed class BookComponentCodecTests
{
    private static byte[] Build(Action<PacketWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        write(w);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Encode(ItemComponentCodec codec, object value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, value, ItemTestRegistries.Context);
        return buffer.WrittenSpan.ToArray();
    }

    private static object Decode(ItemComponentCodec codec, byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        object decoded = codec.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    [Fact]
    public void WritableBookContent_PinnedFrame()
    {
        var content = new WritableBookContentComponent(
        [
            new BookPage("first"),
            new BookPage("second", "censored"),
        ]);

        byte[] expected = Build(w =>
        {
            w.WriteVarInt(2);                   // collection size
            w.WriteString("first", 1024);       // page 0 raw
            w.WriteBool(false);                 // page 0 filtered absent
            w.WriteString("second", 1024);      // page 1 raw
            w.WriteBool(true);                  // page 1 filtered present
            w.WriteString("censored", 1024);    // page 1 filtered
        });

        Assert.Equal(expected, Encode(ItemComponentCodecs.WritableBookContent, content));

        var decoded = (WritableBookContentComponent)Decode(ItemComponentCodecs.WritableBookContent, expected);
        Assert.Equal(2, decoded.Pages.Count);
        Assert.Equal("first", decoded.Pages[0].Raw);
        Assert.Null(decoded.Pages[0].Filtered);
        Assert.Equal("second", decoded.Pages[1].Raw);
        Assert.Equal("censored", decoded.Pages[1].Filtered);
    }

    [Fact]
    public void WrittenBookContent_PinnedFrame()
    {
        var content = new WrittenBookContentComponent(
            Title: "My Book",
            Author: "Steve",
            Generation: 1,
            Pages:
            [
                new WrittenBookPage(Component.Text("Hello")),
                new WrittenBookPage(Component.Text("World"), Component.Text("W****")),
            ],
            Resolved: true);

        byte[] expected = Build(w =>
        {
            w.WriteString("My Book", 32);                                    // title raw
            w.WriteBool(false);                                              // title filtered absent
            w.WriteString("Steve");                                          // author
            w.WriteVarInt(1);                                                // generation
            w.WriteVarInt(2);                                                // page collection size
            w.WriteNbt(new NbtString("Hello"), NbtWireFormat.JavaUnnamedRoot);
            w.WriteBool(false);                                              // page 0 filtered absent
            w.WriteNbt(new NbtString("World"), NbtWireFormat.JavaUnnamedRoot);
            w.WriteBool(true);                                               // page 1 filtered present
            w.WriteNbt(new NbtString("W****"), NbtWireFormat.JavaUnnamedRoot);
            w.WriteBool(true);                                               // resolved
        });

        Assert.Equal(expected, Encode(ItemComponentCodecs.WrittenBookContent, content));

        var decoded = (WrittenBookContentComponent)Decode(ItemComponentCodecs.WrittenBookContent, expected);
        Assert.Equal("My Book", decoded.Title);
        Assert.Null(decoded.FilteredTitle);
        Assert.Equal("Steve", decoded.Author);
        Assert.Equal(1, decoded.Generation);
        Assert.True(decoded.Resolved);
        Assert.Equal(2, decoded.Pages.Count);
        Assert.Equal(Component.Text("Hello"), decoded.Pages[0].Raw);
        Assert.Null(decoded.Pages[0].Filtered);
        Assert.Equal(Component.Text("W****"), decoded.Pages[1].Filtered);
    }

    // The optional filtered title must survive a round trip in both directions.
    [Fact]
    public void WrittenBookContent_FilteredTitle_PinnedFrame()
    {
        var content = new WrittenBookContentComponent(
            Title: "Rude Title",
            Author: "Alex",
            Generation: 0,
            Pages: [],
            Resolved: false,
            FilteredTitle: "Nice Title");

        byte[] expected = Build(w =>
        {
            w.WriteString("Rude Title", 32);   // title raw
            w.WriteBool(true);                 // title filtered present
            w.WriteString("Nice Title", 32);   // title filtered
            w.WriteString("Alex");             // author
            w.WriteVarInt(0);                  // generation
            w.WriteVarInt(0);                  // no pages
            w.WriteBool(false);                // resolved
        });

        Assert.Equal(expected, Encode(ItemComponentCodecs.WrittenBookContent, content));

        var decoded = (WrittenBookContentComponent)Decode(ItemComponentCodecs.WrittenBookContent, expected);
        Assert.Equal("Rude Title", decoded.Title);
        Assert.Equal("Nice Title", decoded.FilteredTitle);
        Assert.Empty(decoded.Pages);
    }

    // Styled pages must retain their component form through both directions.
    [Fact]
    public void WrittenBookContent_StyledPage_PinnedFrame()
    {
        var styled = new Component(new TextContent("Shout"), new Style { Bold = true });
        var content = new WrittenBookContentComponent("T", "A", 0, [new WrittenBookPage(styled)]);

        var styledTag = new NbtCompound();
        styledTag.PutString("text", "Shout");
        styledTag.PutBool("bold", true);

        byte[] expected = Build(w =>
        {
            w.WriteString("T", 32);
            w.WriteBool(false);
            w.WriteString("A");
            w.WriteVarInt(0);
            w.WriteVarInt(1);
            w.WriteNbt(styledTag, NbtWireFormat.JavaUnnamedRoot);
            w.WriteBool(false);
            w.WriteBool(false);
        });

        Assert.Equal(expected, Encode(ItemComponentCodecs.WrittenBookContent, content));

        var decoded = (WrittenBookContentComponent)Decode(ItemComponentCodecs.WrittenBookContent, expected);
        Assert.Equal(styled, decoded.Pages[0].Raw);
        Assert.True(decoded.Pages[0].Raw.Style.Bold);

        // Byte-stability: re-encoding what we decoded reproduces the same frame.
        Assert.Equal(expected, Encode(ItemComponentCodecs.WrittenBookContent, decoded));
    }

    [Fact]
    public void BookComponents_EmptyContent_PinnedFrame()
    {
        byte[] writable = Build(w => w.WriteVarInt(0));
        Assert.Equal(writable, Encode(ItemComponentCodecs.WritableBookContent, new WritableBookContentComponent([])));
        Assert.Empty(((WritableBookContentComponent)Decode(ItemComponentCodecs.WritableBookContent, writable)).Pages);
    }
}
