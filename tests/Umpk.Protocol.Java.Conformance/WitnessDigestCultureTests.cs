using System.Globalization;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

public sealed class WitnessDigestCultureTests
{
    [Theory]
    [InlineData(110)]
    [InlineData(776)]
    public void CanonicalValuesDoNotDependOnCulture(int protocol)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (ResolvedWitness witness in Witnesses.For(protocol).Values)
            {
                BoundPacketCodec codec = Witnesses.Bind(protocol, witness.Key)!;
                object packet = codec.Decode(witness.Payload, Witnesses.Context(protocol));
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                string invariant = WitnessDigest.Canonical(packet);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sr-Latn-RS");
                string localized = WitnessDigest.Canonical(packet);
                Assert.Equal("sr-Latn-RS", CultureInfo.CurrentCulture.Name);
                Assert.Equal(invariant, localized);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
