namespace Umpk.Auth;

/// <summary>Username/password pair for a Yggdrasil (authlib-injector) login, supplied by the host through <see cref="IAuthInteraction.GetYggdrasilCredentialsAsync"/>. The password is a secret and is excluded from <see cref="ToString"/>.</summary>
public sealed record YggdrasilCredentials(string Username, string Password)
{
    /// <summary>Redacts the password; prints only the username.</summary>
    public override string ToString() => $"YggdrasilCredentials {{ Username = {Username}, Password = <redacted> }}";
}
