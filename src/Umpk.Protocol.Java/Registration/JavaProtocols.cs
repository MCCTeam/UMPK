namespace Umpk.Protocol.Java;

/// <summary>The wire protocol numbers of every supported Java version, named by their Minecraft release. This is the single place a protocol number is given a name; the declarative packet timelines reference these constants so a version boundary reads as a release rather than a bare integer. Protocol numbers are totally ordered and chronological across the whole supported set (47 &lt; 107 &lt; ... &lt; 776), which is what makes a per-packet, protocol-ordered timeline able to resolve every wire form.</summary>
internal static class JavaProtocols
{
    /// <summary>1.8.</summary>
    public const int V1_8 = 47;

    /// <summary>1.9.</summary>
    public const int V1_9 = 107;

    /// <summary>1.9.1.</summary>
    public const int V1_9_1 = 108;

    /// <summary>1.9.2.</summary>
    public const int V1_9_2 = 109;

    /// <summary>1.9.4.</summary>
    public const int V1_9_4 = 110;

    /// <summary>1.10.</summary>
    public const int V1_10 = 210;

    /// <summary>1.11.</summary>
    public const int V1_11 = 315;

    /// <summary>1.11.2.</summary>
    public const int V1_11_2 = 316;

    /// <summary>1.12.</summary>
    public const int V1_12 = 335;

    /// <summary>1.12.1.</summary>
    public const int V1_12_1 = 338;

    /// <summary>1.12.2.</summary>
    public const int V1_12_2 = 340;

    /// <summary>1.13.</summary>
    public const int V1_13 = 393;

    /// <summary>1.13.1.</summary>
    public const int V1_13_1 = 401;

    /// <summary>1.13.2.</summary>
    public const int V1_13_2 = 404;

    /// <summary>1.14.</summary>
    public const int V1_14 = 477;

    /// <summary>1.14.1.</summary>
    public const int V1_14_1 = 480;

    /// <summary>1.14.2.</summary>
    public const int V1_14_2 = 485;

    /// <summary>1.14.3.</summary>
    public const int V1_14_3 = 490;

    /// <summary>1.14.4.</summary>
    public const int V1_14_4 = 498;

    /// <summary>1.15.</summary>
    public const int V1_15 = 573;

    /// <summary>1.15.1.</summary>
    public const int V1_15_1 = 575;

    /// <summary>1.15.2.</summary>
    public const int V1_15_2 = 578;

    /// <summary>1.16.</summary>
    public const int V1_16 = 735;

    /// <summary>1.16.1.</summary>
    public const int V1_16_1 = 736;

    /// <summary>1.16.2.</summary>
    public const int V1_16_2 = 751;

    /// <summary>1.16.3.</summary>
    public const int V1_16_3 = 753;

    /// <summary>1.16.4.</summary>
    public const int V1_16_4 = 754;

    /// <summary>1.17.</summary>
    public const int V1_17 = 755;

    /// <summary>1.17.1.</summary>
    public const int V1_17_1 = 756;

    /// <summary>1.18.</summary>
    public const int V1_18 = 757;

    /// <summary>1.18.2.</summary>
    public const int V1_18_2 = 758;

    /// <summary>1.19.</summary>
    public const int V1_19 = 759;

    /// <summary>1.19.1.</summary>
    public const int V1_19_1 = 760;

    /// <summary>1.19.3.</summary>
    public const int V1_19_3 = 761;

    /// <summary>1.19.4.</summary>
    public const int V1_19_4 = 762;

    /// <summary>1.20.</summary>
    public const int V1_20 = 763;

    /// <summary>1.20.2.</summary>
    public const int V1_20_2 = 764;

    /// <summary>1.20.3.</summary>
    public const int V1_20_3 = 765;

    /// <summary>1.20.5.</summary>
    public const int V1_20_5 = 766;

    /// <summary>1.21.</summary>
    public const int V1_21 = 767;

    /// <summary>1.21.2.</summary>
    public const int V1_21_2 = 768;

    /// <summary>1.21.4.</summary>
    public const int V1_21_4 = 769;

    /// <summary>1.21.5.</summary>
    public const int V1_21_5 = 770;

    /// <summary>1.21.6.</summary>
    public const int V1_21_6 = 771;

    /// <summary>1.21.7.</summary>
    public const int V1_21_7 = 772;

    /// <summary>1.21.9.</summary>
    public const int V1_21_9 = 773;

    /// <summary>1.21.11.</summary>
    public const int V1_21_11 = 774;

    /// <summary>26.1.</summary>
    public const int V26_1 = 775;

    /// <summary>26.2.</summary>
    public const int V26_2 = 776;
}
