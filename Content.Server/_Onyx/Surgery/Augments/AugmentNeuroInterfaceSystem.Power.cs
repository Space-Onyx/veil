using Content.Goobstation.Shared.Augments;
using Content.Server.Power.Components;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.PowerCell;
using Content.Shared._Shitmed.Cybernetics;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed partial class AugmentNeuroInterfaceSystem
{
    private void UpdatePowerDraw(EntityUid body)
    {
        if (_augmentPower.GetBodyAugment(body) is { } slot)
            _augmentPower.UpdateDrawRate(slot.Owner);
    }

    private NeuroInterfaceAugmentStatus GetStatus(EntityUid organUid, OrganComponent organ, EntityUid body)
    {
        if (IsEmpBlocked(organUid))
            return NeuroInterfaceAugmentStatus.Deactivated;

        if (!organ.Enabled || HasComp<AugmentNeuroManuallyDisabledComponent>(organUid))
            return NeuroInterfaceAugmentStatus.Disabled;

        if (RequiresPower(organUid) && !HasAugmentPower(body))
            return NeuroInterfaceAugmentStatus.NoPower;

        return NeuroInterfaceAugmentStatus.Enabled;
    }

    private NeuroInterfaceAugmentStatus GetStatus(EntityUid partUid, BodyPartComponent part)
    {
        if (IsEmpBlocked(partUid))
            return NeuroInterfaceAugmentStatus.Deactivated;

        if (!part.Enabled || HasComp<AugmentNeuroManuallyDisabledComponent>(partUid))
            return NeuroInterfaceAugmentStatus.Disabled;

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

                if (IsEmpBlocked(organUid))
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
               || HasComp<AugmentBrainDeactivatedComponent>(uid)
               || (TryComp<CyberneticsComponent>(uid, out var cyber) && cyber.Disabled);
    }
}
