// AuthOffline shows the offline identity UMPK bots use when no account is involved. It needs no network. It derives the same UUID a vanilla offline server assigns, so a bot keeps its identity across reconnects.
//
// Run it like this:
//
//   dotnet run --project samples/AuthOffline -- Steve
//
// Offline mode is not a flag. It is the absence of an authenticator. You build the client with UseProfile and never call UseAuthenticator. For a Microsoft login, see samples/AuthOnline. For a third party auth server, see samples/AuthThirdParty. See docs/guides/authentication.md for the full online flow.
using Umpk;
using Umpk.Auth;

if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: AuthOffline [username]");
    return 2;
}

string username = args.Length == 1 ? args[0] : "Steve";
if (username.Length is 0 or > 16)
{
    Console.Error.WriteLine("An offline username has 1 to 16 characters.");
    return 2;
}

// The UUID is MD5 of "OfflinePlayer:<name>", with version and variant bits set to match Java UUID.nameUUIDFromBytes. Same name, same UUID, every time.
Guid uuid = OfflineIdentity.ComputeUuid(username);
GameProfile profile = OfflineIdentity.ComputeProfile(username);
Console.WriteLine($"Username: {profile.Name}");
Console.WriteLine($"UUID:     {profile.Id}");
Console.WriteLine();

// Calling it twice gives the same answer. That stability is the feature. A server that persists per player data keys it by this id.
Guid again = OfflineIdentity.ComputeUuid(username);
Console.WriteLine($"Stable:   {uuid == again} (second call matches)");
Console.WriteLine();

// Different names give different ids. Case matters here, since the hash covers the exact string.
GameProfile other = OfflineIdentity.ComputeProfile(username == "Steve" ? "Alex" : "Steve");
Console.WriteLine($"Other:    {other.Name} -> {other.Id}");
Console.WriteLine();

// What to do with this profile. Offline bots hand it straight to the builder:
//
//   await using UmpkClient client = new UmpkClientBuilder()
//       .UseVersion(version)
//       .UseProfile(OfflineIdentity.ComputeProfile(username))
//       .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
//       .Build();
//
// Online bots do more. They log in through MinecraftAuthFlow, keep the JavaSession it returns, and hand the session profile plus an ISessionAuthenticator to the client with UseAuthenticator. The token store directory for that flow must live outside any repo working tree, and *.tok files must never be committed. See docs/guides/authentication.md for the full online flow.
Console.WriteLine("Use this profile with UmpkClientBuilder.UseProfile for offline servers.");
Console.WriteLine("Never pair it with UseAuthenticator. The two modes exclude each other.");

return 0;
