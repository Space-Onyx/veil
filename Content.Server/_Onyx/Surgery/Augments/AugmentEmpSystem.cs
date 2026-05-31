using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Server.Emp;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentEmpSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private readonly HashSet<EntityUid> _disabledAugments = new();
    private readonly List<EntityUid> _disabledAugmentBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstalledAugmentsComponent, EmpPulseEvent>(OnEmpPulse);
        SubscribeLocalEvent<AugmentEmpDisabledComponent, ComponentStartup>(OnDisabledStartup);
        SubscribeLocalEvent<AugmentEmpDisabledComponent, ComponentShutdown>(OnDisabledShutdown);
    }

    private void OnDisabledStartup(Entity<AugmentEmpDisabledComponent> ent, ref ComponentStartup args)
    {
        _disabledAugments.Add(ent.Owner);
    }

    private void OnDisabledShutdown(Entity<AugmentEmpDisabledComponent> ent, ref ComponentShutdown args)
    {
        _disabledAugments.Remove(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_disabledAugments.Count == 0)
            return;

        _disabledAugmentBuffer.Clear();
        foreach (var uid in _disabledAugments)
        {
            _disabledAugmentBuffer.Add(uid);
        }

        foreach (var uid in _disabledAugmentBuffer)
        {
            if (!TryComp<AugmentEmpDisabledComponent>(uid, out var disabled)
                || !HasComp<AugmentEmpComponent>(uid))
            {
                _disabledAugments.Remove(uid);
                continue;
            }

            if (disabled.DisabledUntil > _timing.CurTime)
                continue;

            _disabledAugments.Remove(uid);
            RemComp<AugmentEmpDisabledComponent>(uid);

            if (AugmentEnhancementHelpers.TryGetOwningBody(uid, EntityManager) is not { } body)
                continue;

            var ev = new AugmentEmpRestoredEvent(body);
            RaiseLocalEvent(uid, ref ev);
        }
    }

    private void OnEmpPulse(Entity<InstalledAugmentsComponent> ent, ref EmpPulseEvent args)
    {
        var affected = false;

        foreach (var enhancement in AugmentEnhancementHelpers.EnumerateEnhancements(ent.Owner, _body, _itemSlots, EntityManager))
        {
            if (!TryComp<AugmentEmpComponent>(enhancement, out var empComp))
                continue;

            if (!CanBeEmpDisabled(enhancement, empComp))
                continue;

            var duration = _random.NextFloat(empComp.MinDisableDuration, empComp.MaxDisableDuration);
            var disabledComp = EnsureComp<AugmentEmpDisabledComponent>(enhancement);
            disabledComp.DisabledUntil = _timing.CurTime + TimeSpan.FromSeconds(duration);
            _disabledAugments.Add(enhancement);

            var ev = new AugmentEmpDisabledEvent(ent.Owner);
            RaiseLocalEvent(enhancement, ref ev);

            affected = true;
        }

        if (affected)
        {
            args.Affected = true;
            _popup.PopupEntity(Loc.GetString("augment-emp-disabled"), ent.Owner, ent.Owner, PopupType.LargeCaution);
        }
    }

    private bool CanBeEmpDisabled(EntityUid enhancement, AugmentEmpComponent empComp)
    {
        if (!empComp.EmpVulnerable)
            return false;

        if (!AugmentBehaviorPolicyHelpers.IsAffectedByEmp(enhancement, EntityManager))
            return false;

        return !HasComp<AugmentEmpDisabledComponent>(enhancement);
    }
}

