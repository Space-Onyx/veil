using Content.Goobstation.Shared.Augments;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Organ;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentReactorSystem : EntitySystem
{
    private const float UpdateInterval = 0.25f;

    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private float _updateAccumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateAccumulator += frameTime;
        if (_updateAccumulator < UpdateInterval)
            return;

        var elapsed = _updateAccumulator;
        _updateAccumulator = 0f;

        var query = EntityQueryEnumerator<AugmentReactorComponent, OrganComponent>();
        while (query.MoveNext(out var uid, out var reactor, out var organ))
        {
            if (organ.Body is not { } body)
                continue;

            if (_mobState.IsDead(body))
                continue;

            if (IsReactorBlocked(uid))
                continue;

            if (!TryGetBodyBattery(body, out var batteryUid, out var battery))
                continue;

            var missingCharge = battery.MaxCharge - battery.CurrentCharge;
            if (missingCharge <= 0f)
                continue;

            if (!TryComp<HungerComponent>(body, out var hungerComp))
                continue;

            var chargeToGenerate = reactor.ChargeRate * elapsed;
            if (chargeToGenerate <= 0f)
                continue;

            if (reactor.HungerCostPerCharge > 0f)
            {
                if (_hunger.IsHungerBelowState(body, reactor.MinimumHungerThreshold, comp: hungerComp))
                    continue;

                chargeToGenerate = ClampChargeByHungerThreshold(
                    hungerComp,
                    reactor.MinimumHungerThreshold,
                    reactor.HungerCostPerCharge,
                    chargeToGenerate);
            }

            chargeToGenerate = MathF.Min(chargeToGenerate, missingCharge);
            if (chargeToGenerate <= 0f)
                continue;

            _battery.AddCharge(batteryUid, chargeToGenerate, battery);

            if (reactor.HungerCostPerCharge > 0f)
                _hunger.ModifyHunger(body, -chargeToGenerate * reactor.HungerCostPerCharge, hungerComp);
        }
    }

    private bool IsReactorBlocked(EntityUid reactorUid)
    {
        return HasComp<AugmentEmpDisabledComponent>(reactorUid)
               || HasComp<AugmentBrainDeactivatedComponent>(reactorUid)
               || HasComp<AugmentNeuroManuallyDisabledComponent>(reactorUid);
    }

    private bool TryGetBodyBattery(EntityUid body, out EntityUid batteryUid, out BatteryComponent battery)
    {
        batteryUid = default;
        battery = default!;

        if (_augmentPower.GetBodyAugment(body) is not { } slot)
            return false;

        if (!_powerCell.TryGetBatteryFromSlot(slot.Owner, out var foundUid, out BatteryComponent? foundBattery)
            || foundUid == null
            || foundBattery == null)
        {
            return false;
        }

        batteryUid = foundUid.Value;
        battery = foundBattery;
        return true;
    }

    private float ClampChargeByHungerThreshold(
        HungerComponent hungerComp,
        HungerThreshold minThreshold,
        float hungerCostPerCharge,
        float requestedCharge)
    {
        if (requestedCharge <= 0f || hungerCostPerCharge <= 0f)
            return 0f;

        var currentHunger = _hunger.GetHunger(hungerComp);
        var projectedHunger = currentHunger - requestedCharge * hungerCostPerCharge;
        if (_hunger.GetHungerThreshold(hungerComp, projectedHunger) >= minThreshold)
            return requestedCharge;

        var low = 0f;
        var high = requestedCharge;

        for (var i = 0; i < 10; i++)
        {
            var mid = (low + high) * 0.5f;
            projectedHunger = currentHunger - mid * hungerCostPerCharge;

            if (_hunger.GetHungerThreshold(hungerComp, projectedHunger) >= minThreshold)
                low = mid;
            else
                high = mid;
        }

        return low;
    }
}
