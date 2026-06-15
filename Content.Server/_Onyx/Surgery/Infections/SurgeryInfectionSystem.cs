using Content.Goobstation.Shared.Disease.Components;
using Content.Goobstation.Shared.Disease.Systems;
using Content.Shared._Onyx.Surgery.Infections;
using Content.Shared._Shitmed.Medical.Surgery;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Infections;

public sealed class SurgeryInfectionSystem : EntitySystem
{
    private static readonly EntProtoId SurgicalInfection = "DiseaseSurgicalSiteInfection";
    private static readonly TimeSpan InfectionAttemptCooldown = TimeSpan.FromSeconds(30);
    private const float MinimumComplexity = 12f;
    private const float MaximumComplexity = 24f;

    [Dependency] private readonly SharedDiseaseSystem _disease = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurgeryTargetComponent, SurgeryInfectionAttemptEvent>(OnInfectionAttempt);
    }

    private void OnInfectionAttempt(Entity<SurgeryTargetComponent> ent, ref SurgeryInfectionAttemptEvent args)
    {
        if (TryComp<DiseaseCarrierComponent>(ent, out var carrier))
        {
            foreach (var diseaseUid in carrier.Diseases.ContainedEntities)
            {
                if (HasComp<SurgicalSiteInfectionComponent>(diseaseUid))
                    return;
            }
        }

        var cooldown = EnsureComp<SurgeryInfectionCooldownComponent>(ent);
        if (_timing.CurTime < cooldown.NextAttempt)
            return;

        cooldown.NextAttempt = _timing.CurTime + InfectionAttemptCooldown;

        if (!_random.Prob(args.Chance))
            return;

        var complexity = _random.NextFloat(MinimumComplexity, MaximumComplexity);
        var generatedDisease = _disease.MakeRandomDisease(SurgicalInfection, complexity);
        if (generatedDisease == null)
            return;

        if (!_disease.TryInfect(ent.Owner, generatedDisease.Value))
            QueueDel(generatedDisease.Value);
    }
}

[RegisterComponent]
public sealed partial class SurgeryInfectionCooldownComponent : Component
{
    [ViewVariables]
    public TimeSpan NextAttempt;
}
