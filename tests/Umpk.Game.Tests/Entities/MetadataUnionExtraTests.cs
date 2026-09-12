using Umpk.Game.Entities;
using Xunit;

namespace Umpk.Game.Tests.Entities;

/// <summary><see cref="MetadataValueKind.Particles"/> stores a list of opaque particle payloads, while <see cref="MetadataValueKind.ResolvableProfile"/> stores a resolvable profile. These tests pin the factory/accessor contract and wrong-kind guards.</summary>
public sealed class MetadataUnionExtraTests
{
    [Fact]
    public void Particles_StoresAndReturns_TheOpaqueList()
    {
        object p0 = new();
        object p1 = new();
        MetadataValue value = MetadataValue.Particles([p0, p1]);

        Assert.Equal(MetadataValueKind.Particles, value.Kind);
        Assert.True(value.HasValue);
        IReadOnlyList<object> list = value.AsParticles();
        Assert.Equal(2, list.Count);
        Assert.Same(p0, list[0]);
        Assert.Same(p1, list[1]);
        Assert.Throws<InvalidOperationException>(() => value.AsResolvableProfile());
    }

    [Fact]
    public void ResolvableProfile_StoresAndReturns_TheProfile()
    {
        var profile = new ResolvableProfile(
            IsResolved: false,
            Id: null,
            Name: "Steve",
            Properties: [new Umpk.Game.Entities.ProfileProperty("textures", "value", null)],
            Skin: new ResolvableSkinPatch(Body: null, Cape: null, Elytra: null, SlimModel: true));

        MetadataValue value = MetadataValue.ResolvableProfile(profile);

        Assert.Equal(MetadataValueKind.ResolvableProfile, value.Kind);
        ResolvableProfile read = value.AsResolvableProfile();
        Assert.Same(profile, read);
        Assert.Equal("Steve", read.Name);
        Assert.True(read.Skin.SlimModel);
        Assert.Throws<InvalidOperationException>(() => value.AsParticles());
    }

    [Fact]
    public void Particles_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataValue.Particles(null!));
    }
}
