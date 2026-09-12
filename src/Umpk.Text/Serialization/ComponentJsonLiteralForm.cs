namespace Umpk.Text.Serialization;

/// <summary>Selects how <see cref="ComponentJson"/> ENCODES a pure literal (a <c>TextContent</c> with an empty style and no children). The format changed at 1.20.3, which is a different boundary from <see cref="ComponentWireEra"/> (1.21.5), so the two axes cannot be derived from one another.</summary>
/// <remarks>
/// <para>Decoding is unaffected because every era accepts both forms. Only the writer branches on this.</para>
/// <para>Before 1.20.3, a pure literal is always written as <c>{"text":"..."}</c>. From 1.20.3, a plain-text component with no siblings and an empty style collapses to a bare string. The boundary is also pinned on the wire: the recorded protocol-764 <c>set_entity_data</c> frame for a named zombie carries <c>01 15 7b 22 74 65 78 74 ...</c>, a present OPTIONAL_COMPONENT whose JSON is 0x15 = 21 bytes, which is <c>{"text":"TestZombie"}</c>. The collapsed form would have been 12 bytes.</para>
/// </remarks>
public enum ComponentJsonLiteralForm
{
    /// <summary>From 1.20.3, a pure literal is written as a bare JSON string. This is the form the login-disconnect reason and the status description take from 1.20.3 on, and it is the default because the default <see cref="ComponentWireEra"/> is likewise the modern one.</summary>
    Collapsed = 0,

    /// <summary>Before 1.20.3, a pure literal is written as <c>{"text":"..."}</c>, the same object form every other content type takes. Every JSON-carried component on protocol 764 and below uses this.</summary>
    Object = 1,
}
