namespace Umpk.Text;

/// <summary>Push-model sink for <see cref="StyledRun"/>s. <see cref="ComponentFlattener"/> calls <see cref="Accept"/> once per resolved run in document order instead of building a list, so a struct implementation can devirtualize through the generic <c>Flatten&lt;TSink&gt;</c> overload and flatten allocation-free. Implementations must be reference-semantic: a class, or a struct that only ever mutates state reached through a reference-typed field (for example a wrapped <see cref="System.Collections.Generic.List{T}"/>), since the sink is passed by value through the recursive walk and any state stored directly in struct fields would not be observable by the caller after the walk returns.</summary>
public interface IStyledRunSink
{
    /// <summary>Called once per emitted run, in document order. Never called for an empty run.</summary>
    void Accept(in StyledRun run);
}
