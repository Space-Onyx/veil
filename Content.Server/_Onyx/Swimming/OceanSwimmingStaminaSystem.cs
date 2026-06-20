using Content.Goobstation.Common.Body.Components;
using Content.Shared._Onyx.Swimming.Components;
using Content.Shared._Onyx.Swimming.Events;
using Content.Shared._Onyx.Swimming.Systems;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Components;
using Content.Server.Body.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Swimming;

public sealed class OceanSwimmingStaminaSystem : EntitySystem
{
    private static readonly TimeSpan StaminaSuppressLeadTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StaminaSuppressRefreshThreshold = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan DrowningDamageInterval = TimeSpan.FromSeconds(1);
    private static readonly ProtoId<DamageTypePrototype> AsphyxiationDamageType = "Asphyxiation";

    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private DamageTypePrototype _asphyxiation = default!;

    public override void Initialize()
    {
        base.Initialize();

        _asphyxiation = _prototype.Index(AsphyxiationDamageType);
        SubscribeLocalEvent<OceanSwimmingComponent, ComponentShutdown>(OnSwimmingShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var suppressUntil = now + StaminaSuppressLeadTime;
        var refreshSuppressAt = now + StaminaSuppressRefreshThreshold;

        var query = EntityQueryEnumerator<
            OceanSwimmingComponent,
            InputMoverComponent,
            StaminaComponent,
            TransformComponent>();

        while (query.MoveNext(out var uid, out var swimming, out var mover, out var stamina, out var xform))
        {
            if (xform.MapUid is not { } mapUid ||
                !TryComp<OceanMapComponent>(mapUid, out var ocean))
            {
                continue;
            }

            var canSwim = OceanSwimmingStamina.CanSwim(swimming, stamina, ocean);
            var moving = mover.HasDirectionalMovement && mover.CanMove && canSwim;

            var staminaCost = MathF.Max(0f, ocean.StaminaCost);
            var staminaRecovery = MathF.Max(0f, ocean.StaminaRecovery);

            if (TryComp<SwimmingModifierComponent>(uid, out var modifier))
                staminaCost *= MathF.Max(0f, modifier.StaminaCostMultiplier);

            var sprint = new OceanSwimmingSprintEvent();
            RaiseLocalEvent(uid, sprint);

            UpdateSprintDrain(uid, swimming, stamina, sprint, moving);

            if (moving)
            {
                if (sprint.IsSprinting)
                    staminaCost *= MathF.Max(1f, ocean.SprintStaminaCostMultiplier);

                if (staminaCost > 0f)
                {
                    _stamina.TakeStaminaDamage(
                        uid,
                        staminaCost * frameTime,
                        stamina,
                        source: uid,
                        visual: false,
                        immediate: false,
                        logDamage: false);
                }
            }
            else if (stamina.StaminaDamage > 0f && staminaRecovery > 0f)
            {
                if (sprint.IsSprinting)
                    staminaRecovery /= MathF.Max(0.01f, sprint.StaminaRegenMultiplier);

                _stamina.TakeStaminaDamage(
                    uid,
                    -staminaRecovery * frameTime,
                    stamina,
                    source: uid,
                    visual: false,
                    immediate: false,
                    logDamage: false);
            }

            ApplyDrowning(uid, swimming, stamina, ocean, now);

            if (stamina.NextUpdate >= refreshSuppressAt)
                continue;

            stamina.NextUpdate = suppressUntil;
            Dirty(uid, stamina);
        }
    }

    private void UpdateSprintDrain(
        EntityUid uid,
        OceanSwimmingComponent swimming,
        StaminaComponent stamina,
        OceanSwimmingSprintEvent sprint,
        bool moving)
    {
        if (moving)
        {
            RestoreSprintDrain(uid, swimming, stamina);
            return;
        }

        if (!sprint.IsSprinting || sprint.StaminaDrainKey is not { } drainKey)
        {
            swimming.SuspendedSprintDrainKey = null;
            swimming.SuspendedSprintDrainRate = 0f;
            return;
        }

        if (!stamina.ActiveDrains.TryGetValue(drainKey, out var drain) || drain.DrainRate <= 0f)
            return;

        swimming.SuspendedSprintDrainKey = drainKey;
        swimming.SuspendedSprintDrainRate = drain.DrainRate;
        _stamina.ModifyStaminaDrain(uid, drainKey, 0f, stamina);
    }

    private void RestoreSprintDrain(
        EntityUid uid,
        OceanSwimmingComponent swimming,
        StaminaComponent stamina)
    {
        if (swimming.SuspendedSprintDrainKey is not { } drainKey)
            return;

        if (stamina.ActiveDrains.ContainsKey(drainKey))
        {
            _stamina.ModifyStaminaDrain(
                uid,
                drainKey,
                swimming.SuspendedSprintDrainRate,
                stamina);
        }

        swimming.SuspendedSprintDrainKey = null;
        swimming.SuspendedSprintDrainRate = 0f;
    }

    private void OnSwimmingShutdown(Entity<OceanSwimmingComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<StaminaComponent>(ent, out var stamina))
            RestoreSprintDrain(ent, ent.Comp, stamina);
    }

    private void ApplyDrowning(
        EntityUid uid,
        OceanSwimmingComponent swimming,
        StaminaComponent stamina,
        OceanMapComponent ocean,
        TimeSpan now)
    {
        if (!HasComp<RespiratorComponent>(uid) ||
            HasComp<BreathingImmunityComponent>(uid) ||
            HasComp<SpecialBreathingImmunityComponent>(uid) ||
            stamina.CritThreshold <= 0f)
        {
            return;
        }

        var staminaRemainingFraction = Math.Clamp(
            (stamina.CritThreshold - stamina.StaminaDamage) / stamina.CritThreshold,
            0f,
            1f);

        if (staminaRemainingFraction > Math.Clamp(ocean.DrowningStaminaThreshold, 0f, 1f))
        {
            swimming.NextDrowningDamage = TimeSpan.Zero;
            return;
        }

        if (now < swimming.NextDrowningDamage)
            return;

        swimming.NextDrowningDamage = now + DrowningDamageInterval;

        var damage = MathF.Max(0f, ocean.DrowningDamage) *
                     (float) DrowningDamageInterval.TotalSeconds;
        if (damage <= 0f)
            return;

        _damageable.TryChangeDamage(
            uid,
            new DamageSpecifier(_asphyxiation, damage),
            interruptsDoAfters: false,
            origin: uid,
            targetPart: TargetBodyPart.All);
    }
}
