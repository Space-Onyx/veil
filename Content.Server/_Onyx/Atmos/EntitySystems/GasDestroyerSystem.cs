using System.Diagnostics.CodeAnalysis;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Shared._Onyx.Atmos.Components;
using Content.Shared._Onyx.Atmos.EntitySystems;
using Content.Shared.Atmos;
using JetBrains.Annotations;
using Robust.Server.GameObjects;

namespace Content.Server._Onyx.Atmos.EntitySystems;

[UsedImplicitly]
public sealed class GasDestroyerSystem : SharedGasDestroyerSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasDestroyerComponent, AtmosDeviceUpdateEvent>(OnDestroyerUpdated);
    }

    private void OnDestroyerUpdated(Entity<GasDestroyerComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        var destroyer = ent.Comp;
        var oldState = destroyer.DestroyerState;

        if (!GetValidEnvironment(ent, out var environment) || !Transform(ent).Anchored)
        {
            destroyer.DestroyerState = GasDestroyerState.Disabled;
        }
        else
        {
            var destroyed = DestroyGas(destroyer, environment, destroyer.DestroyAmount * args.dt);
            destroyer.DestroyerState = destroyed < Atmospherics.GasMinMoles
                ? GasDestroyerState.Idle
                : GasDestroyerState.Working;
        }

        if (destroyer.DestroyerState != oldState)
        {
            Dirty(ent);
        }
    }

    private bool GetValidEnvironment(Entity<GasDestroyerComponent> ent, [NotNullWhen(true)] out GasMixture? environment)
    {
        var (uid, _) = ent;
        var transform = Transform(uid);
        var position = _transformSystem.GetGridOrMapTilePosition(uid, transform);

        if (_atmosphereSystem.IsTileSpace(transform.GridUid, transform.MapUid, position))
        {
            environment = null;
            return false;
        }

        environment = _atmosphereSystem.GetContainingMixture((uid, transform), true, true);
        return environment != null;
    }

    private float DestroyGas(GasDestroyerComponent destroyer, GasMixture environment, float targetAmount)
    {
        var amount = CapDestroyAmount(destroyer, targetAmount, environment);
        if (amount < Atmospherics.GasMinMoles)
            return 0f;

        if (destroyer.DestroyAnyGas)
            return environment.Remove(amount).TotalMoles;

        if (destroyer.ListDestroyGas is not null)
            return DestroyGasList(environment, destroyer.ListDestroyGas, amount);

        if (destroyer.DestroyGas is not { } gas)
            return 0f;

        return DestroySingleGas(environment, gas, amount);
    }

    private float DestroyGasList(GasMixture environment, Dictionary<Gas, float> gases, float amount)
    {
        var destroyed = 0f;
        var remaining = amount;

        foreach (var (gas, coefficient) in gases)
        {
            if (remaining < Atmospherics.GasMinMoles)
                break;

            var gasAmount = MathF.Min(remaining, amount * coefficient);
            destroyed += DestroySingleGas(environment, gas, gasAmount);
            remaining = amount - destroyed;
        }

        return destroyed;
    }

    private float DestroySingleGas(GasMixture environment, Gas gas, float amount)
    {
        var toDestroy = Math.Clamp(amount, 0f, environment.GetMoles(gas));
        if (toDestroy < Atmospherics.GasMinMoles)
            return 0f;

        environment.AdjustMoles(gas, -toDestroy);
        return toDestroy;
    }

    private float CapDestroyAmount(GasDestroyerComponent destroyer, float targetAmount, GasMixture environment)
    {
        if (environment.TotalMoles <= destroyer.MinExternalAmount ||
            environment.Pressure <= destroyer.MinExternalPressure ||
            environment.Temperature <= 0f)
        {
            return 0f;
        }

        var removableMoles = Math.Min(
            (environment.Pressure - destroyer.MinExternalPressure) * environment.Volume / (environment.Temperature * Atmospherics.R),
            environment.TotalMoles - destroyer.MinExternalAmount);

        return Math.Clamp(removableMoles, 0f, targetAmount);
    }
}
