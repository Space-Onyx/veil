using System.Collections.Generic;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;

namespace Content.Client._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptOpticsOverloadOverlaySystem : EntitySystem
{
    private static readonly Color DefaultFillColor = new(255, 52, 134, 52);
    private static readonly Color DefaultOuterOutlineColor = new(0, 0, 0, 230);
    private static readonly Color DefaultInnerOutlineColor = new(255, 52, 134, 245);
    private const LookupFlags TargetLookupFlags =
        LookupFlags.Dynamic | LookupFlags.StaticSundries | LookupFlags.Sensors;

    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly List<CyberDeckScriptOverlayHelper.HighlightShape> _highlightShapes = new();
    private readonly HashSet<Entity<BodyComponent>> _bodyCandidates = new();
    private CyberDeckScriptOpticsOverloadOverlay? _overlay;
    private Color _fillColor = DefaultFillColor;
    private Color _outerOutlineColor = DefaultOuterOutlineColor;
    private Color _innerOutlineColor = DefaultInnerOutlineColor;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new CyberDeckScriptOpticsOverloadOverlay(this);
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
        _bodyCandidates.Clear();
        _lookup.GetEntitiesInRange(bodyCoords, range, _bodyCandidates, TargetLookupFlags);
        foreach (var (candidate, _) in _bodyCandidates)
        {
            if (candidate == body)
                continue;

            if (!EntityManager.HasOperationalCyberneticOrgan<EyesComponent>(candidate))
                continue;

            if (CyberDeckScriptOverlayHelper.TryBuildHighlightShape(EntityManager, _xform, candidate, out var shape))
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

        if (!CyberDeckScriptOverlayHelper.TryGetActiveTargetActionScript<CyberDeckScriptOpticsOverloadComponent>(
                EntityManager,
                _player,
                _ui,
                out body,
                out range,
                out var opticsComp))
        {
            return false;
        }

        fillColor = opticsComp.OverlayFillColor;
        outerOutlineColor = opticsComp.OverlayOuterOutlineColor;
        innerOutlineColor = opticsComp.OverlayInnerOutlineColor;
        return true;
    }

    private sealed class CyberDeckScriptOpticsOverloadOverlay : Overlay
    {
        private readonly CyberDeckScriptOpticsOverloadOverlaySystem _system;

        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        public CyberDeckScriptOpticsOverloadOverlay(CyberDeckScriptOpticsOverloadOverlaySystem system)
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
