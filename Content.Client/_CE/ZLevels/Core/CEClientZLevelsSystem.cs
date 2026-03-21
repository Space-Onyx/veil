/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Collections.Generic;
using System.Numerics;
using Content.Client.Damage.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Content.Client._Onyx.ZLevels;

namespace Content.Client._CE.ZLevels.Core;

/// <summary>
/// Only process Eye offset and drawdepth on clientside
/// </summary>
public sealed partial class CEClientZLevelsSystem : CESharedZLevelsSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;    // <Onyx-Tweak>

    // <Onyx-Tweak>
    private readonly HashSet<EntityUid> _dirtyVisuals = new();
    private readonly List<EntityUid> _dirtyVisualsBuffer = new();
    private float _cachedZLevelOffset;
    private const int OverMobsDrawDepth = (int)Shared.DrawDepth.DrawDepth.OverMobs;
    // </Onyx-Tweak>

    public static float ZLevelOffset = 0.7f;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new CEZLevelBlurOverlay());
        // <Onyx-Tweak>
        _overlay.AddOverlay(new OnyxZLevelHoleShadowOverlay());
        _overlay.AddOverlay(new OnyxZLevelRoofOverlay());
        // </Onyx-Tweak>

        SubscribeLocalEvent<CEZPhysicsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CEZPhysicsComponent, GetEyeOffsetEvent>(OnEyeOffset);
        // <Onyx-Tweak>
        SubscribeLocalEvent<CEZPhysicsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CEActiveZPhysicsComponent, ComponentStartup>(OnActiveStartup);
        SubscribeLocalEvent<CEActiveZPhysicsComponent, ComponentShutdown>(OnActiveShutdown);
        // </Onyx-Tweak>

        _cfg.OnValueChanged(CCVars.ZLevelOffset, value => _cachedZLevelOffset = value, true); // <Onyx-Tweak> 
    }
    // <Onyx-Tweak>

    private void OnActiveStartup(Entity<CEActiveZPhysicsComponent> ent, ref ComponentStartup args)
    {
        _dirtyVisuals.Add(ent);
    }

    private void OnActiveShutdown(Entity<CEActiveZPhysicsComponent> ent, ref ComponentShutdown args)
    {
        _dirtyVisuals.Add(ent);
    }
    // </Onyx-Tweak>

    // <Onyx-Tweak edited>
    private void OnEyeOffset(Entity<CEZPhysicsComponent> ent, ref GetEyeOffsetEvent args)
    {
        var xform = Transform(ent);
        var localPosition = GetVisualsLocalPositionFast(ent.Comp, xform);
        if (localPosition == 0f)
            return;

        Angle rotation = _eye.CurrentEye.Rotation * -1;
        var offset = rotation.RotateVec(new Vector2(0, localPosition * _cachedZLevelOffset)); // <Onyx-Tweak>
        args.Offset += offset;
    }
    // </Onyx-Tweak edited> 

    private void OnStartup(Entity<CEZPhysicsComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
        {
            _dirtyVisuals.Add(ent); // <Onyx-Tweak>
            return;
        }

        if (sprite.SnapCardinals)
        {
            _dirtyVisuals.Add(ent); // <Onyx-Tweak>
            return;
        }

        ent.Comp.NoRotDefault = sprite.NoRotation;
        ent.Comp.DrawDepthDefault = sprite.DrawDepth;
        ent.Comp.SpriteOffsetDefault = sprite.Offset;
        _dirtyVisuals.Add(ent); // <Onyx-Tweak>
    }

    // <Onyx-Tweak>
    private void OnShutdown(Entity<CEZPhysicsComponent> ent, ref ComponentShutdown args)
    {
        _dirtyVisuals.Remove(ent);
    }
    // </Onyx-Tweak>

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // <Onyx-Tweak Edited>
        var query = EntityQueryEnumerator<CEActiveZPhysicsComponent, CEZPhysicsComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var zPhys, out var sprite, out var xform))
        {
            ApplyVisuals(uid, zPhys, sprite, xform, _cachedZLevelOffset, OverMobsDrawDepth);
        }

        if (_dirtyVisuals.Count > 0)
        {
            _dirtyVisualsBuffer.Clear();
            foreach (var uid in _dirtyVisuals)
            {
                _dirtyVisualsBuffer.Add(uid);
            }
            _dirtyVisuals.Clear();

            foreach (var uid in _dirtyVisualsBuffer)
            {
                if (TerminatingOrDeleted(uid))
                    continue;

                if (!TryComp(uid, out CEZPhysicsComponent? zPhys) ||
                    !TryComp(uid, out SpriteComponent? sprite) ||
                    !TryComp(uid, out TransformComponent? xform))
                {
                    continue;
                }

                if (HasComp<CEActiveZPhysicsComponent>(uid))
                    continue;

                ApplyVisuals(uid, zPhys, sprite, xform, _cachedZLevelOffset, OverMobsDrawDepth);
            }
        }

        var query2 = EntityQueryEnumerator<CEActiveZPhysicsComponent, StaminaComponent, CEZPhysicsComponent>();
        while (query2.MoveNext(out var uid, out _, out var stamina, out var zPhys))
        {
            if (!_animation.HasRunningAnimation(uid, StaminaSystem.StaminaAnimationKey))
                continue;

            stamina.StartOffset = zPhys.SpriteOffsetDefault;
        }
    }
    // </Onyx-Tweak Edited>

    // <Onyx-Tweak>
    private void ApplyVisuals(EntityUid uid,
        CEZPhysicsComponent zPhys,
        SpriteComponent sprite,
        TransformComponent xform,
        float zLevelOffset,
        int overMobs)
    {
        var localPosition = GetVisualsLocalPositionFast(zPhys, xform);

        if (localPosition == zPhys.LastVisualLocalPosition)
            return;

        zPhys.LastVisualLocalPosition = localPosition;

        sprite.NoRotation = localPosition != 0 || zPhys.NoRotDefault;
        _sprite.SetOffset((uid, sprite), zPhys.SpriteOffsetDefault + new Vector2(0, localPosition * zLevelOffset));
        _sprite.SetDrawDepth((uid, sprite), localPosition > 0 ? overMobs : zPhys.DrawDepthDefault);
    }
    // </Onyx-Tweak>

    // <Onyx-Tweak>
    private float GetVisualsLocalPositionFast(CEZPhysicsComponent zPhys, TransformComponent xform)
    {
        if (xform.ParentUid != xform.MapUid && ZPhyzQuery.TryComp(xform.ParentUid, out var parentZPhys))
            return parentZPhys.LocalPosition;

        return zPhys.LocalPosition;
    }
    // </Onyx-Tweak>


    public float GetVisualsLocalPosition(Entity<CEZPhysicsComponent?> ent, TransformComponent? xform = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return 0;
        if (!Resolve(ent, ref xform, false))
            return 0;

        var pos = ent.Comp.LocalPosition;

        if (xform.ParentUid != xform.MapUid && ZPhyzQuery.TryComp(xform.ParentUid, out var parentZPhys))
            pos = parentZPhys.LocalPosition;

        return pos;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<CEZLevelBlurOverlay>();
        // <Onyx-Tweak>
        _overlay.RemoveOverlay<OnyxZLevelHoleShadowOverlay>();
        _overlay.RemoveOverlay<OnyxZLevelRoofOverlay>();
        // </Onyx-Tweak>
    }
}
