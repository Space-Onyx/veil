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
    private bool _zLevelOffsetInitialized;
    private float _cachedZLevelOffset;
    private float _nonActiveReconcileAccumulator;
    private const int OverMobsDrawDepth = (int)Shared.DrawDepth.DrawDepth.OverMobs;
    private const float VisualEpsilon = 0.001f;
    private const float NonActiveReconcileInterval = 0.10f;
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

        _cfg.OnValueChanged(CCVars.ZLevelOffset, OnZLevelOffsetChanged, true); // <Onyx-Tweak> 
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

    private void OnZLevelOffsetChanged(float value)
    {
        _cachedZLevelOffset = value;

        if (!_zLevelOffsetInitialized)
        {
            _zLevelOffsetInitialized = true;
            return;
        }

        var query = EntityQueryEnumerator<CEZPhysicsComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            _dirtyVisuals.Add(uid);
        }
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

        CacheVisualDefaults(ent.Comp, sprite);
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
        var activeQuery = EntityQueryEnumerator<CEActiveZPhysicsComponent, CEZPhysicsComponent>();
        while (activeQuery.MoveNext(out var uid, out _, out var zPhys))
        {
            if (!float.IsNaN(zPhys.LastVisualLocalPosition) && MathF.Abs(zPhys.Velocity) <= VisualEpsilon)
                continue;

            _dirtyVisuals.Add(uid);
        }

        _nonActiveReconcileAccumulator += frameTime;
        if (_nonActiveReconcileAccumulator >= NonActiveReconcileInterval)
        {
            _nonActiveReconcileAccumulator = 0f;
            QueueMismatchedVisuals();
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
    private void QueueMismatchedVisuals()
    {
        var query = EntityQueryEnumerator<CEZPhysicsComponent>();
        while (query.MoveNext(out var uid, out var zPhys))
        {
            var current = zPhys.LocalPosition;
            if (MathF.Abs(current) <= VisualEpsilon)
                current = 0f;

            var last = zPhys.LastVisualLocalPosition;
            if (float.IsNaN(last))
            {
                _dirtyVisuals.Add(uid);
                continue;
            }

            if (MathF.Abs(last) <= VisualEpsilon)
                last = 0f;

            if (current != last)
                _dirtyVisuals.Add(uid);
        }
    }

    private void ApplyVisuals(EntityUid uid,
        CEZPhysicsComponent zPhys,
        SpriteComponent sprite,
        TransformComponent xform,
        float zLevelOffset,
        int overMobs)
    {
        var localPosition = GetVisualsLocalPositionFast(zPhys, xform);
        if (MathF.Abs(localPosition) <= VisualEpsilon)
            localPosition = 0f;

        var last = zPhys.LastVisualLocalPosition;
        if (!float.IsNaN(last) && MathF.Abs(last) <= VisualEpsilon)
            last = 0f;

        if (localPosition == 0f)
        {
            if (!float.IsNaN(last) && last != 0f)
            {
                sprite.NoRotation = zPhys.NoRotDefault;
                _sprite.SetOffset((uid, sprite), zPhys.SpriteOffsetDefault);
                _sprite.SetDrawDepth((uid, sprite), zPhys.DrawDepthDefault);
            }

            CacheVisualDefaults(zPhys, sprite);
            zPhys.LastVisualLocalPosition = 0f;
            return;
        }

        if (!float.IsNaN(last) && localPosition == last)
            return;

        if (float.IsNaN(last) || last == 0f)
            CacheVisualDefaults(zPhys, sprite);

        zPhys.LastVisualLocalPosition = localPosition;
        sprite.NoRotation = true;
        _sprite.SetOffset((uid, sprite), zPhys.SpriteOffsetDefault + new Vector2(0, localPosition * zLevelOffset));
        _sprite.SetDrawDepth((uid, sprite), localPosition > 0 ? overMobs : zPhys.DrawDepthDefault);
    }

    private static void CacheVisualDefaults(CEZPhysicsComponent zPhys, SpriteComponent sprite)
    {
        zPhys.NoRotDefault = sprite.NoRotation;
        zPhys.DrawDepthDefault = sprite.DrawDepth;
        zPhys.SpriteOffsetDefault = sprite.Offset;
    }

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
