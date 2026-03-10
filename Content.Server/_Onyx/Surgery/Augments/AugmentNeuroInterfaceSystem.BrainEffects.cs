using System;
using System.Linq;
using System.Text;
using Content.Server.Body.Components;
using Content.Shared._Onyx.Speech;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Body.Organ;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed partial class AugmentNeuroInterfaceSystem
{
    private void OnRefreshMovementSpeed(EntityUid uid, MovementSpeedModifierComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        var stage = GetBrainPenaltyStage(uid);
        var slowdown = GetBrainSlowdownMultiplier(stage);
        if (slowdown >= 1f)
            return;

        args.ModifySpeed(slowdown, slowdown);
    }

    private void ApplyBrainIntegrityEffects(EntityUid body)
    {
        var newStage = GetBrainPenaltyStage(body);
        if (newStage >= BrainPenaltyStage.Below10)
            TryForceDeadByBrainFailure(body);

        var oldStage = _brainPenaltyStages.GetValueOrDefault(body, BrainPenaltyStage.None);
        if (newStage != oldStage)
        {
            _brainPenaltyStages[body] = newStage;
            var oldSlowdown = GetBrainSlowdownMultiplier(oldStage);
            var newSlowdown = GetBrainSlowdownMultiplier(newStage);
            if (!MathHelper.CloseTo(oldSlowdown, newSlowdown))
                _movementSpeed.RefreshMovementSpeedModifiers(body);
        }

        UpdateBrainDamageAccent(body, newStage);

        SetBrainDeactivationState(body, newStage >= BrainPenaltyStage.Below30);

        if (newStage < BrainPenaltyStage.Below30)
            return;

        ForceDisableAllAugments(body);

        var dropChance = GetDropChanceForStage(newStage);
        if (dropChance > 0f)
            TryDropHeldItemsFromHands(body, dropChance);
    }

    private void ForceDisableAllAugments(EntityUid body)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            if (IsControllablePart(partUid))
                EnsureComp<AugmentBrainDeactivatedComponent>(partUid);

            if (IsControllablePart(partUid) && partComp.CanEnable && partComp.Enabled)
            {
                var partDisable = new BodyPartEnableChangedEvent(false);
                RaiseLocalEvent(partUid, ref partDisable);
            }

            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsControllableOrgan(organUid))
                    continue;

                if (HasComp<AugmentNeuroInterfaceComponent>(organUid))
                    continue;

                EnsureComp<AugmentBrainDeactivatedComponent>(organUid);

                if (!organComp.CanEnable || !organComp.Enabled)
                    continue;

                var organDisable = new OrganEnableChangedEvent(false);
                RaiseLocalEvent(organUid, ref organDisable);
            }
        }

        UpdatePowerDraw(body);
    }

    private void SetBrainDeactivationState(EntityUid body, bool deactivated)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            if (IsControllablePart(partUid))
            {
                if (deactivated)
                    EnsureComp<AugmentBrainDeactivatedComponent>(partUid);
                else
                    RemComp<AugmentBrainDeactivatedComponent>(partUid);
            }

            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsControllableOrgan(organUid) || HasComp<AugmentNeuroInterfaceComponent>(organUid))
                    continue;

                if (deactivated)
                    EnsureComp<AugmentBrainDeactivatedComponent>(organUid);
                else
                    RemComp<AugmentBrainDeactivatedComponent>(organUid);
            }
        }
    }

    private void TryDropHeldItemsFromHands(EntityUid body, float dropChancePerHeldItem)
    {
        if (!TryComp<HandsComponent>(body, out var hands))
            return;

        var heldItems = _hands.EnumerateHeld((body, hands)).ToList();
        foreach (var held in heldItems)
        {
            if (!_random.Prob(dropChancePerHeldItem))
                continue;

            _hands.TryDrop((body, hands), held, checkActionBlocker: false);
        }
    }

    private float GetAdjustedMaxNeuroLoad(EntityUid body, EntityUid neuroInterfaceUid, float baseMaxLoad)
    {
        var moduleDelta = GetUniversalModuleMaxNeuroLoadDelta(neuroInterfaceUid);
        var adjustedBase = baseMaxLoad + moduleDelta;
        if (moduleDelta < 0f && baseMaxLoad > 0f)
            adjustedBase = MathF.Max(1f, adjustedBase);
        else
            adjustedBase = MathF.Max(0f, adjustedBase);

        if (GetBrainPenaltyStage(body) < BrainPenaltyStage.Below80)
            return adjustedBase;

        return MathF.Max(0f, adjustedBase - BrainNeuroLoadPenalty);
    }

    private float GetUniversalModuleMaxNeuroLoadDelta(EntityUid neuroInterfaceUid)
    {
        return TryComp<AugmentUniversalModuleAccumulatorComponent>(neuroInterfaceUid, out var accumulator)
            ? accumulator.MaxNeuroLoadDelta
            : 0f;
    }

    private BrainPenaltyStage GetBrainPenaltyStage(EntityUid body)
    {
        if (!TryGetLowestBrainIntegrityRatio(body, out var ratio))
            return BrainPenaltyStage.None;

        if (ratio <= 0f)
            return BrainPenaltyStage.Destroyed;
        if (ratio <= BrainPenaltyThreshold10)
            return BrainPenaltyStage.Below10;
        if (ratio <= BrainPenaltyThreshold20)
            return BrainPenaltyStage.Below20;
        if (ratio <= BrainPenaltyThreshold30)
            return BrainPenaltyStage.Below30;
        if (ratio <= BrainPenaltyThreshold50)
            return BrainPenaltyStage.Below50;
        if (ratio <= BrainPenaltyThreshold60)
            return BrainPenaltyStage.Below60;
        if (ratio <= BrainPenaltyThreshold80)
            return BrainPenaltyStage.Below80;

        return BrainPenaltyStage.None;
    }

    private bool TryGetLowestBrainIntegrityRatio(EntityUid body, out float ratio)
    {
        ratio = 1f;

        if (!TryComp<BodyComponent>(body, out var bodyComp)
            || !_body.TryGetBodyOrganEntityComps<BrainComponent>((body, bodyComp), out var brains)
            || brains.Count == 0)
        {
            return false;
        }

        var anyValidBrain = false;
        foreach (var brain in brains)
        {
            var cap = (float)brain.Comp2.IntegrityCap;
            if (cap <= 0f)
                continue;

            anyValidBrain = true;
            var current = (float)brain.Comp2.OrganIntegrity;
            var currentRatio = Math.Clamp(current / cap, 0f, 1f);
            ratio = MathF.Min(ratio, currentRatio);
        }

        if (!anyValidBrain)
        {
            ratio = 1f;
            return false;
        }

        return true;
    }

    private void TryForceDeadByBrainFailure(EntityUid body)
    {
        if (!TryComp<MobStateComponent>(body, out var mobState))
            return;

        if (mobState.CurrentState == MobState.Dead)
            return;

        _mobState.ChangeMobState(body, MobState.Dead, mobState);
    }

    private float GetBrainSlowdownMultiplier(BrainPenaltyStage stage)
    {
        if (stage >= BrainPenaltyStage.Below50)
            return BrainSlowdownMultiplier50;

        if (stage >= BrainPenaltyStage.Below60)
            return BrainSlowdownMultiplier60;

        return 1f;
    }

    private float GetDropChanceForStage(BrainPenaltyStage stage)
    {
        if (stage >= BrainPenaltyStage.Below20)
            return CriticalDropChancePerHeldItem20;

        if (stage >= BrainPenaltyStage.Below30)
            return CriticalDropChancePerHeldItem30;

        return 0f;
    }

    private void UpdateBrainDamageAccent(EntityUid body, BrainPenaltyStage stage)
    {
        if (stage < BrainPenaltyStage.Below60)
        {
            RemComp<BrainDamagedAccentComponent>(body);
            return;
        }

        var accent = EnsureComp<BrainDamagedAccentComponent>(body);
        var replaceChance = BrainAccentMessageReplaceChance60;
        var swapChance = BrainAccentLetterSwapChance60;

        if (stage >= BrainPenaltyStage.Below50)
        {
            replaceChance = BrainAccentMessageReplaceChance50;
            swapChance = BrainAccentLetterSwapChance50;
        }

        if (MathHelper.CloseTo(accent.MessageReplaceChance, replaceChance)
            && MathHelper.CloseTo(accent.LetterSwapChance, swapChance))
        {
            return;
        }

        accent.MessageReplaceChance = replaceChance;
        accent.LetterSwapChance = swapChance;
        Dirty(body, accent);
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
