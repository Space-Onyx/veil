using Content.Shared._Onyx.Phasing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.GameStates;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._Onyx.Phasing;

namespace Content.Client._Onyx;

public sealed class PhasingSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private ShaderPrototype _shaderPrototype = default!;
    private readonly Dictionary<EntityUid, ShaderInstance> _shaderInstances = new();
    private readonly Dictionary<EntityUid, PhasingParameters> _parameterCache = new();
    private int _frameCounter = 0;
    private const int UPDATE_FREQUENCY = 2;
    private const int MAX_SHADER_INSTANCES = 1000;
    private const int MAX_UPDATES_PER_FRAME = 50;
    private const int WARNING_THRESHOLD = 500;

    public override void Initialize()
    {
        base.Initialize();
        _shaderPrototype = _protoMan.Index<ShaderPrototype>("Phasing");
        SubscribeLocalEvent<PhasingComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PhasingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PhasingComponent, BeforePostShaderRenderEvent>(OnShaderRender);
        SubscribeLocalEvent<PhasingComponent, ComponentHandleState>(OnHandleState);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_shaderInstances.Count > MAX_SHADER_INSTANCES)
        {
            return;
        }

        _frameCounter++;
        if (_frameCounter % UPDATE_FREQUENCY != 0)
            return;
        int updateCount = 0;

        var visibleEntities = EntityManager.EntityQuery<PhasingComponent, SpriteComponent>()
            .Where(x => x.Item2.Visible)
            .Take(MAX_UPDATES_PER_FRAME);

        foreach (var (comp, sprite) in visibleEntities)
        {
            if (updateCount >= MAX_UPDATES_PER_FRAME)
                break;

            if (_shaderInstances.TryGetValue(comp.Owner, out var shaderInstance) && sprite.PostShader == shaderInstance)
            {
                if (HasParametersChanged(comp.Owner, comp))
                {
                    ApplyShaderParams(comp, shaderInstance);
                    UpdateParameterCache(comp.Owner, comp);
                    updateCount++;
                }
            }
        }
    }

    private void OnStartup(EntityUid uid, PhasingComponent component, ComponentStartup args)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        var shaderInstance = _shaderPrototype.InstanceUnique();
        _shaderInstances[uid] = shaderInstance;

        UpdateParameterCache(uid, component);

        ApplyShaderParams(component, shaderInstance);
        sprite.PostShader = shaderInstance;
        sprite.GetScreenTexture = false;
        sprite.RaiseShaderEvent = true;
    }

    private void OnShutdown(EntityUid uid, PhasingComponent component, ComponentShutdown args)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        if (_shaderInstances.TryGetValue(uid, out var shaderInstance) && sprite.PostShader == shaderInstance)
            sprite.PostShader = null;

        if (_shaderInstances.TryGetValue(uid, out var instance))
        {
            instance.Dispose();
            _shaderInstances.Remove(uid);
        }

        _parameterCache.Remove(uid);
    }

    private void OnShaderRender(EntityUid uid, PhasingComponent component, BeforePostShaderRenderEvent args)
    {
        if (_shaderInstances.TryGetValue(uid, out var shaderInstance) && HasParametersChanged(uid, component))
        {
            ApplyShaderParams(component, shaderInstance);
            UpdateParameterCache(uid, component);
        }
    }

    private void OnHandleState(EntityUid uid, PhasingComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not PhasingComponentState state)
            return;

        bool needsRestart = component.AnimationSpeed != state.AnimationSpeed ||
                           component.DistortionStrength != state.DistortionStrength ||
                           component.BandMin != state.BandMin ||
                           component.BandMax != state.BandMax ||
                           component.GlitchFrequency != state.GlitchFrequency ||
                           component.BandSplitStrength != state.BandSplitStrength ||
                           component.BandSplitFrequency != state.BandSplitFrequency;

        component.AnimationSpeed = state.AnimationSpeed;
        component.DistortionStrength = state.DistortionStrength;
        component.BandMin = state.BandMin;
        component.BandMax = state.BandMax;
        component.GlitchFrequency = state.GlitchFrequency;
        component.BandSplitStrength = state.BandSplitStrength;
        component.BandSplitFrequency = state.BandSplitFrequency;

        if (needsRestart && TryComp(uid, out SpriteComponent? sprite))
        {
            RestartShader(uid, sprite, component);
        }
    }

    private void ApplyShaderParams(PhasingComponent component, ShaderInstance shaderInstance)
    {
        shaderInstance.SetParameter("bandMin", component.BandMin);
        shaderInstance.SetParameter("bandMax", component.BandMax);
        shaderInstance.SetParameter("animationSpeed", component.AnimationSpeed);
        shaderInstance.SetParameter("distortionStrength", component.DistortionStrength);
        shaderInstance.SetParameter("glitchFrequency", component.GlitchFrequency);
        shaderInstance.SetParameter("bandSplitStrength", component.BandSplitStrength);
        shaderInstance.SetParameter("bandSplitFrequency", component.BandSplitFrequency);
    }

    private bool HasParametersChanged(EntityUid uid, PhasingComponent component)
    {
        if (!_parameterCache.TryGetValue(uid, out var cached))
            return true;

        return cached.AnimationSpeed != component.AnimationSpeed ||
               cached.DistortionStrength != component.DistortionStrength ||
               cached.BandMin != component.BandMin ||
               cached.BandMax != component.BandMax ||
               cached.GlitchFrequency != component.GlitchFrequency ||
               cached.BandSplitStrength != component.BandSplitStrength ||
               cached.BandSplitFrequency != component.BandSplitFrequency;
    }

    private void UpdateParameterCache(EntityUid uid, PhasingComponent component)
    {
        _parameterCache[uid] = new PhasingParameters
        {
            AnimationSpeed = component.AnimationSpeed,
            DistortionStrength = component.DistortionStrength,
            BandMin = component.BandMin,
            BandMax = component.BandMax,
            GlitchFrequency = component.GlitchFrequency,
            BandSplitStrength = component.BandSplitStrength,
            BandSplitFrequency = component.BandSplitFrequency
        };
    }
    public void RestartShader(EntityUid uid, SpriteComponent sprite, PhasingComponent component)
    {
        sprite.PostShader = null;

        if (_shaderInstances.TryGetValue(uid, out var oldInstance))
        {
            oldInstance.Dispose();
        }

        var newInstance = _shaderPrototype.InstanceUnique();
        _shaderInstances[uid] = newInstance;

        UpdateParameterCache(uid, component);

        ApplyShaderParams(component, newInstance);

        sprite.PostShader = newInstance;
    }
    public (int activeShaders, int cachedParameters) GetShaderStats()
    {
        return (_shaderInstances.Count, _parameterCache.Count);
    }

    public void ClearAllShaders()
    {
        foreach (var (uid, shaderInstance) in _shaderInstances)
        {
            if (TryComp(uid, out SpriteComponent? sprite))
            {
                sprite.PostShader = null;
            }
            shaderInstance.Dispose();
        }

        _shaderInstances.Clear();
        _parameterCache.Clear();
    }
}

public struct PhasingParameters
{
    public float AnimationSpeed;
    public float DistortionStrength;
    public float BandMin;
    public float BandMax;
    public float GlitchFrequency;
    public float BandSplitStrength;
    public float BandSplitFrequency;
}
