using System.Net;
using System.Net.Http;

namespace Umpk.Auth.Tests.Fakes;

/// <summary>Scripts the canned responses for the full Microsoft chain (XBL, XSTS, login_with_xbox, entitlement, profile) onto a <see cref="ScriptedHttpHandler"/>. The MSA token step is scripted separately by each flow test (device-code, browser, refresh).</summary>
public static class MicrosoftChainScript
{
    public const string ProfileId = "4566e69fc90748ee8d71d7ba5aa00d20";
    public const string ProfileName = "Dinnerbone";
    public const string McAccessToken = "MC_ACCESS_TOKEN_SECRET";

    public static ScriptedHttpHandler AddChain(this ScriptedHttpHandler handler)
    {
        handler.On(HttpMethod.Post, "user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
            """{"Token":"XBL_TOKEN","DisplayClaims":{"xui":[{"uhs":"USERHASH"}]}}""");

        handler.On(HttpMethod.Post, "xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
            """{"Token":"XSTS_TOKEN","DisplayClaims":{"xui":[{"uhs":"USERHASH"}]}}""");

        handler.On(HttpMethod.Post, "authentication/login_with_xbox", HttpStatusCode.OK,
            $$"""{"access_token":"{{McAccessToken}}","expires_in":86400}""");

        handler.On(HttpMethod.Get, "entitlements/mcstore", HttpStatusCode.OK,
            """{"items":[{"name":"product_minecraft"},{"name":"game_minecraft"}]}""");

        handler.On(HttpMethod.Get, "minecraft/profile", HttpStatusCode.OK,
            $$"""{"id":"{{ProfileId}}","name":"{{ProfileName}}"}""");

        return handler;
    }
}
