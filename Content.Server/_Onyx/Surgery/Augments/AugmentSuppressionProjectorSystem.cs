using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Popups;
using Content.Shared.Body.Events;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentSuppressionProjectorSystem : EntitySystem
{
    private const float MinProjectorUpdateInterval = 0.05f;
    private const float VisualizationUpdateInterval = 0.1f;
    private const float SquareLookupMultiplier = 1.4142135f;

    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    private readonly HashSet<ICommonSession> _visualizationObservers = new();
    private readonly HashSet<EntityUid> _dirtyProjectors = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _bodyProjectorLinks = new();
    private readonly HashSet<Entity<InstalledAugmentsComponent>> _bodyCandidates = new();
    private readonly HashSet<Entity<AugmentSuppressionProjectorComponent>> _projectorCandidates = new();
    private TimeSpan _nextProjectorSweep = TimeSpan.Zero;
    private TimeSpan _nextVisualizationUpdate = TimeSpan.Zero;
    private float _maxProjectorLookupRadius;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentSuppressionProjectorComponent, ComponentStartup>(OnProjectorStartup);
        SubscribeLocalEvent<AugmentSuppressionProjectorComponent, ComponentShutdown>(OnProjectorShutdown);
        SubscribeLocalEvent<AugmentSuppressionProjectorComponent, MoveEvent>(OnProjectorMoved);
        SubscribeLocalEvent<AugmentSuppressionProjectorComponent, EntParentChangedMessage>(OnProjectorParentChanged);
        SubscribeLocalEvent<InstalledAugmentsComponent, MoveEvent>(OnBodyMoved);
        SubscribeLocalEvent<InstalledAugmentsComponent, EntParentChangedMessage>(OnBodyParentChanged);
        SubscribeLocalEvent<InstalledAugmentsComponent, ComponentShutdown>(OnBodyShutdown);
        SubscribeLocalEvent<AugmentSuppressedByProjectorsComponent, OrganRemovedFromBodyEvent>(OnSuppressedAugmentRemovedFromBody);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        ProcessDirtyProjectors();

        if (_timing.CurTime >= _nextProjectorSweep)
        {
            _nextProjectorSweep = _timing.CurTime + TimeSpan.FromSeconds(MinProjectorUpdateInterval);
            SweepScheduledProjectorUpdates();
        }

        if (_timing.CurTime >= _nextVisualizationUpdate)
        {
            _nextVisualizationUpdate = _timing.CurTime + TimeSpan.FromSeconds(VisualizationUpdateInterval);
            UpdateVisualizationObservers();
        }
    }

    private void OnProjectorStartup(Entity<AugmentSuppressionProjectorComponent> ent, ref ComponentStartup args)
    {
        _dirtyProjectors.Add(ent.Owner);
        UpdateMaxProjectorLookupRadius(ent.Comp);
    }

    private void OnProjectorShutdown(Entity<AugmentSuppressionProjectorComponent> ent, ref ComponentShutdown args)
    {
        _dirtyProjectors.Remove(ent.Owner);

        foreach (var body in ent.Comp.AffectedBodies.ToArray())
        {
            RemoveSuppressionFromBody(ent, body);
            UnlinkBodyProjector(body, ent.Owner);
        }

        ent.Comp.AffectedBodies.Clear();
        RecalculateMaxProjectorLookupRadius();
    }

    private void OnProjectorMoved(Entity<AugmentSuppressionProjectorComponent> ent, ref MoveEvent args)
    {
        UpdateProjector(ent);
    }

    private void OnProjectorParentChanged(Entity<AugmentSuppressionProjectorComponent> ent, ref EntParentChangedMessage args)
    {
        UpdateProjector(ent);
    }

    private void OnBodyMoved(Entity<InstalledAugmentsComponent> ent, ref MoveEvent args)
    {
        UpdateRelevantProjectorsForBody(ent.Owner);
    }

    private void OnBodyParentChanged(Entity<InstalledAugmentsComponent> ent, ref EntParentChangedMessage args)
    {
        UpdateRelevantProjectorsForBody(ent.Owner);
    }

    private void OnBodyShutdown(Entity<InstalledAugmentsComponent> ent, ref ComponentShutdown args)
    {
        if (!_bodyProjectorLinks.TryGetValue(ent.Owner, out var links))
            return;

        foreach (var projectorUid in links)
        {
            _dirtyProjectors.Add(projectorUid);
        }

        _bodyProjectorLinks.Remove(ent.Owner);
    }

    private void ProcessDirtyProjectors()
    {
        if (_dirtyProjectors.Count == 0)
            return;

        var dirty = _dirtyProjectors.ToArray();
        _dirtyProjectors.Clear();

        foreach (var projectorUid in dirty)
        {
            if (!TryComp<AugmentSuppressionProjectorComponent>(projectorUid, out var comp))
                continue;

            comp.NextUpdateAt = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(MinProjectorUpdateInterval, comp.UpdateInterval));
            UpdateProjector((projectorUid, comp));
            UpdateMaxProjectorLookupRadius(comp);
        }
    }

    private void SweepScheduledProjectorUpdates()
    {
        var maxLookupRadius = 0f;
        var query = EntityQueryEnumerator<AugmentSuppressionProjectorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime >= comp.NextUpdateAt)
            {
                comp.NextUpdateAt = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(MinProjectorUpdateInterval, comp.UpdateInterval));
                UpdateProjector((uid, comp));
            }

            if (!comp.Enabled || comp.Radius <= 0f)
                continue;

            var lookup = comp.Shape == AugmentSuppressionFieldShape.Square
                ? comp.Radius * SquareLookupMultiplier
                : comp.Radius;

            if (lookup > maxLookupRadius)
                maxLookupRadius = lookup;
        }

        _maxProjectorLookupRadius = maxLookupRadius;
    }

    private void OnSuppressedAugmentRemovedFromBody(Entity<AugmentSuppressedByProjectorsComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        ent.Comp.Sources.Clear();

        if (ent.Comp.CreatedEmpDisabledComponent && HasComp<AugmentEmpDisabledComponent>(ent))
            RemComp<AugmentEmpDisabledComponent>(ent);

        RemCompDeferred<AugmentSuppressedByProjectorsComponent>(ent);
        UpdateRelevantProjectorsForBody(args.OldBody);
    }

    private void UpdateProjector(Entity<AugmentSuppressionProjectorComponent> ent)
    {
        if (ent.Comp.Radius <= 0f || !ent.Comp.Enabled)
        {
            if (ent.Comp.AffectedBodies.Count == 0)
                return;

            foreach (var body in ent.Comp.AffectedBodies.ToArray())
            {
                RemoveSuppressionFromBody(ent, body);
                UnlinkBodyProjector(body, ent.Owner);
            }

            ent.Comp.AffectedBodies.Clear();
            return;
        }

        var center = _transform.GetWorldPosition(ent.Owner);
        var projectorRotation = _transform.GetWorldRotation(ent.Owner);
        var radius = ent.Comp.Radius;
        var currentlyAffected = new HashSet<EntityUid>();
        var affectedBefore = ent.Comp.AffectedBodies;
        var lookupRange = ent.Comp.Shape == AugmentSuppressionFieldShape.Square
            ? radius * SquareLookupMultiplier
            : radius;

        _bodyCandidates.Clear();
        _lookup.GetEntitiesInRange<InstalledAugmentsComponent>(
            Transform(ent.Owner).Coordinates,
            lookupRange,
            _bodyCandidates,
            LookupFlags.Dynamic | LookupFlags.StaticSundries | LookupFlags.Sensors);

        foreach (var candidate in _bodyCandidates)
        {
            var body = candidate.Owner;
            if (!IsBodyInsideShape(center, projectorRotation, radius, body, ent.Comp.Shape))
                continue;

            var suppressedAny = ApplySuppressionToBody(ent, body, candidate.Comp);
            if (!suppressedAny)
                continue;

            currentlyAffected.Add(body);
            if (!affectedBefore.Contains(body))
            {
                LinkBodyProjector(body, ent.Owner);
                _popup.PopupEntity(Loc.GetString("augment-suppression-zone-enter"), body, body, PopupType.SmallCaution);
            }
        }

        foreach (var body in ent.Comp.AffectedBodies)
        {
            if (!currentlyAffected.Contains(body))
            {
                RemoveSuppressionFromBody(ent, body);
                UnlinkBodyProjector(body, ent.Owner);
                if (!BodyHasAnyProjectorSuppression(body))
                    _popup.PopupEntity(Loc.GetString("augment-suppression-zone-exit"), body, body);
            }
        }

        ent.Comp.AffectedBodies = currentlyAffected;
    }

    private void UpdateRelevantProjectorsForBody(EntityUid body)
    {
        if (_bodyProjectorLinks.TryGetValue(body, out var linkedProjectors))
        {
            foreach (var projectorUid in linkedProjectors)
            {
                if (TryComp<AugmentSuppressionProjectorComponent>(projectorUid, out var linkedComp))
                    UpdateProjector((projectorUid, linkedComp));
            }
        }

        if (_maxProjectorLookupRadius <= 0f || !TryComp<TransformComponent>(body, out var xform))
            return;

        _projectorCandidates.Clear();
        _lookup.GetEntitiesInRange<AugmentSuppressionProjectorComponent>(
            xform.Coordinates,
            _maxProjectorLookupRadius,
            _projectorCandidates,
            LookupFlags.Dynamic | LookupFlags.StaticSundries | LookupFlags.Sensors);

        foreach (var projector in _projectorCandidates)
        {
            if (!projector.Comp.Enabled || projector.Comp.Radius <= 0f)
                continue;

            UpdateProjector(projector);
        }
    }

    private bool IsBodyInsideShape(
        Vector2 center,
        Angle projectorRotation,
        float radius,
        EntityUid body,
        AugmentSuppressionFieldShape shape)
    {
        var position = _transform.GetWorldPosition(body);
        var delta = position - center;

        return shape switch
        {
            AugmentSuppressionFieldShape.Circle => delta.LengthSquared() <= radius * radius,
            AugmentSuppressionFieldShape.Square => IsInsideRotatedSquare(delta, projectorRotation, radius),
            _ => false,
        };
    }

    private static bool IsInsideRotatedSquare(Vector2 delta, Angle projectorRotation, float radius)
    {
        var localDelta = (-projectorRotation).RotateVec(delta);
        return MathF.Abs(localDelta.X) <= radius && MathF.Abs(localDelta.Y) <= radius;
    }

    private bool ApplySuppressionToBody(Entity<AugmentSuppressionProjectorComponent> projector, EntityUid body, InstalledAugmentsComponent installed)
    {
        var suppressedAny = false;

        foreach (var netEnt in installed.InstalledAugments)
        {
            var augmentUid = GetEntity(netEnt);
            if (!Exists(augmentUid))
                continue;

            if (ShouldSuppressAugment(projector.Comp, augmentUid))
            {
                AddOrMaintainSuppression(projector.Owner, augmentUid, body);
                suppressedAny = true;
            }
            else
                RemoveSuppressionSource(projector.Owner, augmentUid);
        }

        return suppressedAny;
    }

    private void RemoveSuppressionFromBody(Entity<AugmentSuppressionProjectorComponent> projector, EntityUid body)
    {
        if (!TryComp<InstalledAugmentsComponent>(body, out var installed))
            return;

        foreach (var netEnt in installed.InstalledAugments)
        {
            var augmentUid = GetEntity(netEnt);
            if (!Exists(augmentUid))
                continue;

            RemoveSuppressionSource(projector.Owner, augmentUid);
        }
    }

    private bool ShouldSuppressAugment(AugmentSuppressionProjectorComponent projector, EntityUid augmentUid)
    {
        if (projector.TargetTags.Count == 0)
            return true;

        var matched = false;
        foreach (var tag in projector.TargetTags)
        {
            if (!_tag.HasTag(augmentUid, tag))
                continue;

            matched = true;
            break;
        }

        return projector.InvertTags ? !matched : matched;
    }

    private void AddOrMaintainSuppression(EntityUid projectorUid, EntityUid augmentUid, EntityUid body)
    {
        var suppress = EnsureComp<AugmentSuppressedByProjectorsComponent>(augmentUid);
        suppress.Sources.Add(projectorUid);

        if (HasComp<AugmentEmpDisabledComponent>(augmentUid))
            return;

        var disabled = EnsureComp<AugmentEmpDisabledComponent>(augmentUid);
        disabled.DisabledUntil = TimeSpan.MaxValue;
        suppress.CreatedEmpDisabledComponent = true;

        var ev = new AugmentEmpDisabledEvent(body);
        RaiseLocalEvent(augmentUid, ref ev);
    }

    private void RemoveSuppressionSource(EntityUid projectorUid, EntityUid augmentUid)
    {
        if (!TryComp<AugmentSuppressedByProjectorsComponent>(augmentUid, out var suppress))
            return;

        if (!suppress.Sources.Remove(projectorUid))
            return;

        if (suppress.Sources.Count > 0)
            return;

        if (suppress.CreatedEmpDisabledComponent && HasComp<AugmentEmpDisabledComponent>(augmentUid))
        {
            RemComp<AugmentEmpDisabledComponent>(augmentUid);

            if (_augment.GetBody(augmentUid) is { } body)
            {
                var ev = new AugmentEmpRestoredEvent(body);
                RaiseLocalEvent(augmentUid, ref ev);
            }
        }

        RemComp<AugmentSuppressedByProjectorsComponent>(augmentUid);
    }

    private bool BodyHasAnyProjectorSuppression(EntityUid body)
    {
        return _bodyProjectorLinks.TryGetValue(body, out var links) && links.Count > 0;
    }

    private void LinkBodyProjector(EntityUid body, EntityUid projector)
    {
        if (!_bodyProjectorLinks.TryGetValue(body, out var links))
        {
            links = new HashSet<EntityUid>();
            _bodyProjectorLinks[body] = links;
        }

        links.Add(projector);
    }

    private void UnlinkBodyProjector(EntityUid body, EntityUid projector)
    {
        if (!_bodyProjectorLinks.TryGetValue(body, out var links))
            return;

        links.Remove(projector);
        if (links.Count == 0)
            _bodyProjectorLinks.Remove(body);
    }

    private static float GetProjectorLookupRadius(AugmentSuppressionProjectorComponent projector)
    {
        if (!projector.Enabled || projector.Radius <= 0f)
            return 0f;

        return projector.Shape == AugmentSuppressionFieldShape.Square
            ? projector.Radius * SquareLookupMultiplier
            : projector.Radius;
    }

    private void UpdateMaxProjectorLookupRadius(AugmentSuppressionProjectorComponent projector)
    {
        var lookup = GetProjectorLookupRadius(projector);
        if (lookup > _maxProjectorLookupRadius)
            _maxProjectorLookupRadius = lookup;
    }

    private void RecalculateMaxProjectorLookupRadius()
    {
        var maxLookupRadius = 0f;
        var query = EntityQueryEnumerator<AugmentSuppressionProjectorComponent>();
        while (query.MoveNext(out _, out var projector))
        {
            var lookup = GetProjectorLookupRadius(projector);
            if (lookup > maxLookupRadius)
                maxLookupRadius = lookup;
        }

        _maxProjectorLookupRadius = maxLookupRadius;
    }

    public bool ToggleVisualization(ICommonSession session)
    {
        if (_visualizationObservers.Contains(session))
        {
            _visualizationObservers.Remove(session);
            RaiseNetworkEvent(new AugmentSuppressionZoneVisualizationEvent(), session.Channel);
            return false;
        }

        _visualizationObservers.Add(session);
        RaiseNetworkEvent(BuildVisualizationEvent(), session.Channel);
        return true;
    }

    public bool DisableVisualization(ICommonSession session)
    {
        if (!_visualizationObservers.Remove(session))
            return false;

        RaiseNetworkEvent(new AugmentSuppressionZoneVisualizationEvent(), session.Channel);
        return true;
    }

    private void UpdateVisualizationObservers()
    {
        if (_visualizationObservers.Count == 0)
            return;

        var ev = BuildVisualizationEvent();
        var snapshot = _visualizationObservers.ToArray();
        foreach (var session in snapshot)
        {
            if (session.Status != SessionStatus.InGame)
            {
                _visualizationObservers.Remove(session);
                continue;
            }

            RaiseNetworkEvent(ev, session.Channel);
        }
    }

    private AugmentSuppressionZoneVisualizationEvent BuildVisualizationEvent()
    {
        var zones = new List<AugmentSuppressionZoneVisualizationEvent.ZoneData>();

        var query = EntityQueryEnumerator<AugmentSuppressionProjectorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var center = _transform.GetWorldPosition(uid);
            var rotation = _transform.GetWorldRotation(uid);
            zones.Add(new AugmentSuppressionZoneVisualizationEvent.ZoneData(
                GetNetEntity(uid),
                comp.Radius,
                comp.Shape,
                comp.Enabled && comp.Radius > 0f,
                center,
                (float) rotation.Theta));
        }

        return new AugmentSuppressionZoneVisualizationEvent(zones);
    }
}
