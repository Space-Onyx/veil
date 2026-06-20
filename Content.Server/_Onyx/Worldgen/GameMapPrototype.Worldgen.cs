using Content.Server.Worldgen.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Maps;

public sealed partial class GameMapPrototype
{
    [DataField("worldgenEnabled")]
    public bool WorldgenEnabled { get; private set; } = true;

    [DataField("worldgenConfig")]
    public ProtoId<WorldgenConfigPrototype>? WorldgenConfig { get; private set; }

    [DataField("disabledGridSpawnGroups")]
    public HashSet<string> DisabledGridSpawnGroups { get; private set; } = new();
}
