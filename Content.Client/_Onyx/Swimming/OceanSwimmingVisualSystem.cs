using Content.Shared._Onyx.Swimming.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._Onyx.Swimming;

public sealed class OceanSwimmingVisualSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> OceanSubmersionShader = "OnyxOceanSubmersion";
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = new();
    private ShaderPrototype _shaderPrototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shaderPrototype = _prototypes.Index(OceanSubmersionShader);

        SubscribeLocalEvent<OceanSwimmingComponent, ComponentShutdown>(OnSwimmingShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<OceanSwimmingComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            if (xform.MapUid is not { } mapUid ||
                !TryComp<OceanMapComponent>(mapUid, out var ocean))
            {
                RemoveShader(uid, sprite);
                continue;
            }

            if (!TryApplyShader(uid, sprite, out var shader))
                continue;

            shader.SetParameter("submersionDepth", Math.Clamp(ocean.SubmersionDepth, 0f, 1f));
            shader.SetParameter("submergedAlpha", Math.Clamp(ocean.SubmergedAlpha, 0f, 1f));
        }
    }

    private bool TryApplyShader(EntityUid uid, SpriteComponent sprite, out ShaderInstance shader)
    {
        if (_shaders.TryGetValue(uid, out shader!))
        {
            if (sprite.PostShader == null)
            {
                sprite.PostShader = shader;
                return true;
            }

            if (sprite.PostShader == shader)
                return true;

            _shaders.Remove(uid);
            shader = default!;
            return false;
        }

        if (sprite.PostShader != null)
        {
            shader = default!;
            return false;
        }

        shader = _shaderPrototype.InstanceUnique();
        _shaders.Add(uid, shader);
        sprite.PostShader = shader;

        return true;
    }

    private void OnSwimmingShutdown(Entity<OceanSwimmingComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent.Owner, out var sprite))
            RemoveShader(ent.Owner, sprite);
        else
            _shaders.Remove(ent.Owner);
    }

    private void RemoveShader(EntityUid uid, SpriteComponent sprite)
    {
        if (!_shaders.Remove(uid, out var shader))
            return;

        if (sprite.PostShader == shader)
            sprite.PostShader = null;
    }
}