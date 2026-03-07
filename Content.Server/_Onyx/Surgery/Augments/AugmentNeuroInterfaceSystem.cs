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
using Content.Shared._Shitmed.Body.Organ;
using Robust.Server.GameObjects;
using Robust.Shared.Random;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentNeuroInterfaceSystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly NeuroInterfaceBodyCategory[] CategoryOrder =
    {
        NeuroInterfaceBodyCategory.Head,
        NeuroInterfaceBodyCategory.Torso,
        NeuroInterfaceBodyCategory.RightArm,
        NeuroInterfaceBodyCategory.LeftArm,
        NeuroInterfaceBodyCategory.RightHand,
        NeuroInterfaceBodyCategory.LeftHand,
        NeuroInterfaceBodyCategory.Groin,
        NeuroInterfaceBodyCategory.RightLeg,
        NeuroInterfaceBodyCategory.LeftLeg,
        NeuroInterfaceBodyCategory.RightFoot,
        NeuroInterfaceBodyCategory.LeftFoot,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, ComponentInit>(OnInit);

        Subs.BuiEvents<AugmentNeuroInterfaceComponent>(NeuroInterfaceUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<NeuroInterfaceToggleAugmentMessage>(OnToggleAugment);
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
    }

    private void OnToggleAugment(Entity<AugmentNeuroInterfaceComponent> ent, ref NeuroInterfaceToggleAugmentMessage msg)
    {
        if (_augment.GetBody(ent) is not { } body || body != msg.Actor)
            return;

        var target = GetEntity(msg.Augment);

        if (target == ent.Owner)
            return;

        if (!TryComp<InstalledAugmentsComponent>(body, out var installed) || !installed.InstalledAugments.Contains(msg.Augment))
            return;

        if (!TryComp<OrganComponent>(target, out var organ) || organ.Body != body)
            return;

        if (!organ.CanEnable)
        {
            _popup.PopupEntity(Loc.GetString("neuro-interface-popup-cannot-toggle"), body, body, PopupType.SmallCaution);
            return;
        }

        var manuallyDisabled = HasComp<AugmentNeuroManuallyDisabledComponent>(target);

        if (msg.Enable)
        {
            if (HasComp<AugmentEmpDisabledComponent>(target))
            {
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-emp-blocked"), body, body, PopupType.SmallCaution);
                return;
            }

            var enabledEv = new OrganEnableChangedEvent(true);
            RaiseLocalEvent(target, ref enabledEv);

            if (manuallyDisabled)
            {
                RemComp<AugmentNeuroManuallyDisabledComponent>(target);
                var restoredEv = new AugmentManuallyRestoredEvent(body);
                RaiseLocalEvent(target, ref restoredEv);
            }
        }
        else
        {
            var disabledEv = new OrganEnableChangedEvent(false);
            RaiseLocalEvent(target, ref disabledEv);

            if (!manuallyDisabled)
            {
                EnsureComp<AugmentNeuroManuallyDisabledComponent>(target);
                var manualDisabledEv = new AugmentManuallyDisabledEvent(body);
                RaiseLocalEvent(target, ref manualDisabledEv);
            }
        }

        UpdatePowerDraw(body);
        UpdateUi(ent, body);
    }

    private void UpdateUi(Entity<AugmentNeuroInterfaceComponent> ent, EntityUid body)
    {
        var augments = BuildAugmentList(body);

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

        _ui.SetUiState(ent.Owner, NeuroInterfaceUiKey.Key,
            new NeuroInterfaceBuiState(ent.Comp.InterfaceCode, hasBattery, charge, maxCharge, augments));
    }

    private List<NeuroInterfaceAugmentEntry> BuildAugmentList(EntityUid body)
    {
        var entries = new List<NeuroInterfaceAugmentEntry>();

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            var category = GetCategory(partComp);

            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!HasComp<AugmentComponent>(organUid))
                    continue;

                var enabled = organComp.Enabled && !HasComp<AugmentNeuroManuallyDisabledComponent>(organUid);
                var canToggle = organComp.CanEnable && !HasComp<AugmentNeuroInterfaceComponent>(organUid);
                var canConfigure = HasComp<AugmentNeuroConfigurableComponent>(organUid);
                var name = Name(organUid);
                var status = GetStatus(organUid, organComp);

                entries.Add(new NeuroInterfaceAugmentEntry(
                    GetNetEntity(organUid),
                    category,
                    name,
                    enabled,
                    canToggle,
                    canConfigure,
                    status));
            }
        }

        entries.Sort((a, b) =>
        {
            var indexA = Array.IndexOf(CategoryOrder, a.Category);
            var indexB = Array.IndexOf(CategoryOrder, b.Category);

            if (indexA != indexB)
                return indexA.CompareTo(indexB);

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return entries;
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

    private NeuroInterfaceAugmentStatus GetStatus(EntityUid organUid, OrganComponent organ)
    {
        if (!organ.Enabled || HasComp<AugmentNeuroManuallyDisabledComponent>(organUid))
            return NeuroInterfaceAugmentStatus.Disabled;

        if (HasComp<AugmentEmpDisabledComponent>(organUid))
            return NeuroInterfaceAugmentStatus.Deactivated;

        return NeuroInterfaceAugmentStatus.Enabled;
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


