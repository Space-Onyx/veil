using System.Collections.Generic;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;

namespace Content.Client._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptMotorImpairmentOverlaySystem : EntitySystem
{
    private static readonly Color DefaultFillColor = new(255, 124, 34, 58);
    private static readonly Color DefaultOuterOutlineColor = new(0, 0, 0, 230);
    private static readonly Color DefaultInnerOutlineColor = new(255, 160, 52, 245);
    private const LookupFlags TargetLookupFlags =
        LookupFlags.Dynamic | LookupFlags.StaticSundries | LookupFlags.Sensors;

    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly List<CyberDeckScriptOverlayHelper.HighlightShape> _highlightShapes = new();
    private CyberDeckScriptMotorImpairmentOverlay? _overlay;
    private Color _fillColor = DefaultFillColor;
    private Color _outerOutlineColor = DefaultOuterOutlineColor;
    private Color _innerOutlineColor = DefaultInnerOutlineColor;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new CyberDeckScriptMotorImpairmentOverlay(this);
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
        foreach (var (candidate, _) in _lookup.GetEntitiesInRange<BodyComponent>(
                     bodyCoords,
                     range,
                     TargetLookupFlags))
        {
            if (candidate == body)
                continue;

            if (!HasOperationalCyberneticLeg(candidate))
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

        if (!CyberDeckScriptOverlayHelper.TryGetActiveTargetActionScript<CyberDeckScriptMotorImpairmentComponent>(
                EntityManager,
                _player,
                _ui,
                out body,
                out range,
                out var motorComp))
        {
            return false;
        }

        fillColor = motorComp.OverlayFillColor;
        outerOutlineColor = motorComp.OverlayOuterOutlineColor;
        innerOutlineColor = motorComp.OverlayInnerOutlineColor;
        return true;
    }

    private bool HasOperationalCyberneticLeg(EntityUid targetBody)
    {
        if (!TryComp<BodyComponent>(targetBody, out var bodyComp))
            return false;

        foreach (var (partUid, part) in _body.GetBodyChildren(targetBody, bodyComp))
        {
            if (part.PartType != BodyPartType.Leg)
                continue;

            if (!TryComp<CyberneticsComponent>(partUid, out var cyberComp) || cyberComp.Disabled)
                continue;

            return true;
        }

        return false;
    }

    private sealed class CyberDeckScriptMotorImpairmentOverlay : Overlay
    {
        private readonly CyberDeckScriptMotorImpairmentOverlaySystem _system;

        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        public CyberDeckScriptMotorImpairmentOverlay(CyberDeckScriptMotorImpairmentOverlaySystem system)
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
