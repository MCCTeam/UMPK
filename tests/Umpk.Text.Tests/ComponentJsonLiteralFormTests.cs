using System.Text;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Text.Tests;

/// <summary>The 1.20.3 literal-form boundary, which is a DIFFERENT axis from <see cref="ComponentWireEra"/> (1.21.5) and cannot be derived from it.</summary>
/// <remarks>
/// <para>The wire format permits a collapsed bare string only from 1.20.3:</para>
/// <list type="bullet">
/// <item>Before 1.20.3, serializers always return a JSON object and represent a literal with a
/// <c>text</c> property. There is no bare-string branch.</item>
/// <item>From 1.20.3, a plain-text component with no siblings and an empty style may collapse to its
/// string value.</item>
/// <item>And on the wire: the recorded protocol-764 named-zombie <c>set_entity_data</c> frame carries a
/// 21-byte JSON payload, which is <c>{"text":"TestZombie"}</c>. The collapsed form is 12 bytes. See <c>EntityDataTypeIdTransitionTests</c>, which now pins that capture byte-for-byte.</item>
/// </list>
/// <para>Both forms decode to the same component, but choosing the wrong form breaks byte-exact wire fidelity.</para>
/// </remarks>
public sealed class ComponentJsonLiteralFormTests
{
    /// <summary>The width assertion. A round trip agrees with itself under either form, so the byte COUNT is what separates them: 13 for the vanilla object spelling against 4 for the collapse.</summary>
    [Fact]
    public void PureLiteral_WidthIsTheFormsOwn()
    {
        Component literal = Component.Text("hi");

        Assert.Equal(
            "{\"text\":\"hi\"}",
            ComponentJson.ToJsonString(literal, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));
        Assert.Equal(13, ComponentJson.ToUtf8Bytes(literal, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object).Length);

        Assert.Equal(
            "\"hi\"",
            ComponentJson.ToJsonString(literal, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Collapsed));
        Assert.Equal(4, ComponentJson.ToUtf8Bytes(literal, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Collapsed).Length);
    }

    /// <summary>The collapse predicate is plain-text content with no siblings and an empty style. A component that fails it anywhere in the tree takes the OBJECT spelling on both sides of the boundary, so the collapse axis exists for the pure literal alone. The cases below are chosen so that no NESTED pure literal exists either; nested ones do move, which is what <see cref="NestedLiterals_TakeTheFormToo"/> asserts.</summary>
    /// <remarks>For non-literals, neither form may degenerate to a bare string and both must carry the same component.</remarks>
    /// <param name="component">A component with no pure literal anywhere in it.</param>
    [Theory]
    [MemberData(nameof(NotPureLiterals))]
    public void NonLiteral_IsUnaffectedByTheForm(Component component)
    {
        string objectForm = ComponentJson.ToJsonString(component, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object);
        string collapsedForm = ComponentJson.ToJsonString(component, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Collapsed);

        Assert.StartsWith("{", objectForm, StringComparison.Ordinal);
        Assert.StartsWith("{", collapsedForm, StringComparison.Ordinal);
        Assert.Equal(component, ComponentJson.Parse(objectForm, ComponentWireEra.Legacy));
        Assert.Equal(component, ComponentJson.Parse(collapsedForm, ComponentWireEra.Legacy));
    }

    public static TheoryData<Component> NotPureLiterals => new()
    {
        new Component(new TextContent("hi"), new Style { Bold = true }),
        new Component(
            new TextContent("hi"),
            Style.Empty,
            [new Component(new TextContent("there"), new Style { Italic = true })]),
        new Component(new TranslatableContent("k", null, []), Style.Empty),
    };

    /// <summary>The form reaches every nested component through recursive serialization, including <c>extra</c>, translation arguments, and hover-text payloads. A form that stopped at the root would leave the common case of a styled parent wrapping plain children wrong.</summary>
    /// <remarks>Both expectations here moved when the pre-1.20.3 key order landed, from content-first to style-then-extra-then-content. They were NOT rewritten to match this library's output: the new strings use the required style-then-extra-then-content order. See <c>ComponentJsonVanillaShapeTests</c> for the full set of pinned shapes.</remarks>
    [Fact]
    public void NestedLiterals_TakeTheFormToo()
    {
        var withChild = new Component(
            new TextContent("a"),
            new Style { Bold = true },
            [Component.Text("b")]);

        Assert.Equal(
            "{\"bold\":true,\"extra\":[{\"text\":\"b\"}],\"text\":\"a\"}",
            ComponentJson.ToJsonString(withChild, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));

        var withHover = new Component(
            new TextContent("a"),
            new Style { HoverEvent = new HoverShowText(Component.Text("b")) });

        Assert.Equal(
            "{\"hoverEvent\":{\"action\":\"show_text\",\"contents\":{\"text\":\"b\"}},\"text\":\"a\"}",
            ComponentJson.ToJsonString(withHover, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object));
    }

    /// <summary>The two forms decode to the same component but are not interchangeable as bytes. The test asserts both properties because a symmetric round trip can observe only the decoded value.</summary>
    [Fact]
    public void TheTwoForms_AreByteDistinctAndValueIdentical()
    {
        Component literal = Component.Text("hi");
        byte[] objectForm = ComponentJson.ToUtf8Bytes(literal, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object);
        byte[] collapsed = ComponentJson.ToUtf8Bytes(literal, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Collapsed);

        Assert.NotEqual(objectForm, collapsed);
        Assert.NotEqual(objectForm.Length, collapsed.Length);

        // Both read back, on both eras: vanilla accepts the bare string wherever a component is expected, and always has. Dropping the collapse from the WRITER does not narrow the READER.
        Assert.Equal(literal, ComponentJson.Parse(Encoding.UTF8.GetString(objectForm), ComponentWireEra.Legacy));
        Assert.Equal(literal, ComponentJson.Parse(Encoding.UTF8.GetString(collapsed), ComponentWireEra.Legacy));
        Assert.Equal(literal, ComponentJson.Parse(Encoding.UTF8.GetString(objectForm), ComponentWireEra.Modern));
        Assert.Equal(literal, ComponentJson.Parse(Encoding.UTF8.GetString(collapsed), ComponentWireEra.Modern));
    }

    /// <summary>The default is the collapsed literal form used by the default <see cref="ComponentWireEra.Modern"/>. Every pre-765 wire call site opts in to <see cref="ComponentJsonLiteralForm.Object"/> explicitly instead.</summary>
    [Fact]
    public void DefaultForm_IsTheCollapse() =>
        Assert.Equal("\"hi\"", ComponentJson.ToJsonString(Component.Text("hi")));

    /// <summary>The NBT path is not part of this axis and must not be dragged into it. Network-NBT components only exist from 1.20.3, which is exactly where vanilla started collapsing, so <c>ComponentNbt.To</c> collapsing on every era it can be reached from is correct as it stands.</summary>
    [Fact]
    public void NbtPath_StillCollapses()
    {
        Umpk.Nbt.NbtTag tag = ComponentNbt.To(Component.Text("hi"), ComponentWireEra.Legacy);
        Assert.Equal("hi", Assert.IsType<Umpk.Nbt.NbtString>(tag).Value);
    }
}
