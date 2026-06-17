// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Goobstation.Shared.Particles;
using Content.Shared.Tag;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Client.Particles.Visuals;

/// <summary>
/// Particles when entities are on fire.
/// </summary>
public sealed class FlammableParticleSystem : EntitySystem
{
    [Dependency] private readonly ParticleSystem _particles = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tag = default!; // <Onyx-Tweak>

    private static readonly ProtoId<ParticleEffectPrototype> FireEffect = "SfFireContinuous";
    private static readonly ProtoId<ParticleEffectPrototype> SmokeEffect = "SfFireSmoke";
    private static readonly ProtoId<TagPrototype> SuppressFlammableParticlesTag = "SuppressFlammableParticles"; // <Onyx-Tweak>

    private const float MaxStacks = 10f;

    private sealed class FireState
    {
        public ActiveEmitter? FireEmitter;
        public ActiveEmitter? SmokeEmitter;
        public bool OnFire;
    }

    private readonly Dictionary<EntityUid, FireState> _active = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FlammableComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<FlammableComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnAppearanceChange(Entity<FlammableComponent> ent, ref AppearanceChangeEvent args)
    {
        if (!_appearance.TryGetData(ent, FireVisuals.OnFire, out bool onFire))
            onFire = false;

        if (!_appearance.TryGetData(ent, FireVisuals.FireStacks, out float stacks))
            stacks = 0f;

        _active.TryGetValue(ent, out var state);

        // <Onyx-Tweak>
        if (_tag.HasTag(ent, SuppressFlammableParticlesTag))
        {
            if (state is { OnFire: true })
            {
                StopState(state);
                state.OnFire = false;
            }

            return;
        }
        // <Onyx-Tweak>

        if (onFire)
        {
            if (state == null)
            {
                state = new FireState();
                _active[ent] = state;
            }

            if (!state.OnFire)
            {
                var coords = _transform.GetMapCoordinates(ent);
                state.SmokeEmitter = _particles.SpawnEffect(SmokeEffect, coords, ent.Owner);
                state.FireEmitter  = _particles.SpawnEffect(FireEffect,  coords, ent.Owner);

                if (state.SmokeEmitter != null) state.SmokeEmitter.Intensity = 1f;
                if (state.FireEmitter != null)  state.FireEmitter.Intensity  = 1f;

                state.OnFire = true;
            }

            var intensity = Math.Clamp(stacks / MaxStacks * 2f, 1f, 2f);
            if (state.FireEmitter != null)
                state.FireEmitter.Intensity = intensity;
            if (state.SmokeEmitter != null)
                state.SmokeEmitter.Intensity = intensity;
        }
        else if (state is { OnFire: true })
        {
            StopState(state);
            state.OnFire = false;
        }
    }

    private void OnShutdown(Entity<FlammableComponent> ent, ref ComponentShutdown args)
    {
        if (_active.Remove(ent, out var state))
            StopState(state);
    }

    private void StopState(FireState state)
    {
        if (state.FireEmitter != null)
        {
            state.FireEmitter.Exhausted = true;
            state.FireEmitter = null;
        }

        if (state.SmokeEmitter != null)
        {
            state.SmokeEmitter.Exhausted = true;
            state.SmokeEmitter = null;
        }
    }
}
