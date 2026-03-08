using System;
using System.Collections.Generic;
using System.Text;
using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;
using Content.Server.Power.Components;
using Content.Server.PowerCell;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Popups;
using Content.Shared.PowerCell;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Cybernetics;
using Content.Goobstation.Shared.Overlays;
using Content.Server.Body.Components;
using Content.Shared.Body.Components;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Systems;
using Content.Goobstation.Maths.FixedPoint;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentNeuroInterfaceSystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly TraumaSystem _trauma = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan OverloadDamageInterval = TimeSpan.FromSeconds(1);
    private const float DefaultNeuroLoadWithoutInterface = 5f;
    private const string NeuroLoadOverloadIdentifier = "NeuroLoadOverload";
    private const float NeuroOverloadDamagePerSecond = 0.1f;
    private readonly Dictionary<EntityUid, TimeSpan> _nextUiUpdate = new();
    private TimeSpan _nextOverloadDamageSweep = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, ComponentRemove>(OnRemoved);

        Subs.BuiEvents<AugmentNeuroInterfaceComponent>(NeuroInterfaceUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<NeuroInterfaceToggleAugmentMessage>(OnToggleAugment);
            subs.Event<NeuroInterfaceBulkToggleMessage>(OnBulkToggle);
        });
    }

    private void OnInit(Entity<AugmentNeuroInterfaceComponent> ent, ref ComponentInit args)
    {
        if (string.IsNullOrWhiteSpace(ent.Comp.InterfaceCode))
            ent.Comp.InterfaceCode = GenerateHexCode();
    }

    private void OnUiOpened(Entity<AugmentNeuroInterfaceComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (_augment.GetBody(ent) is not { } body || body != args.Actor)
            return;

        UpdateUi(ent, body);
        _nextUiUpdate[ent.Owner] = _timing.CurTime + UiUpdateInterval;
    }

    private void OnRemoved(Entity<AugmentNeuroInterfaceComponent> ent, ref ComponentRemove args)
    {
        _nextUiUpdate.Remove(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        if (now >= _nextOverloadDamageSweep)
        {
            _nextOverloadDamageSweep = now + OverloadDamageInterval;
            var bodyQuery = EntityQueryEnumerator<BodyComponent>();
            while (bodyQuery.MoveNext(out var bodyUid, out _))
            {
                ApplyOverloadDamage(bodyUid);
            }
        }

        var query = EntityQueryEnumerator<AugmentNeuroInterfaceComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_augment.GetBody(uid) is not { } body)
                continue;

            if (!_ui.IsUiOpen(uid, NeuroInterfaceUiKey.Key))
                continue;

            if (_nextUiUpdate.TryGetValue(uid, out var next) && next > now)
                continue;

            _nextUiUpdate[uid] = now + UiUpdateInterval;
            UpdateUi((uid, comp), body);
        }
    }

    private void OnToggleAugment(Entity<AugmentNeuroInterfaceComponent> ent, ref NeuroInterfaceToggleAugmentMessage msg)
    {
        if (_augment.GetBody(ent) is not { } body || body != msg.Actor)
            return;

        var target = GetEntity(msg.Augment);

        if (target == ent.Owner)
            return;

        if (TryComp<OrganComponent>(target, out var organ)
            && organ.Body == body
            && IsControllableOrgan(target))
        {
            ToggleOrgan(body, target, organ, msg.Enable, true);
        }
        else if (TryComp<BodyPartComponent>(target, out var part)
            && part.Body == body
            && IsControllablePart(target))
        {
            TogglePart(body, target, part, msg.Enable, true);
        }
        else
        {
            return;
        }

        UpdatePowerDraw(body);
        UpdateUi(ent, body);
    }

    private void UpdateUi(Entity<AugmentNeuroInterfaceComponent> ent, EntityUid body)
    {
        var augments = BuildAugmentList(body);
        var (powerSourceName, powerOutputPerSecond) = GetPowerSourceInfo(body);
        var powerConsumptionPerSecond = _augmentPower.GetBodyDraw(body);

        var hasBattery = false;
        var charge = 0f;
        var maxCharge = 0f;

        if (_augmentPower.GetBodyAugment(body) is { } slot
            && _powerCell.TryGetBatteryFromSlot(slot.Owner, out _, out BatteryComponent? battery))
        {
            hasBattery = true;
            charge = battery.CurrentCharge;
            maxCharge = battery.MaxCharge;
        }

        var state = new NeuroInterfaceBuiState(
            ent.Comp.InterfaceCode,
            powerSourceName,
            powerOutputPerSecond,
            powerConsumptionPerSecond,
            hasBattery,
            charge,
            maxCharge,
            GetCurrentNeuroLoad(body),
            ent.Comp.MaxNeuroLoad,
            augments);
        _ui.SetUiState(ent.Owner, NeuroInterfaceUiKey.Key, state);
    }

    private void OnBulkToggle(Entity<AugmentNeuroInterfaceComponent> ent, ref NeuroInterfaceBulkToggleMessage msg)
    {
        if (_augment.GetBody(ent) is not { } body || body != msg.Actor)
            return;

        switch (msg.Target)
        {
            case NeuroInterfaceBulkTarget.Implants:
                ToggleAllOrgans(body, msg.Enable);
                break;
            case NeuroInterfaceBulkTarget.Limbs:
                ToggleAllParts(body, msg.Enable);
                break;
            case NeuroInterfaceBulkTarget.All:
                ToggleAllOrgans(body, msg.Enable);
                ToggleAllParts(body, msg.Enable);
                break;
        }

        UpdatePowerDraw(body);
        UpdateUi(ent, body);
    }

    private List<NeuroInterfaceAugmentEntry> BuildAugmentList(EntityUid body)
    {
        var entries = new List<NeuroInterfaceAugmentEntry>();

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            var category = GetCategory(partComp);

            if (IsControllablePart(partUid))
            {
                var partEnabled = partComp.Enabled && !HasComp<AugmentNeuroManuallyDisabledComponent>(partUid);
                var partName = Name(partUid);
                var partStatus = GetStatus(partUid, partComp);
                var partDescription = GetEntityDescription(partUid);
                var (partPassivePower, partActivePower, partPassiveNeuro, partActiveNeuro) = GetAugmentMetrics(partUid);

                entries.Add(new NeuroInterfaceAugmentEntry(
                    GetNetEntity(partUid),
                    GetNetEntity(partUid),
                    category,
                    partName,
                    partEnabled,
                    partComp.CanEnable,
                    false,
                    partStatus,
                    partDescription,
                    partPassivePower,
                    partActivePower,
                    partPassiveNeuro,
                    partActiveNeuro));
            }

            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsControllableOrgan(organUid))
                    continue;

                var enabled = organComp.Enabled && !HasComp<AugmentNeuroManuallyDisabledComponent>(organUid);
                var canToggle = organComp.CanEnable && !HasComp<AugmentNeuroInterfaceComponent>(organUid);
                var canConfigure = HasComp<AugmentComponent>(organUid) && HasComp<AugmentNeuroConfigurableComponent>(organUid);
                var name = Name(organUid);
                var status = GetStatus(organUid, organComp, body);
                var description = GetEntityDescription(organUid);
                var (passivePower, activePower, passiveNeuro, activeNeuro) = GetAugmentMetrics(organUid);

                entries.Add(new NeuroInterfaceAugmentEntry(
                    GetNetEntity(organUid),
                    GetNetEntity(partUid),
                    category,
                    name,
                    enabled,
                    canToggle,
                    canConfigure,
                    status,
                    description,
                    passivePower,
                    activePower,
                    passiveNeuro,
                    activeNeuro));
            }
        }

        entries.Sort((a, b) =>
        {
            var indexA = GetCategoryRank(a.Category);
            var indexB = GetCategoryRank(b.Category);

            if (indexA != indexB)
                return indexA.CompareTo(indexB);

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return entries;
    }

    private static int GetCategoryRank(NeuroInterfaceBodyCategory category)
    {
        return category switch
        {
            NeuroInterfaceBodyCategory.Head => 0,
            NeuroInterfaceBodyCategory.Torso => 1,
            NeuroInterfaceBodyCategory.RightArm => 2,
            NeuroInterfaceBodyCategory.LeftArm => 3,
            NeuroInterfaceBodyCategory.RightHand => 4,
            NeuroInterfaceBodyCategory.LeftHand => 5,
            NeuroInterfaceBodyCategory.Groin => 6,
            NeuroInterfaceBodyCategory.RightLeg => 7,
            NeuroInterfaceBodyCategory.LeftLeg => 8,
            NeuroInterfaceBodyCategory.RightFoot => 9,
            NeuroInterfaceBodyCategory.LeftFoot => 10,
            _ => int.MaxValue,
        };
    }

    private NeuroInterfaceBodyCategory GetCategory(BodyPartComponent part)
    {
        return (part.PartType, part.Symmetry) switch
        {
            (BodyPartType.Head, _) => NeuroInterfaceBodyCategory.Head,
            (BodyPartType.Chest, _) => NeuroInterfaceBodyCategory.Torso,
            (BodyPartType.Arm, BodyPartSymmetry.Right) => NeuroInterfaceBodyCategory.RightArm,
            (BodyPartType.Arm, BodyPartSymmetry.Left) => NeuroInterfaceBodyCategory.LeftArm,
            (BodyPartType.Hand, BodyPartSymmetry.Right) => NeuroInterfaceBodyCategory.RightHand,
            (BodyPartType.Hand, BodyPartSymmetry.Left) => NeuroInterfaceBodyCategory.LeftHand,
            (BodyPartType.Groin, _) => NeuroInterfaceBodyCategory.Groin,
            (BodyPartType.Leg, BodyPartSymmetry.Right) => NeuroInterfaceBodyCategory.RightLeg,
            (BodyPartType.Leg, BodyPartSymmetry.Left) => NeuroInterfaceBodyCategory.LeftLeg,
            (BodyPartType.Foot, BodyPartSymmetry.Right) => NeuroInterfaceBodyCategory.RightFoot,
            (BodyPartType.Foot, BodyPartSymmetry.Left) => NeuroInterfaceBodyCategory.LeftFoot,
            _ => NeuroInterfaceBodyCategory.Torso,
        };
    }

    private void UpdatePowerDraw(EntityUid body)
    {
        if (_augmentPower.GetBodyAugment(body) is { } slot)
            _augmentPower.UpdateDrawRate(slot.Owner);
    }

    private NeuroInterfaceAugmentStatus GetStatus(EntityUid organUid, OrganComponent organ, EntityUid body)
    {
        if (!organ.Enabled || HasComp<AugmentNeuroManuallyDisabledComponent>(organUid))
            return NeuroInterfaceAugmentStatus.Disabled;

        if (IsEmpBlocked(organUid))
            return NeuroInterfaceAugmentStatus.Deactivated;

        if (RequiresPower(organUid) && !HasAugmentPower(body))
            return NeuroInterfaceAugmentStatus.NoPower;

        return NeuroInterfaceAugmentStatus.Enabled;
    }

    private NeuroInterfaceAugmentStatus GetStatus(EntityUid partUid, BodyPartComponent part)
    {
        if (!part.Enabled || HasComp<AugmentNeuroManuallyDisabledComponent>(partUid))
            return NeuroInterfaceAugmentStatus.Disabled;

        if (IsEmpBlocked(partUid))
            return NeuroInterfaceAugmentStatus.Deactivated;

        return NeuroInterfaceAugmentStatus.Enabled;
    }

    private bool RequiresPower(EntityUid uid)
    {
        if (TryComp<AugmentPowerConfigComponent>(uid, out var powerConfig) && !powerConfig.RequiresPower)
            return false;

        if (TryComp<AugmentMovementSpeedComponent>(uid, out var movement)
            && movement.RequiresPower
            && movement.PowerDraw > 0f)
        {
            return true;
        }

        if (TryComp<AugmentItemPanelComponent>(uid, out var itemPanel)
            && itemPanel.RequiresPower
            && (itemPanel.ExtendPowerCost > 0f || itemPanel.RetractPowerCost > 0f))
        {
            return true;
        }

        if (TryComp<AugmentVisionComponent>(uid, out var vision)
            && vision.RequiresPower)
        {
            if (vision.PowerDraw > 0f)
                return true;

            foreach (var draw in vision.ActivePowerDrawByType.Values)
            {
                if (draw > 0f)
                    return true;
            }
        }

        if (TryComp<AugmentPowerDrawComponent>(uid, out var powerDraw)
            && powerDraw.Draw > 0f)
        {
            return true;
        }

        return false;
    }

    private bool HasAugmentPower(EntityUid body)
    {
        if (_augmentPower.GetBodyAugment(body) is not { } slot)
            return false;

        if (!TryComp<PowerCellDrawComponent>(slot, out var draw))
            return false;

        return _powerCell.HasDrawCharge(slot, draw);
    }

    private void ToggleOrgan(EntityUid body, EntityUid target, OrganComponent organ, bool enable, bool showPopups)
    {
        if (!organ.CanEnable)
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-cannot-toggle"), body, body, PopupType.SmallCaution);
            return;
        }

        var manuallyDisabled = HasComp<AugmentNeuroManuallyDisabledComponent>(target);

        if (enable)
        {
            if (IsEmpBlocked(target))
            {
                if (showPopups)
                    _popup.PopupEntity(Loc.GetString("neuro-interface-popup-emp-blocked"), body, body, PopupType.SmallCaution);
                return;
            }

            var enabledEv = new OrganEnableChangedEvent(true);
            RaiseLocalEvent(target, ref enabledEv);

            if (!manuallyDisabled)
                return;

            RemComp<AugmentNeuroManuallyDisabledComponent>(target);
            var restoredEv = new AugmentManuallyRestoredEvent(body);
            RaiseLocalEvent(target, ref restoredEv);
            return;
        }

        var disabledEv = new OrganEnableChangedEvent(false);
        RaiseLocalEvent(target, ref disabledEv);

        if (manuallyDisabled)
            return;

        EnsureComp<AugmentNeuroManuallyDisabledComponent>(target);
        var manualDisabledEv = new AugmentManuallyDisabledEvent(body);
        RaiseLocalEvent(target, ref manualDisabledEv);
    }

    private void TogglePart(EntityUid body, EntityUid target, BodyPartComponent part, bool enable, bool showPopups)
    {
        if (!part.CanEnable)
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-cannot-toggle"), body, body, PopupType.SmallCaution);
            return;
        }

        var manuallyDisabled = HasComp<AugmentNeuroManuallyDisabledComponent>(target);

        if (enable)
        {
            if (IsEmpBlocked(target))
            {
                if (showPopups)
                    _popup.PopupEntity(Loc.GetString("neuro-interface-popup-emp-blocked"), body, body, PopupType.SmallCaution);
                return;
            }

            var enabledEv = new BodyPartEnableChangedEvent(true);
            RaiseLocalEvent(target, ref enabledEv);

            if (!manuallyDisabled)
                return;

            RemComp<AugmentNeuroManuallyDisabledComponent>(target);
            return;
        }

        var disabledEv = new BodyPartEnableChangedEvent(false);
        RaiseLocalEvent(target, ref disabledEv);

        if (manuallyDisabled)
            return;

        EnsureComp<AugmentNeuroManuallyDisabledComponent>(target);
    }

    private void ToggleAllOrgans(EntityUid body, bool enable)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsControllableOrgan(organUid))
                    continue;

                if (HasComp<AugmentNeuroInterfaceComponent>(organUid))
                    continue;

                ToggleOrgan(body, organUid, organComp, enable, false);
            }
        }
    }

    private void ToggleAllParts(EntityUid body, bool enable)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            if (!IsControllablePart(partUid))
                continue;

            TogglePart(body, partUid, partComp, enable, false);
        }
    }

    private (string SourceName, float OutputPerSecond) GetPowerSourceInfo(EntityUid body)
    {
        var output = 0f;
        var firstSourceName = string.Empty;
        var sourceCount = 0;

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentBioReactorComponent>(organUid, out var reactor))
                    continue;

                if (!organComp.Enabled)
                    continue;

                if (HasComp<AugmentEmpDisabledComponent>(organUid))
                    continue;

                if (HasComp<AugmentNeuroManuallyDisabledComponent>(organUid))
                    continue;

                if (reactor.ChargeRate <= 0f)
                    continue;

                output += reactor.ChargeRate;
                sourceCount++;
                if (sourceCount == 1)
                    firstSourceName = Name(organUid);
            }
        }

        if (sourceCount <= 0)
            return (Loc.GetString("neuro-interface-window-source-none"), 0f);

        if (sourceCount == 1)
            return (firstSourceName, output);

        return (Loc.GetString("neuro-interface-window-source-multiple", ("count", sourceCount)), output);
    }

    private bool IsControllableOrgan(EntityUid organUid)
    {
        return HasComp<AugmentComponent>(organUid) || HasComp<CyberneticsComponent>(organUid);
    }

    private bool IsControllablePart(EntityUid partUid)
    {
        return HasComp<CyberneticsComponent>(partUid);
    }

    private bool IsEmpBlocked(EntityUid uid)
    {
        return HasComp<AugmentEmpDisabledComponent>(uid)
               || (TryComp<CyberneticsComponent>(uid, out var cyber) && cyber.Disabled);
    }

    private (float Current, float Max) GetNeuroLoad(EntityUid body)
    {
        TryGetNeuroLoadLimit(body, out var maxLoad);
        return (GetCurrentNeuroLoad(body), maxLoad);
    }

    private float GetCurrentNeuroLoad(EntityUid body)
    {
        var load = 0f;

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            if (IsPartActive(partUid, partComp))
                load += GetPassiveNeuroLoad(partUid);

            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsOrganActive(organUid, organComp))
                    continue;

                load += GetPassiveNeuroLoad(organUid);

                if (TryComp<AugmentItemPanelComponent>(organUid, out var panel) && panel.IsEquipped)
                    load += panel.EquippedNeuroLoad;

                if (TryComp<AugmentVisionComponent>(organUid, out var vision))
                    load += GetActiveVisionNeuroLoad(body, vision);
            }
        }

        return load;
    }

    private float GetPassiveNeuroLoad(EntityUid uid)
    {
        return TryComp<AugmentNeuroLoadComponent>(uid, out var load) ? load.PassiveLoad : 0f;
    }

    private (float PassivePower, float ActivePower, float PassiveNeuroLoad, float ActiveNeuroLoad) GetAugmentMetrics(EntityUid uid)
    {
        var passivePower = 0f;
        var activePower = 0f;
        var passiveNeuro = GetPassiveNeuroLoad(uid);
        var activeNeuro = 0f;

        if (TryComp<AugmentMovementSpeedComponent>(uid, out var movement)
            && movement.RequiresPower
            && movement.PowerDraw > 0f)
        {
            passivePower = Math.Max(passivePower, movement.PowerDraw);
        }

        if (TryComp<AugmentItemPanelComponent>(uid, out var panel))
        {
            if (panel.RequiresPower)
                activePower = Math.Max(activePower, Math.Max(panel.ExtendPowerCost, panel.RetractPowerCost));

            activeNeuro = Math.Max(activeNeuro, panel.EquippedNeuroLoad);
        }

        if (TryComp<AugmentVisionComponent>(uid, out var vision))
        {
            if (vision.RequiresPower)
                passivePower = Math.Max(passivePower, vision.PowerDraw);

            var hasToggleableType = false;
            foreach (var draw in vision.ActivePowerDrawByType.Values)
            {
                if (draw > activePower)
                    activePower = draw;
            }

            foreach (var type in vision.GetAllVisionTypes())
            {
                if (AugmentVisionComponent.IsToggleable(type))
                {
                    hasToggleableType = true;
                    break;
                }
            }

            if (hasToggleableType && activePower <= 0f && vision.RequiresPower && vision.PowerDraw > 0f)
                activePower = vision.PowerDraw;

            foreach (var load in vision.ActiveNeuroLoadByType.Values)
            {
                if (load > activeNeuro)
                    activeNeuro = load;
            }
        }

        if (TryComp<AugmentPowerDrawComponent>(uid, out var powerDraw)
            && powerDraw.Draw > 0f)
        {
            passivePower = Math.Max(passivePower, powerDraw.Draw);
        }

        return (passivePower, activePower, passiveNeuro, activeNeuro);
    }

    private string GetEntityDescription(EntityUid uid)
    {
        if (!TryComp<Robust.Shared.GameObjects.MetaDataComponent>(uid, out var meta))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(meta.EntityDescription))
        {
            return meta.EntityDescription;
        }

        if (meta.EntityPrototype != null
            && !string.IsNullOrWhiteSpace(meta.EntityPrototype.Description))
        {
            return meta.EntityPrototype.Description;
        }

        return string.Empty;
    }

    private bool IsPartActive(EntityUid partUid, BodyPartComponent part)
    {
        return part.Enabled
               && !HasComp<AugmentNeuroManuallyDisabledComponent>(partUid)
               && !IsEmpBlocked(partUid);
    }

    private bool IsOrganActive(EntityUid organUid, OrganComponent organ)
    {
        return organ.Enabled
               && !HasComp<AugmentNeuroManuallyDisabledComponent>(organUid)
               && !IsEmpBlocked(organUid);
    }

    private float GetActiveVisionNeuroLoad(EntityUid body, AugmentVisionComponent vision)
    {
        var load = 0f;

        foreach (var type in vision.GetAllVisionTypes())
        {
            if (!AugmentVisionComponent.IsToggleable(type))
                continue;

            if (!IsVisionTypeActive(body, type))
                continue;

            load += vision.GetActiveNeuroLoad(type);
        }

        return load;
    }

    private bool IsVisionTypeActive(EntityUid body, AugmentVisionType type)
    {
        return type switch
        {
            AugmentVisionType.NightVision => TryComp<NightVisionComponent>(body, out var nv) && nv.IsActive,
            AugmentVisionType.ThermalVision => TryComp<ThermalVisionComponent>(body, out var tv) && tv.IsActive,
            _ => false,
        };
    }

    private bool TryGetNeuroLoadLimit(EntityUid body, out float maxLoad)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentNeuroInterfaceComponent>(organUid, out var neuro))
                    continue;

                maxLoad = neuro.MaxNeuroLoad;
                return true;
            }
        }

        maxLoad = DefaultNeuroLoadWithoutInterface;
        return true;
    }

    private void ApplyOverloadDamage(EntityUid body)
    {
        var (currentLoad, maxLoad) = GetNeuroLoad(body);

        var overload = currentLoad - maxLoad;
        if (overload <= 0f)
            return;

        if (!TryComp<BodyComponent>(body, out var bodyComp)
            || !_body.TryGetBodyOrganEntityComps<BrainComponent>((body, bodyComp), out var brains))
        {
            return;
        }

        var damage = (FixedPoint2)(overload * NeuroOverloadDamagePerSecond * (float) OverloadDamageInterval.TotalSeconds);
        if (damage <= 0)
            return;

        var effectOwner = GetNeuroLoadEffectOwner(body);
        foreach (var brain in brains)
        {
            ApplyBrainIntegrityDamage(brain, effectOwner, damage);
        }
    }

    private void ApplyBrainIntegrityDamage(Entity<BrainComponent, OrganComponent> brain, EntityUid effectOwner, FixedPoint2 damage)
    {
        if (damage <= 0)
            return;

        var currentIntegrity = brain.Comp2.OrganIntegrity;
        var desiredIntegrity = FixedPoint2.Max(FixedPoint2.Zero, currentIntegrity - damage);
        if (desiredIntegrity == currentIntegrity)
            return;

        var sumWithoutOverload = FixedPoint2.Zero;
        foreach (var (key, value) in brain.Comp2.IntegrityModifiers)
        {
            if (key == (NeuroLoadOverloadIdentifier, effectOwner))
                continue;

            sumWithoutOverload += value;
        }

        var overloadContribution = desiredIntegrity - sumWithoutOverload;

        // TraumaSystem ignores attempts to set/create zero modifiers.
        // Force clamped 0 integrity when target is 0 and no other modifiers keep integrity above 0.
        if (desiredIntegrity <= 0 && overloadContribution == 0)
            overloadContribution = -1;

        if (overloadContribution == 0)
        {
            _trauma.TryRemoveOrganDamageModifier(brain.Owner, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2);
            return;
        }

        if (!_trauma.TrySetOrganDamageModifier(brain.Owner, overloadContribution, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2))
        {
            if (!_trauma.TryCreateOrganDamageModifier(brain.Owner, overloadContribution, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2))
                _trauma.TryChangeOrganDamageModifier(brain.Owner, overloadContribution, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2);
        }
    }

    private EntityUid GetNeuroLoadEffectOwner(EntityUid body)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, partComp))
            {
                if (HasComp<AugmentNeuroInterfaceComponent>(organUid))
                    return organUid;
            }
        }

        return body;
    }

    private string GenerateHexCode(int length = 8)
    {
        const string symbols = "0123456789ABCDEF";
        var builder = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            builder.Append(symbols[_random.Next(symbols.Length)]);
        }

        return builder.ToString();
    }
}


