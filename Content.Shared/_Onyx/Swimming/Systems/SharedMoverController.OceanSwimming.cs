using System.Numerics;
using Content.Shared._Onyx.Swimming.Components;
using Content.Shared._Onyx.Swimming.Events;
using Content.Shared._Onyx.Swimming.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Movement.Components;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    private const float SwimGlidePower = 0.35f;
    private const float MinStrokeInterval = 0.05f;
    private const float MinStrokeDuration = 0.01f;

    private void ApplyOceanSwimming(
        EntityUid uid,
        InputMoverComponent mover,
        TransformComponent xform,
        ref Vector2 wishDir,
        ref float acceleration,
        ref float friction)
    {
        if (!TryComp<OceanSwimmingComponent>(uid, out var swimming) ||
            xform.MapUid is not { } mapUid ||
            !TryComp<OceanMapComponent>(mapUid, out var ocean))
        {
            return;
        }

        friction = MathF.Max(0f, ocean.WaterDrag);
        acceleration = MathF.Max(0f, ocean.StrokeAcceleration);

        if (TryComp<StaminaComponent>(uid, out var stamina))
        {
            if (!OceanSwimmingStamina.CanSwim(swimming, stamina, ocean))
            {
                swimming.NextStroke = Timing.CurTime;
                swimming.StrokeUntil = TimeSpan.Zero;
                acceleration = 0f;
                wishDir = Vector2.Zero;
                return;
            }
        }

        var wishLengthSquared = wishDir.LengthSquared();

        if (!mover.HasDirectionalMovement || wishLengthSquared <= 0f)
        {
            swimming.NextStroke = Timing.CurTime;
            swimming.StrokeUntil = TimeSpan.Zero;
            wishDir = Vector2.Zero;
            return;
        }

        var now = Timing.CurTime;

        var intervalSeconds = Math.Max(MinStrokeInterval, ocean.StrokeInterval.TotalSeconds);
        var durationSeconds = Math.Clamp(
            ocean.StrokeDuration.TotalSeconds,
            MinStrokeDuration,
            intervalSeconds);

        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var duration = TimeSpan.FromSeconds(durationSeconds);

        if (now >= swimming.NextStroke)
        {
            swimming.NextStroke = now + interval;
            swimming.StrokeUntil = now + duration;
        }

        var power = GetStrokePower(now, swimming.StrokeUntil, duration);
        var speed = MathF.Max(0f, ocean.SwimSpeed);

        var sprint = new OceanSwimmingSprintEvent();
        RaiseLocalEvent(uid, sprint);
        if (sprint.IsSprinting)
            speed *= MathF.Max(1f, ocean.SprintSpeedMultiplier);

        wishDir = Vector2.Normalize(wishDir) * speed * power;
    }

    private static float GetStrokePower(TimeSpan now, TimeSpan strokeUntil, TimeSpan duration)
    {
        if (now >= strokeUntil)
            return SwimGlidePower;

        var strokeStart = strokeUntil - duration;

        var strokeProgress = Math.Clamp(
            (float) ((now - strokeStart).TotalSeconds / duration.TotalSeconds),
            0f,
            1f);

        var strokePulse = MathF.Sin(strokeProgress * MathF.PI);

        return SwimGlidePower + (1f - SwimGlidePower) * strokePulse;
    }
}
