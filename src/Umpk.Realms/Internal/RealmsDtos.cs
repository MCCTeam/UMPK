using System.Text.Json.Serialization;

namespace Umpk.Realms.Internal;

/// <summary>Wire shape of a Realms error body: <c>reason</c>, <c>errorMsg</c>, and <c>errorCode</c>. <c>errorCode</c> tolerates a JSON string as well as a number; some Realms error paths encode it as a string.</summary>
internal sealed class RealmsErrorDto
{
    [JsonPropertyName("errorCode")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("errorMsg")]
    public string? ErrorMsg { get; set; }
}

/// <summary>Wire shape of <c>GET /worlds</c>: an array of server entries under <c>servers</c>.</summary>
internal sealed class RealmsWorldsResponseDto
{
    [JsonPropertyName("servers")]
    public List<RealmWorldDto>? Servers { get; set; }
}

/// <summary>Wire shape of a single Realms world entry within the worlds list.</summary>
internal sealed class RealmWorldDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("motd")]
    public string? Motd { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("ownerUUID")]
    public string? OwnerUuid { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("worldType")]
    public string? WorldType { get; set; }

    [JsonPropertyName("expired")]
    public bool Expired { get; set; }

    [JsonPropertyName("expiredTrial")]
    public bool ExpiredTrial { get; set; }

    [JsonPropertyName("daysLeft")]
    public int DaysLeft { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("activeSlot")]
    public int? ActiveSlot { get; set; }

    [JsonPropertyName("member")]
    public bool Member { get; set; }
}

/// <summary>Wire shape of the join endpoint response: the connection address plus optional pack fields.</summary>
internal sealed class RealmsJoinResponseDto
{
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("resourcePackUrl")]
    public string? ResourcePackUrl { get; set; }

    [JsonPropertyName("resourcePackHash")]
    public string? ResourcePackHash { get; set; }
}
