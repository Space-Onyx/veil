using System;
using System.Collections.Generic;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Doors.Components;
using Content.Shared.StationRecords;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptRemoteDeactivationOverlaySystem : EntitySystem
{
    private static readonly Color DefaultFillColor = new(24, 132, 255, 26);
    private static readonly Color DefaultOuterOutlineColor = new(0, 0, 0, 230);
    private static readonly Color DefaultInnerOutlineColor = new(24, 132, 255, 245);
    private static readonly ICollection<StationRecordKey> EmptyStationKeys = Array.Empty<StationRecordKey>();
    private const LookupFlags TargetLookupFlags =
        LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.StaticSundries | LookupFlags.Sundries;

    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly List<CyberDeckScriptOverlayHelper.HighlightShape> _highlightShapes = new();
    private readonly HashSet<Entity<AirlockComponent>> _airlockCandidates = new();
    private readonly HashSet<Entity<CyberDeckRemoteDeactivationCameraTargetComponent>> _cameraCandidates = new();
    private CyberDeckScriptRemoteDeactivationOverlay? _overlay;
    private Color _fillColor = DefaultFillColor;
    private Color _outerOutlineColor = DefaultOuterOutlineColor;
    private Color _innerOutlineColor = DefaultInnerOutlineColor;
    private ICollection<ProtoId<AccessLevelPrototype>>? _requiredAccess;
    private bool _invertedAccess;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new CyberDeckScriptRemoteDeactivationOverlay(this);
        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlay != null)
        {
            _overlayManager.RemoveOverlay(_overlay);
            _overlay = null;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _highlightShapes.Clear();

        if (!TryGetActiveRange(
                out var body,
                out var range,
                out _fillColor,
                out _outerOutlineColor,
                out _innerOutlineColor))
            return;

        if (!EntityManager.HasOperationalCyberneticOrgan<EyesComponent>(body))
            return;

        var bodyCoords = Transform(body).Coordinates;
        _airlockCandidates.Clear();
        _lookup.GetEntitiesInRange(bodyCoords, range, _airlockCandidates, TargetLookupFlags);
        foreach (var (airlock, _) in _airlockCandidates)
        {
            if (!MatchesConfiguredAccess(airlock))
                continue;

            if (CyberDeckScriptOverlayHelper.TryBuildHighlightShape(EntityManager, _xform, airlock, out var shape))
                _highlightShapes.Add(shape);
        }

        _cameraCandidates.Clear();
        _lookup.GetEntitiesInRange(bodyCoords, range, _cameraCandidates, TargetLookupFlags);
        foreach (var (camera, _) in _cameraCandidates)
        {
            if (!Transform(camera).Anchored)
                continue;

            if (CyberDeckScriptOverlayHelper.TryBuildHighlightShape(EntityManager, _xform, camera, out var shape))
                _highlightShapes.Add(shape);
        }
    }

    private bool TryGetActiveRange(
        out EntityUid body,
        out float range,
        out Color fillColor,
        out Color outerOutlineColor,
        out Color innerOutlineColor)
    {
        body = default;
        range = 0f;
        fillColor = DefaultFillColor;
        outerOutlineColor = DefaultOuterOutlineColor;
        innerOutlineColor = DefaultInnerOutlineColor;
        _requiredAccess = null;
        _invertedAccess = false;

        if (!CyberDeckScriptOverlayHelper.TryGetActiveTargetActionScript<CyberDeckScriptRemoteDeactivationComponent>(
                EntityManager,
                _player,
                _ui,
                out body,
                out range,
                out var remoteComp))
        {
            return false;
        }

        fillColor = remoteComp.OverlayFillColor;
        outerOutlineColor = remoteComp.OverlayOuterOutlineColor;
        innerOutlineColor = remoteComp.OverlayInnerOutlineColor;
        _requiredAccess = remoteComp.Access.Count > 0 ? remoteComp.Access : null;
        _invertedAccess = remoteComp.Inverted;
        return true;
    }

    private bool MatchesConfiguredAccess(EntityUid target)
    {
        if (_requiredAccess == null)
            return true;

        var matches = true;
        if (_accessReader.GetMainAccessReader(target, out var readerEnt) &&
            readerEnt is { } reader)
        {
            matches = _accessReader.IsAllowed(_requiredAccess, EmptyStationKeys, reader.Owner, reader.Comp);
        }

        return _invertedAccess ? !matches : matches;
    }

    private sealed class CyberDeckScriptRemoteDeactivationOverlay : Overlay
    {
        private readonly CyberDeckScriptRemoteDeactivationOverlaySystem _system;

        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        public CyberDeckScriptRemoteDeactivationOverlay(CyberDeckScriptRemoteDeactivationOverlaySystem system)
        {
            _system = system;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (_system._highlightShapes.Count == 0)
                return;

            var handle = args.WorldHandle;

            foreach (var shape in _system._highlightShapes)
            {
                if (!shape.Bounds.Intersects(args.WorldAABB))
                    continue;

                CyberDeckScriptOverlayHelper.DrawHighlightShape(
                    handle,
                    shape,
                    _system._fillColor,
                    _system._outerOutlineColor,
                    _system._innerOutlineColor);
            }
        }
    }
}
