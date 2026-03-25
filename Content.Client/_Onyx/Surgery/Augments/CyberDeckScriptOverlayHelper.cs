using System.Collections.Generic;
using System.Numerics;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared.Actions.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Dynamics;
using PhysTransform = Robust.Shared.Physics.Transform;

namespace Content.Client._Onyx.Surgery.Augments;

internal static class CyberDeckScriptOverlayHelper
{
    public readonly record struct HighlightShape(Vector2[] Vertices, Vector2[] OuterVertices, Box2 Bounds);

    public static bool TryGetActiveTargetActionScript<TScript>(
        IEntityManager entityManager,
        IPlayerManager player,
        IUserInterfaceManager ui,
        out EntityUid body,
        out float range,
        out TScript scriptComp)
        where TScript : Component
    {
        body = default;
        range = 0f;
        scriptComp = default!;

        if (player.LocalEntity is not { Valid: true } user)
            return false;

        body = user;

        var actionUi = ui.GetUIController<ActionUIController>();
        if (actionUi.SelectingTargetFor is not { } actionUid || !entityManager.EntityExists(actionUid))
            return false;

        if (!entityManager.TryGetComponent<ActionComponent>(actionUid, out var actionComp))
            return false;

        if (actionComp.AttachedEntity != user)
            return false;

        if (actionComp.Container is not { } container)
            return false;

        if (!entityManager.TryGetComponent<TScript>(container, out var resolvedScript) || resolvedScript == null)
            return false;

        scriptComp = resolvedScript;

        if (!entityManager.TryGetComponent<TargetActionComponent>(actionUid, out var targetComp))
            return false;

        range = MathF.Max(0f, targetComp.Range);
        return range > 0f;
    }

    public static bool TryBuildHighlightShape(
        IEntityManager entityManager,
        SharedTransformSystem xformSystem,
        EntityUid target,
        out HighlightShape shape)
    {
        shape = default;

        if (!TryGetMainFixture(entityManager, target, out var fixture))
            return false;

        if (!entityManager.TryGetComponent<TransformComponent>(target, out var xform))
            return false;

        var worldTransform = new PhysTransform(
            xformSystem.GetWorldPosition(xform),
            xformSystem.GetWorldRotation(xform));

        if (!TryGetShapeVertices(fixture.Shape, worldTransform, out var vertices) || vertices.Length < 3)
            return false;

        var bounds = ComputeBounds(vertices);
        var center = ComputeCenter(vertices);
        var outerVertices = ExpandPolygon(vertices, center, 0.03f);
        shape = new HighlightShape(vertices, outerVertices, bounds);
        return true;
    }

    public static void DrawHighlightShape(
        DrawingHandleWorld handle,
        in HighlightShape shape,
        Color fillColor,
        Color outerOutlineColor,
        Color innerOutlineColor)
    {
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, shape.Vertices, fillColor);
        DrawOutline(handle, shape.OuterVertices, outerOutlineColor);
        DrawOutline(handle, shape.Vertices, innerOutlineColor);
    }

    private static bool TryGetMainFixture(IEntityManager entityManager, EntityUid uid, out Fixture fixture)
    {
        fixture = default!;

        if (!entityManager.TryGetComponent<Robust.Shared.Physics.FixturesComponent>(uid, out var fixtures) || fixtures.Fixtures.Count == 0)
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
            PhysShapeAabb aabb => aabb.LocalBounds.Width * aabb.LocalBounds.Height,
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
            case PhysShapeAabb aabb:
            {
                vertices = new[]
                {
                    PhysTransform.Mul(transform, aabb.LocalBounds.BottomLeft),
                    PhysTransform.Mul(transform, aabb.LocalBounds.BottomRight),
                    PhysTransform.Mul(transform, aabb.LocalBounds.TopRight),
                    PhysTransform.Mul(transform, aabb.LocalBounds.TopLeft),
                };
                return true;
            }
            default:
            {
                var aabb = shape.ComputeAABB(transform, 0);
                vertices = new[]
                {
                    aabb.BottomLeft,
                    aabb.BottomRight,
                    aabb.TopRight,
                    aabb.TopLeft,
                };
                return true;
            }
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
}
