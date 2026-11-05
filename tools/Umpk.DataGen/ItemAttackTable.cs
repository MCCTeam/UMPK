using System.Text.Json;

namespace Umpk.DataGen;

internal sealed record DefaultPiercing(bool Knockback, bool Dismounts, string? Sound, string? HitSound);
internal sealed record DefaultAttack(DefaultPiercing? Piercing, float? Charge);

internal sealed record ItemAttackTable(int Schema, IReadOnlyDictionary<string, DefaultAttack> Items)
{
    public static ItemAttackTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("item-attack.json", out JsonElement root))
            return null;
        Dictionary<string, DefaultAttack> items = [];
        foreach (JsonProperty entry in root.GetProperty("items").EnumerateObject())
        {
            JsonElement value = entry.Value;
            if (value.EnumerateObject().Any(field => field.Name is not ("piercing_weapon" or "minimum_attack_charge")))
                throw new DatasetException("item-attack.json has an unsupported component field");
            DefaultPiercing? piercing = null;
            JsonElement component = value.GetProperty("piercing_weapon");
            if (component.ValueKind != JsonValueKind.Null)
            {
                if (component.EnumerateObject().Any(field => field.Name is not ("deals_knockback" or "dismounts" or "sound" or "hit_sound")))
                    throw new DatasetException("item-attack.json has an unsupported piercing field");
                piercing = new DefaultPiercing(!component.TryGetProperty("deals_knockback", out JsonElement knockback) || knockback.GetBoolean(),
                    component.TryGetProperty("dismounts", out JsonElement dismounts) && dismounts.GetBoolean(),
                    component.TryGetProperty("sound", out JsonElement sound) ? sound.GetString() : null,
                    component.TryGetProperty("hit_sound", out JsonElement hit) ? hit.GetString() : null);
            }
            JsonElement charge = value.GetProperty("minimum_attack_charge");
            items.Add(entry.Name, new DefaultAttack(piercing, charge.ValueKind == JsonValueKind.Null ? null : charge.GetSingle()));
        }
        return new ItemAttackTable(root.GetProperty("schema").GetInt32(), items);
    }

    public void Validate(VersionData version, List<string> problems)
    {
        void Reject(string message) => problems.Add($"proto {version.Protocol}: item-attack.json {message}");
        if (Schema != 1)
            Reject("has an unsupported schema");
        if (!Items.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(version.Items.Select(item => item.Name)))
            Reject("must measure every item name exactly once");
        HashSet<string> components = [.. version.Components.Select(component => component.Id)];
        HashSet<string> sounds = version.Registries.TryGetValue("minecraft:sound_event", out var registry) ? [.. registry.Select(sound => sound.Name)] : [];
        foreach ((string item, DefaultAttack value) in Items)
        {
            if (value.Charge is { } charge && (!float.IsFinite(charge) || !components.Contains("minecraft:minimum_attack_charge")))
                Reject($"has an unavailable or invalid minimum-attack-charge component for '{item}'");
            if (value.Piercing is not { } piercing)
                continue;
            if (!components.Contains("minecraft:piercing_weapon"))
                Reject($"has an unavailable piercing component for '{item}'");
            foreach (string? sound in new[] { piercing.Sound, piercing.HitSound })
                if (sound is not null && !sounds.Contains(sound))
                    Reject($"has an unknown sound holder for '{item}'");
        }
    }
}
