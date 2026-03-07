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

public sealed class AugmentBioReactorSystem : EntitySystem
{
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AugmentBioReactorComponent, OrganComponent>();
        while (query.MoveNext(out var uid, out var reactor, out var organ))
        {
            if (organ.Body is not { } body)
                continue;

            if (_mobState.IsDead(body))
                continue;

            if (HasComp<AugmentEmpDisabledComponent>(uid))
                continue;

            if (_augmentPower.GetBodyAugment(body) is not { } slot)
                continue;

            if (!_powerCell.TryGetBatteryFromSlot(slot.Owner, out var batteryUid, out BatteryComponent? battery))
                continue;

            var missingCharge = battery.MaxCharge - battery.CurrentCharge;
            if (missingCharge <= 0f)
                continue;

            if (!TryComp<HungerComponent>(body, out var hungerComp))
                continue;

            var chargeToGenerate = reactor.ChargeRate * frameTime;
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

            _battery.AddCharge(batteryUid.Value, chargeToGenerate, battery);

            if (reactor.HungerCostPerCharge > 0f)
                _hunger.ModifyHunger(body, -chargeToGenerate * reactor.HungerCostPerCharge, hungerComp);
        }
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
