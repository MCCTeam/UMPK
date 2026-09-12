namespace Umpk.Text;

/// <summary>A visitor over the closed <see cref="ComponentContent"/> union, enabling custom renderers without runtime type checks. Each method handles one content kind; dispatch is by <see cref="ComponentContent.Accept{TResult}"/>.</summary>
/// <typeparam name="TResult">The value each visit produces.</typeparam>
public interface IComponentContentVisitor<out TResult>
{
    /// <summary>Visits literal text content.</summary>
    TResult VisitText(TextContent content);

    /// <summary>Visits translatable content.</summary>
    TResult VisitTranslatable(TranslatableContent content);

    /// <summary>Visits score content.</summary>
    TResult VisitScore(ScoreContent content);

    /// <summary>Visits selector content.</summary>
    TResult VisitSelector(SelectorContent content);

    /// <summary>Visits keybind content.</summary>
    TResult VisitKeybind(KeybindContent content);

    /// <summary>Visits NBT content.</summary>
    TResult VisitNbt(NbtContent content);
}
