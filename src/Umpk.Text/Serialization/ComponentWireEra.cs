namespace Umpk.Text.Serialization;

/// <summary>Selects which wire-era encoding a component serializer uses for the constructs that changed shape across versions, chiefly click and hover events. The component tree itself is era-neutral; only the serializers branch on this.</summary>
public enum ComponentWireEra
{
    /// <summary>Pre-1.21.5 encoding. Click events are a flat <c>{"action","value"}</c> pair; hover events nest their payload under <c>contents</c> (with a legacy <c>value</c> fallback on decode), and <c>show_entity</c> uses the <c>type</c>/<c>id</c>/<c>name</c> field names.</summary>
    Legacy = 0,

    /// <summary>1.21.5+ encoding. Click events are dispatched on <c>action</c> with a per-action value field (<c>url</c>, <c>command</c>, <c>page</c>, and so on) and add <c>show_dialog</c>/<c>custom</c>; hover events inline their payload beside <c>action</c>, and <c>show_entity</c> uses the <c>id</c>/<c>uuid</c>/<c>name</c> field names.</summary>
    Modern = 1,
}
