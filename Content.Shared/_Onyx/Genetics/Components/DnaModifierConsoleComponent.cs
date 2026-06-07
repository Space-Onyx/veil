using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Genetics;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DnaModifierConsoleComponent : Component
{
    public const string ScannerPort = "MedicalScannerSender";

    [ViewVariables]
    public EntityUid? GeneticScanner = null;

    [DataField("maxDistance")]
    public float MaxDistance = 4f;

    public bool GeneticScannerInRange = true;

    public TimeSpan NextUpdate;
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan LastInjectorTime;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan LastSubjectInjectTime;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan InjectorCooldown = TimeSpan.FromMinutes(2);

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan SubjectInjectCooldown = TimeSpan.FromMinutes(2);

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<ReagentPrototype> IdentityStabilizerReagent = "GeneticStabilizer";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public FixedPoint2 IdentityStabilizerCost = FixedPoint2.New(10);

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<ReagentPrototype> EvolutionStabilizerReagent = "EvolutionCatalyst";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public FixedPoint2 EvolutionStabilizerCost = FixedPoint2.New(5);

    [DataField("clickSound"), ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");
}
