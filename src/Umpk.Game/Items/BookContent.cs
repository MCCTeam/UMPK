using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Game.Items;

/// <summary>One page of the era-neutral <see cref="BookContent"/> view. Always a <see cref="Component"/>: a writable book's plain page and a legacy plain-text page become <see cref="Component.Text(string)"/>, a written book's page is the wire component, and a legacy JSON page is the parsed component. Use <see cref="PlainText"/> when a consumer only wants the flattened text.</summary>
/// <param name="Text">The page content.</param>
/// <param name="Filtered">The filtered variant of the page, when the server sent one.</param>
public sealed record BookContentPage(Component Text, Component? Filtered = null)
{
    /// <summary>The page flattened to plain text.</summary>
    public string PlainText => Text.ToPlainText();
}

/// <summary>
/// The decoded content of a book, unified across both item eras so a consumer never branches on whether the server speaks structured components or pre-1.20.5 NBT.
/// <list type="bullet">
/// <item>1.20.5+ reads <c>minecraft:written_book_content</c> (signed) or
/// <c>minecraft:writable_book_content</c> (unsigned).</item>
/// <item>Pre-1.20.5 reads the residual legacy compound carried by
/// <see cref="DataComponents.LegacyNbt"/>: <c>pages</c> (plus <c>filtered_pages</c>), and for a signed book <c>title</c>, <c>filtered_title</c>, <c>author</c>, <c>generation</c> and <c>resolved</c>. Legacy signed pages hold JSON components; unsigned pages hold plain strings.</item>
/// </list>
/// </summary>
/// <param name="IsSigned">True for a written (signed) book, false for a writable book and quill.</param>
/// <param name="Title">The title, or null when unsigned.</param>
/// <param name="FilteredTitle">The filtered title variant, when the server sent one.</param>
/// <param name="Author">The author, or null when unsigned.</param>
/// <param name="Generation">The copy generation (0 original, 1 copy, 2 copy of a copy, 3 tattered); 0 when unsigned.</param>
/// <param name="Resolved">Whether the server has resolved the pages; false when unsigned.</param>
/// <param name="Pages">The pages in order; empty for a blank book.</param>
public sealed record BookContent(
    bool IsSigned,
    string? Title,
    string? FilteredTitle,
    string? Author,
    int Generation,
    bool Resolved,
    IReadOnlyList<BookContentPage> Pages)
{
    /// <summary>The pages flattened to plain text, in order.</summary>
    public IReadOnlyList<string> PlainPages
    {
        get
        {
            var result = new string[Pages.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = Pages[i].PlainText;

            return result;
        }
    }

    /// <summary>Reads the book content carried by a component map, or null when the map holds no book data. Structured components win over the legacy compound so a hybrid map (a legacy stack that was bridged and then edited) still reports the authoritative value.</summary>
    /// <param name="components">The stack's component map.</param>
    /// <exception cref="ArgumentNullException"><paramref name="components"/> is null.</exception>
    public static BookContent? From(DataComponentMap components)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (components.TryGet(DataComponents.WrittenBookContent, out WrittenBookContentComponent? written))
        {
            var pages = new BookContentPage[written.Pages.Count];
            for (int i = 0; i < pages.Length; i++)
            {
                WrittenBookPage page = written.Pages[i];
                pages[i] = new BookContentPage(page.Raw, page.Filtered);
            }

            return new BookContent(
                IsSigned: true,
                written.Title,
                written.FilteredTitle,
                written.Author,
                written.Generation,
                written.Resolved,
                pages);
        }

        if (components.TryGet(DataComponents.WritableBookContent, out WritableBookContentComponent? writable))
        {
            var pages = new BookContentPage[writable.Pages.Count];
            for (int i = 0; i < pages.Length; i++)
            {
                BookPage page = writable.Pages[i];
                pages[i] = new BookContentPage(
                    Component.Text(page.Raw),
                    page.Filtered is null ? null : Component.Text(page.Filtered));
            }

            return new BookContent(
                IsSigned: false,
                Title: null,
                FilteredTitle: null,
                Author: null,
                Generation: 0,
                Resolved: false,
                pages);
        }

        return components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy)
            ? FromLegacyNbt(legacy.Nbt)
            : null;
    }

    /// <summary>Reads the book content out of a pre-1.20.5 item NBT compound (the old <c>tag</c> payload), or null when the compound carries no book members. Exposed so a caller holding raw legacy NBT can reach the same view without first building a component map.</summary>
    /// <param name="nbt">The legacy item compound.</param>
    /// <exception cref="ArgumentNullException"><paramref name="nbt"/> is null.</exception>
    public static BookContent? FromLegacyNbt(NbtCompound nbt)
    {
        ArgumentNullException.ThrowIfNull(nbt);

        NbtList? pageList = nbt.GetList("pages");
        bool hasTitle = nbt.TryGet("title", out NbtString? titleTag);
        bool hasAuthor = nbt.TryGet("author", out NbtString? authorTag);
        if (pageList is null && !hasTitle && !hasAuthor)
            return null;

        // A written book always carries a title; a book and quill carries pages only. Vanilla signs a book by replacing writable_book with written_book and adding title/author in the same tag.
        bool signed = hasTitle || hasAuthor;
        NbtCompound? filteredPages = nbt.GetCompound("filtered_pages");

        var pages = new List<BookContentPage>(pageList?.Count ?? 0);
        if (pageList is not null)
            for (int i = 0; i < pageList.Count; i++)
            {
                if (pageList[i] is not NbtString raw)
                    continue;

                Component? filtered = null;
                if (filteredPages is not null &&
                    filteredPages.TryGet(i.ToString(System.Globalization.CultureInfo.InvariantCulture), out NbtString? filteredRaw))
                    filtered = ReadLegacyPage(filteredRaw.Value, signed);

                pages.Add(new BookContentPage(ReadLegacyPage(raw.Value, signed), filtered));
            }

        if (!signed)
            return new BookContent(false, null, null, null, 0, false, pages);

        string? filteredTitle = nbt.TryGet("filtered_title", out NbtString? filteredTitleTag) ? filteredTitleTag.Value : null;
        return new BookContent(
            IsSigned: true,
            titleTag?.Value ?? string.Empty,
            filteredTitle,
            authorTag?.Value ?? string.Empty,
            nbt.GetInt("generation"),
            nbt.GetBool("resolved"),
            pages);
    }

    // A legacy signed page is a JSON component string; an unsigned page is plain text. Servers and older worlds are not consistent about this, so a signed page that is not parseable JSON falls back to plain text rather than faulting the stack.
    private static Component ReadLegacyPage(string raw, bool signed)
    {
        if (!signed || !LooksLikeJson(raw))
            return Component.Text(raw);

        try
        {
            return ComponentJson.Parse(raw, ComponentWireEra.Legacy);
        }
        catch (ComponentFormatException)
        {
            return Component.Text(raw);
        }
    }

    private static bool LooksLikeJson(string raw)
    {
        foreach (char c in raw)
        {
            if (char.IsWhiteSpace(c))
                continue;

            return c is '{' or '[' or '"';
        }

        return false;
    }
}
