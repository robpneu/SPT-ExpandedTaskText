using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace ExpandedTaskText;

public record EttMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.friedengineer.ett";
    public string Name { get; init; } = "Expanded Task Text";
    public string Author { get; init; } = "Cj, FriedEngineer";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("2.1.0");
    public Range SptVersion { get; init; } = new("~4.1");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/robpneu/SPT-ExpandedTaskText-CSharp";
    public bool HasPrepatcher { get; init; } = false;
    public string License { get; init; } = "MIT";
    
    public static readonly string ResourcesDirectory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Resources");
}