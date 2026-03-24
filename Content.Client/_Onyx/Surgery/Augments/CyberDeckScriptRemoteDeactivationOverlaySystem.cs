using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Actions.Components;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Doors.Components;
using Content.Shared.StationRecords;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Prototypes;
using PhysTransform = Robust.Shared.Physics.Transform;

namespace Content.Client._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptRemoteDeactivationOverlaySystem : EntitySystem
{
    private static readonly Color DefaultFillColor = new(24, 132, 255, 26);
    private static readonly Color DefaultOuterOutlineColor = new(0, 0, 0, 230);
    private static readonly Color DefaultInnerOutlineColor = new(24, 132, 255, 245);
    private static readonly ICollection<StationRecordKey> EmptyStationKeys = Array.Empty<StationRecordKey>();
    private const LookupFlags AirlockLookupFlags =
        LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.StaticSundries | LookupFlags.Sundries;

    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly List<HighlightShape> _highlightShapes = new();
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
        foreach (var (airlock, _) in _lookup.GetEntitiesInRange<AirlockComponent>(
                     bodyCoords,
                     range,
                     AirlockLookupFlags))
        {
            if (!MatchesConfiguredAccess(airlock))
                continue;

            if (TryBuildHighlightShape(airlock, out var shape))
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

        if (_player.LocalEntity is not { Valid: true } user)
            return false;

        body = user;

        var actionUi = _ui.GetUIController<ActionUIController>();
        if (actionUi.SelectingTargetFor is not { } actionUid || !Exists(actionUid))
            return false;

        if (!TryComp<ActionComponent>(actionUid, out var actionComp))
            return false;

        if (actionComp.AttachedEntity != user)
            return false;

        if (actionComp.Container is not { } container ||
            !TryComp<CyberDeckScriptRemoteDeactivationComponent>(container, out var remoteComp))
        {
            return false;
        }

        fillColor = remoteComp.OverlayFillColor;
        outerOutlineColor = remoteComp.OverlayOuterOutlineColor;
        innerOutlineColor = remoteComp.OverlayInnerOutlineColor;
        _requiredAccess = remoteComp.Access.Count > 0 ? remoteComp.Access : null;
        _invertedAccess = remoteComp.Inverted;

        if (!TryComp<TargetActionComponent>(actionUid, out var targetComp))
            return false;

        range = MathF.Max(0f, targetComp.Range);
        return range > 0f;
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

    private bool TryBuildHighlightShape(EntityUid airlock, out HighlightShape shape)
    {
        shape = default;

        if (!TryGetMainFixture(airlock, out var fixture))
            return false;

        var xform = Transform(airlock);
        var worldTransform = new PhysTransform(
            _xform.GetWorldPosition(xform),
            _xform.GetWorldRotation(xform));

        if (!TryGetShapeVertices(fixture.Shape, worldTransform, out var vertices) || vertices.Length < 3)
            return false;

        var bounds = ComputeBounds(vertices);
        var center = ComputeCenter(vertices);
        var outerVertices = ExpandPolygon(vertices, center, 0.03f);
        shape = new HighlightShape(vertices, outerVertices, bounds);
        return true;
    }

    private bool TryGetMainFixture(EntityUid uid, out Fixture fixture)
    {
        fixture = default!;

        if (!TryComp<Robust.Shared.Physics.FixturesComponent>(uid, out var fixtures) || fixtures.Fixtures.Count == 0)
            return false;

        if (fixtures.Fixtures.TryGetValue("fix1", out var fix1))
        {
            fixture = fix1;
            return true;
        }

        Fixture? best = null;
        var bestScore = float.MinValue;

        foreach (var candidate in fixtures.Fixtures.Values)
        {
            if (!candidate.Hard)
                continue;

            var score = GetFixtureScore(candidate.Shape);
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = candidate;
        }

        if (best == null)
        {
            foreach (var candidate in fixtures.Fixtures.Values)
            {
                best = candidate;
                break;
            }
        }

        if (best == null)
            return false;

        fixture = best;
        return true;
    }

    private static float GetFixtureScore(IPhysShape shape)
    {
        return shape switch
        {
            PolygonShape polygon => ComputePolygonArea(polygon.Vertices),
            PhysShapeCircle circle => MathF.PI * circle.Radius * circle.Radius,
            EdgeShape => 0.001f,
            _ => 0f,
        };
    }

    private static float ComputePolygonArea(IReadOnlyList<Vector2> vertices)
    {
        if (vertices.Count < 3)
            return 0f;

        var area = 0f;
        for (var i = 0; i < vertices.Count; i++)
        {
            var next = (i + 1) % vertices.Count;
            area += vertices[i].X * vertices[next].Y - vertices[next].X * vertices[i].Y;
        }

        return MathF.Abs(area) * 0.5f;
    }

    private static bool TryGetShapeVertices(IPhysShape shape, PhysTransform transform, out Vector2[] vertices)
    {
        switch (shape)
        {
            case PolygonShape polygon:
            {
                vertices = new Vector2[polygon.VertexCount];
                for (var i = 0; i < polygon.VertexCount; i++)
                {
                    vertices[i] = PhysTransform.Mul(transform, polygon.Vertices[i]);
                }

                return true;
            }
            case PhysShapeCircle circle:
            {
                const int segments = 20;
                vertices = new Vector2[segments];
                for (var i = 0; i < segments; i++)
                {
                    var angle = i / (float) segments * MathF.PI * 2f;
                    var localPoint = circle.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * circle.Radius;
                    vertices[i] = PhysTransform.Mul(transform, localPoint);
                }

                return true;
            }
            default:
                vertices = Array.Empty<Vector2>();
                return false;
        }
    }

    private static Vector2 ComputeCenter(IReadOnlyList<Vector2> vertices)
    {
        var sum = Vector2.Zero;
        for (var i = 0; i < vertices.Count; i++)
        {
            sum += vertices[i];
        }

        return vertices.Count > 0 ? sum / vertices.Count : Vector2.Zero;
    }

    private static Box2 ComputeBounds(IReadOnlyList<Vector2> vertices)
    {
        if (vertices.Count == 0)
            return Box2.CenteredAround(Vector2.Zero, Vector2.Zero);

        var minX = vertices[0].X;
        var minY = vertices[0].Y;
        var maxX = vertices[0].X;
        var maxY = vertices[0].Y;

        for (var i = 1; i < vertices.Count; i++)
        {
            var v = vertices[i];
            if (v.X < minX)
                minX = v.X;
            if (v.Y < minY)
                minY = v.Y;
            if (v.X > maxX)
                maxX = v.X;
            if (v.Y > maxY)
                maxY = v.Y;
        }

        return new Box2(minX, minY, maxX, maxY);
    }

    private static Vector2[] ExpandPolygon(IReadOnlyList<Vector2> vertices, Vector2 center, float offset)
    {
        var result = new Vector2[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var dir = vertices[i] - center;
            if (dir.LengthSquared() <= 0.0001f)
            {
                result[i] = vertices[i];
                continue;
            }

            result[i] = vertices[i] + Vector2.Normalize(dir) * offset;
        }

        return result;
    }

    private static void DrawOutline(DrawingHandleWorld handle, IReadOnlyList<Vector2> vertices, Color color)
    {
        if (vertices.Count < 2)
            return;

        for (var i = 0; i < vertices.Count; i++)
        {
            var next = (i + 1) % vertices.Count;
            handle.DrawLine(vertices[i], vertices[next], color);
        }
    }

    private readonly record struct HighlightShape(Vector2[] Vertices, Vector2[] OuterVertices, Box2 Bounds);

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

                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, shape.Vertices, _system._fillColor);
                DrawOutline(handle, shape.OuterVertices, _system._outerOutlineColor);
                DrawOutline(handle, shape.Vertices, _system._innerOutlineColor);
            }
        }
    }
}
