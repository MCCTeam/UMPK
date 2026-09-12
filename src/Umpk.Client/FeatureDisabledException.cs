namespace Umpk.Client;

/// <summary>Thrown when a <see cref="ClientState"/> or action surface is accessed while its owning feature is disabled in <see cref="ClientFeatures"/>.</summary>
public sealed class FeatureDisabledException : InvalidOperationException
{
    /// <summary>Creates the exception naming the disabled feature.</summary>
    public FeatureDisabledException(string feature)
        : base($"The '{feature}' feature is disabled for this client; enable it via ConfigureFeatures.")
    {
        Feature = feature;
    }

    /// <summary>The feature that was disabled.</summary>
    public string Feature { get; }
}
