using System.Collections.Generic;
using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;
using Content.Server.Emp;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Enums;
using Robust.Shared.Physics;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptImplantFailureSystem : EntitySystem
{
    private const LookupFlags BodyLookupFlags =
        LookupFlags.Dynamic | LookupFlags.StaticSundries | LookupFlags.Sensors;

    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EmpSystem _emp = default!;

    private readonly HashSet<Entity<InstalledAugmentsComponent>> _bodyCandidates = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CyberDeckScriptImplantFailureComponent, CyberDeckScriptExecutedEvent>(OnExecuted);
    }

    private void OnExecuted(Entity<CyberDeckScriptImplantFailureComponent> ent, ref CyberDeckScriptExecutedEvent args)
    {
        if (ent.Comp.Range <= 0f)
            return;

        var minDuration = MathF.Max(0f, MathF.Min(ent.Comp.MinDisableDuration, ent.Comp.MaxDisableDuration));
        var maxDuration = MathF.Max(minDuration, ent.Comp.MaxDisableDuration);
        var duration = _random.NextFloat(minDuration, maxDuration);
        var disabledUntil = _timing.CurTime + TimeSpan.FromSeconds(duration);

        _bodyCandidates.Clear();
        _lookup.GetEntitiesInRange<InstalledAugmentsComponent>(
            Transform(args.Body).Coordinates,
            ent.Comp.Range,
            _bodyCandidates,
            BodyLookupFlags);

        foreach (var candidate in _bodyCandidates)
        {
            var body = candidate.Owner;

            if (!ent.Comp.AffectSelf && body == args.Body)
                continue;

            if (!_interaction.InRangeUnobstructed(args.Body, body, ent.Comp.Range, CollisionGroup.Opaque))
                continue;

            if (!TryDisableBodyEnhancements(body, disabledUntil))
                continue;

            _popup.PopupEntity(Loc.GetString("augment-emp-disabled"), body, body, PopupType.LargeCaution);
        }
    }

    private bool TryDisableBodyEnhancements(EntityUid body, TimeSpan disabledUntil)
    {
        var affectedAny = false;

        foreach (var enhancement in AugmentEnhancementHelpers.EnumerateEnhancements(body, _body, _itemSlots, EntityManager))
        {
            if (!Exists(enhancement))
                continue;

            if (TryComp<AugmentEmpComponent>(enhancement, out _))
            {
                if (DisableAugment(enhancement, body, disabledUntil))
                    affectedAny = true;

                continue;
            }

            if (TryComp<CyberneticsComponent>(enhancement, out _))
            {
                if (DisableCybernetics(enhancement, disabledUntil))
                    affectedAny = true;
            }
        }

        return affectedAny;
    }

    private bool DisableAugment(EntityUid augment, EntityUid body, TimeSpan disabledUntil)
    {
        var hadEmpDisabled = TryComp<AugmentEmpDisabledComponent>(augment, out var disabled);
        disabled ??= EnsureComp<AugmentEmpDisabledComponent>(augment);

        var changed = false;
        if (disabled.DisabledUntil < disabledUntil)
        {
            disabled.DisabledUntil = disabledUntil;
            Dirty(augment, disabled);
            changed = true;
        }

        if (hadEmpDisabled)
            return changed;

        var ev = new AugmentEmpDisabledEvent(body);
        RaiseLocalEvent(augment, ref ev);
        return true;
    }

    private bool DisableCybernetics(EntityUid cybernetic, TimeSpan disabledUntil)
    {
        if (!TryComp<CyberneticsComponent>(cybernetic, out var cyberComp))
            return false;

        if (cyberComp.Disabled)
            return false;

        var duration = MathF.Max(0.01f, (float) (disabledUntil - _timing.CurTime).TotalSeconds);
        _emp.DoEmpEffects(cybernetic, 0f, duration);

        return TryComp<CyberneticsComponent>(cybernetic, out cyberComp) && cyberComp.Disabled;
    }
}
