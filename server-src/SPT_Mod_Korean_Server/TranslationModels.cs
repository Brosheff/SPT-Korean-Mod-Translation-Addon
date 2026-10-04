using System.Text.Json.Serialization;

namespace SPT_Mod_Korean_Server;

internal sealed class ModTexts
{
    public int schema_version { get; set; }
    public string? target_spt { get; set; }
    public string? mod_id { get; set; }
    public string? mod_name { get; set; }
    public Dictionary<string, List<TextRule>> channels { get; set; } = [];
}

internal sealed class TextRule
{
    public string? id { get; set; }
    public string? source { get; set; }
    public string? translation { get; set; }
    public string? translation_bilingual { get; set; }
    public string? match_mode { get; set; }
    public string? exclusive_group { get; set; }
    public bool enabled { get; set; } = true;
    public string? trader_id { get; set; }
}

internal sealed class CultureSyncRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    [JsonPropertyName("culture")]
    public string? Culture { get; set; }
}

internal sealed record UntranslatedDirectMessage(string trader_id, string source);
