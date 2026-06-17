using Content.Server._Onyx.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;

namespace Content.Server._Onyx.Atmos.Systems;

public sealed class PhotosynthesisSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PhotosynthesisComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var photosynthesis, out var xform))
        {
            photosynthesis.AccumulatedFrametime += frameTime;
            if (photosynthesis.AccumulatedFrametime < photosynthesis.UpdateDelay)
                continue;

            var elapsed = photosynthesis.AccumulatedFrametime;
            photosynthesis.AccumulatedFrametime = 0f;

            var air = _atmosphere.GetContainingMixture((uid, xform), excite: true);
            if (air == null || air.Immutable)
                continue;

            var carbonDioxide = air.GetMoles(Gas.CarbonDioxide);
            if (carbonDioxide <= Atmospherics.GasMinMoles)
                continue;

            var converted = MathF.Min(carbonDioxide, photosynthesis.MolesPerSecond * elapsed);
            air.AdjustMoles(Gas.CarbonDioxide, -converted);
            air.AdjustMoles(Gas.Oxygen, converted);
        }
    }
}
