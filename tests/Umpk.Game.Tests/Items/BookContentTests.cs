using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Text;
using Xunit;

namespace Umpk.Game.Tests.Items;

/// <summary>The era-neutral book view. The point of these tests is that a 1.20.5+ structured-component book and a pre-1.20.5 NBT book land on the SAME shape, so a consumer implementing "read the pages" never branches on era.</summary>
public class BookContentTests
{
    private static ItemStack Book(string item, DataComponentMap components) =>
        new(ItemTestData.Item(item), 1, components);

    private static ItemStack LegacyBook(string item, NbtCompound tag) =>
        Book(item, DataComponentMap.Empty.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag)));

    private static NbtList Pages(params string[] pages)
    {
        var list = new NbtList(NbtTagType.String);
        foreach (string page in pages)
            list.Add(new NbtString(page));

        return list;
    }

    // 1.20.5+ structured components

    [Fact]
    public void Modern_WrittenBook_ReadsSignedView()
    {
        var components = DataComponentMap.Empty.With(
            DataComponents.WrittenBookContent,
            new WrittenBookContentComponent(
                "Travels",
                "Steve",
                2,
                [new WrittenBookPage(Component.Text("page one")), new WrittenBookPage(Component.Text("page two"))],
                Resolved: true,
                FilteredTitle: "Trav***"));

        BookContent book = Assert.IsType<BookContent>(Book("stone", components).Book);

        Assert.True(book.IsSigned);
        Assert.Equal("Travels", book.Title);
        Assert.Equal("Trav***", book.FilteredTitle);
        Assert.Equal("Steve", book.Author);
        Assert.Equal(2, book.Generation);
        Assert.True(book.Resolved);
        Assert.Equal(["page one", "page two"], book.PlainPages);
    }

    [Fact]
    public void Modern_WritableBook_ReadsUnsignedView()
    {
        var components = DataComponentMap.Empty.With(
            DataComponents.WritableBookContent,
            new WritableBookContentComponent([new BookPage("draft one"), new BookPage("draft two", "d**** two")]));

        BookContent book = Assert.IsType<BookContent>(Book("stone", components).Book);

        Assert.False(book.IsSigned);
        Assert.Null(book.Title);
        Assert.Null(book.Author);
        Assert.Equal(0, book.Generation);
        Assert.False(book.Resolved);
        Assert.Equal(["draft one", "draft two"], book.PlainPages);
        Assert.Equal("d**** two", book.Pages[1].Filtered?.ToPlainText());
    }

    [Fact]
    public void Modern_WrittenBook_KeepsPageStyling()
    {
        var styled = new Component(new TextContent("loud"), new Style { Bold = true });
        var components = DataComponentMap.Empty.With(
            DataComponents.WrittenBookContent,
            new WrittenBookContentComponent("T", "A", 0, [new WrittenBookPage(styled)]));

        BookContent book = Assert.IsType<BookContent>(Book("stone", components).Book);
        Assert.True(book.Pages[0].Text.Style.Bold);
    }

    // pre-1.20.5 NBT, read through the residual legacy compound

    [Fact]
    public void Legacy_WritableBook_PlainStringPages_ReadSameView()
    {
        var tag = new NbtCompound();
        tag.Put("pages", Pages("draft one", "draft two"));

        BookContent book = Assert.IsType<BookContent>(LegacyBook("stone", tag).Book);

        Assert.False(book.IsSigned);
        Assert.Null(book.Title);
        Assert.Null(book.Author);
        Assert.Equal(["draft one", "draft two"], book.PlainPages);
    }

    // A legacy SIGNED book stores each page as a JSON component string, not as plain text. The view parses them, so PlainPages reads the same as the modern component form.
    [Fact]
    public void Legacy_WrittenBook_JsonPages_ReadSameView()
    {
        var tag = new NbtCompound();
        tag.PutString("title", "Travels");
        tag.PutString("author", "Steve");
        tag.PutInt("generation", 2);
        tag.PutBool("resolved", true);
        tag.Put("pages", Pages("{\"text\":\"page one\"}", "{\"text\":\"page two\",\"bold\":true}"));

        BookContent book = Assert.IsType<BookContent>(LegacyBook("stone", tag).Book);

        Assert.True(book.IsSigned);
        Assert.Equal("Travels", book.Title);
        Assert.Equal("Steve", book.Author);
        Assert.Equal(2, book.Generation);
        Assert.True(book.Resolved);
        Assert.Equal(["page one", "page two"], book.PlainPages);
        Assert.True(book.Pages[1].Text.Style.Bold);
    }

    // 1.8-era signed books sometimes carry bare text where a JSON component is expected. That must degrade to a literal page rather than faulting the stack.
    [Fact]
    public void Legacy_WrittenBook_NonJsonPage_FallsBackToLiteral()
    {
        var tag = new NbtCompound();
        tag.PutString("title", "Old");
        tag.PutString("author", "Notch");
        tag.Put("pages", Pages("just text", "{ not valid json"));

        BookContent book = Assert.IsType<BookContent>(LegacyBook("stone", tag).Book);

        Assert.True(book.IsSigned);
        Assert.Equal(["just text", "{ not valid json"], book.PlainPages);
    }

    [Fact]
    public void Legacy_FilteredTitleAndPages_AreRead()
    {
        var filtered = new NbtCompound();
        filtered.PutString("1", "{\"text\":\"p***\"}");

        var tag = new NbtCompound();
        tag.PutString("title", "Rude");
        tag.PutString("filtered_title", "Nice");
        tag.PutString("author", "Steve");
        tag.Put("pages", Pages("{\"text\":\"clean\"}", "{\"text\":\"pfff\"}"));
        tag.Put("filtered_pages", filtered);

        BookContent book = Assert.IsType<BookContent>(LegacyBook("stone", tag).Book);

        Assert.Equal("Rude", book.Title);
        Assert.Equal("Nice", book.FilteredTitle);
        Assert.Null(book.Pages[0].Filtered);
        Assert.Equal("p***", book.Pages[1].Filtered?.ToPlainText());
    }

    // signed vs unsigned, and the absent case

    [Fact]
    public void SignedAndUnsigned_DifferOnlyByTheSignedFields_AcrossBothWireLayouts()
    {
        var modernUnsigned = DataComponentMap.Empty.With(
            DataComponents.WritableBookContent, new WritableBookContentComponent([new BookPage("same text")]));
        var modernSigned = DataComponentMap.Empty.With(
            DataComponents.WrittenBookContent,
            new WrittenBookContentComponent("T", "A", 0, [new WrittenBookPage(Component.Text("same text"))]));

        var legacyUnsigned = new NbtCompound();
        legacyUnsigned.Put("pages", Pages("same text"));

        var legacySigned = new NbtCompound();
        legacySigned.PutString("title", "T");
        legacySigned.PutString("author", "A");
        legacySigned.Put("pages", Pages("{\"text\":\"same text\"}"));

        BookContent[] unsigned =
        [
            Assert.IsType<BookContent>(Book("stone", modernUnsigned).Book),
            Assert.IsType<BookContent>(LegacyBook("stone", legacyUnsigned).Book),
        ];
        BookContent[] signed =
        [
            Assert.IsType<BookContent>(Book("stone", modernSigned).Book),
            Assert.IsType<BookContent>(LegacyBook("stone", legacySigned).Book),
        ];

        foreach (BookContent book in unsigned)
        {
            Assert.False(book.IsSigned);
            Assert.Null(book.Title);
            Assert.Null(book.Author);
            Assert.Equal(["same text"], book.PlainPages);
        }

        foreach (BookContent book in signed)
        {
            Assert.True(book.IsSigned);
            Assert.Equal("T", book.Title);
            Assert.Equal("A", book.Author);
            Assert.Equal(["same text"], book.PlainPages);
        }
    }

    [Fact]
    public void NonBookStack_HasNoBookContent()
    {
        Assert.Null(ItemTestData.Stack("stone", 1).Book);
        Assert.Null(ItemStack.Empty.Book);
    }

    // A legacy stack whose residual NBT is unrelated to books must not be mistaken for a blank book.
    [Fact]
    public void LegacyNbt_WithoutBookMembers_HasNoBookContent()
    {
        var tag = new NbtCompound();
        tag.PutInt("SomeOtherThing", 7);
        Assert.Null(LegacyBook("stone", tag).Book);
    }

    [Fact]
    public void EmptyBook_ReadsAsPresentWithNoPages()
    {
        var modern = DataComponentMap.Empty.With(
            DataComponents.WritableBookContent, new WritableBookContentComponent([]));
        BookContent book = Assert.IsType<BookContent>(Book("stone", modern).Book);
        Assert.False(book.IsSigned);
        Assert.Empty(book.Pages);

        var legacy = new NbtCompound();
        legacy.Put("pages", Pages());
        BookContent legacyBook = Assert.IsType<BookContent>(LegacyBook("stone", legacy).Book);
        Assert.False(legacyBook.IsSigned);
        Assert.Empty(legacyBook.Pages);
    }
}
