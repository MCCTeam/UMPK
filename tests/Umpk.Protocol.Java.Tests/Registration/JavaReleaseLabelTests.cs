using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>Each era name resolves to the protocol it claims. The expected values are written as literals, independently of the constants under test: a test that derives its expectation from the value it checks is structurally blind. Written against the constants directly rather than through a name-to-value map, because a reflective map would put reflection in the verification layer and a hand-written one would compare one hand-written literal against another.</summary>
public sealed class JavaReleaseLabelTests
{
    [Fact]
    public void EveryWireLayoutNameIsTheProtocolItClaims()
    {
        Assert.Equal(107, JavaEras.Combat);
        Assert.Equal(393, JavaEras.Flattening);
        Assert.Equal(477, JavaEras.Palettes);
        Assert.Equal(755, JavaEras.Caves);
        Assert.Equal(759, JavaEras.ChatSigning);
        Assert.Equal(764, JavaEras.ConfigurationPhase);
        Assert.Equal(765, JavaEras.ComponentNbtTransport);
        Assert.Equal(766, JavaEras.ItemComponents);
        Assert.Equal(768, JavaEras.WideIds);
        Assert.Equal(770, JavaEras.ModernComponents);
    }
}
