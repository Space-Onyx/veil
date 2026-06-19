using Content.Shared._Onyx.Swimming.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Components;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Swimming;

public sealed class OceanSwimmingStaminaSystem : EntitySystem
{
    private static readonly TimeSpan StaminaSuppressLeadTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StaminaSuppressRefreshThreshold = TimeSpan.FromSeconds(0.25);

    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

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

        while (query.MoveNext(out var uid, out _, out var mover, out var stamina, out var xform))
        {
            if (xform.MapUid is not { } mapUid ||
                !TryComp<OceanMapComponent>(mapUid, out var ocean))
            {
                continue;
            }

            var moving = mover.HasDirectionalMovement && mover.CanMove && !stamina.Critical;

            var staminaCost = MathF.Max(0f, ocean.StaminaCost);
            var staminaRecovery = MathF.Max(0f, ocean.StaminaRecovery);

            if (moving)
            {
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
                _stamina.TakeStaminaDamage(
                    uid,
                    -staminaRecovery * frameTime,
                    stamina,
                    source: uid,
                    visual: false,
                    immediate: false,
                    logDamage: false);
            }

            if (stamina.NextUpdate >= refreshSuppressAt)
                continue;

            stamina.NextUpdate = suppressUntil;
            Dirty(uid, stamina);
        }
    }
}