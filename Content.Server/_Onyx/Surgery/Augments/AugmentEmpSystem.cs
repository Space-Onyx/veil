using Content.Goobstation.Shared.Augments;
using Content.Server.Emp;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Organ;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentEmpSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstalledAugmentsComponent, EmpPulseEvent>(OnEmpPulse);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AugmentEmpDisabledComponent, AugmentEmpComponent, OrganComponent>();
        while (query.MoveNext(out var uid, out var disabled, out _, out var organ))
        {
            if (disabled.DisabledUntil > _timing.CurTime)
                continue;

            RemComp<AugmentEmpDisabledComponent>(uid);

            if (organ.Body is not { } body)
                continue;

            var ev = new AugmentEmpRestoredEvent(body);
            RaiseLocalEvent(uid, ref ev);
        }
    }

    private void OnEmpPulse(Entity<InstalledAugmentsComponent> ent, ref EmpPulseEvent args)
    {
        var affected = false;

        foreach (var netEnt in ent.Comp.InstalledAugments)
        {
            var aug = GetEntity(netEnt);

            if (!TryComp<AugmentEmpComponent>(aug, out var empComp))
                continue;

            if (!empComp.EmpVulnerable)
                continue;

            if (HasComp<AugmentEmpDisabledComponent>(aug))
                continue;

            var duration = _random.NextFloat(empComp.MinDisableDuration, empComp.MaxDisableDuration);
            var disabledComp = EnsureComp<AugmentEmpDisabledComponent>(aug);
            disabledComp.DisabledUntil = _timing.CurTime + TimeSpan.FromSeconds(duration);

            var ev = new AugmentEmpDisabledEvent(ent.Owner);
            RaiseLocalEvent(aug, ref ev);

            affected = true;
        }

        if (affected)
        {
            args.Affected = true;
            _popup.PopupEntity(Loc.GetString("augment-emp-disabled"), ent.Owner, ent.Owner, PopupType.LargeCaution);
        }
    }
}
