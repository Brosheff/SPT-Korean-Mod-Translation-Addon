using SPTarkov.Server.Core.Models.Spt.Mod;

namespace SPT_Mod_Korean_Server;

public record Metadata : IModMetadata
{
    public string ModGuid { get; init; } = "local.spt41.modkorean.directmessages";
    public string Name { get; init; } = "SPT Mod Korean Direct Messages";
    public string Author { get; init; } = "Local";
    public List<string>? Contributors { get; init; } = [];
    public SemanticVersioning.Version Version { get; init; } = new("1.0.1");
    public SemanticVersioning.Range SptVersion { get; init; } = new("=4.1.6");
    public List<string>? Incompatibilities { get; init; } = [];
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = [];
    public string? Url { get; init; }
    public bool HasPrepatcher { get; init; } = false;
    public string License { get; init; } = "MIT";
}
