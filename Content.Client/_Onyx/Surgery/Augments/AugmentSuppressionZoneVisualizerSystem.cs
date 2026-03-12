using System.Numerics;
using Content.Shared._Onyx.Surgery.Augments;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._Onyx.Surgery.Augments;

public sealed class AugmentSuppressionZoneVisualizerSystem : EntitySystem
{
    private static readonly Color ActiveZoneColor = new(42, 145, 235);
    private static readonly Color InactiveZoneColor = new(150, 150, 150);
    private const byte ActiveFillAlpha = 52;
    private const byte InactiveFillAlpha = 28;
    private const int CircleSegments = 48;

    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    private readonly Dictionary<EntityUid, ZoneDrawData> _zones = new();

    private sealed class ZoneDrawData
    {
        public float Radius;
        public AugmentSuppressionFieldShape Shape;
        public bool Active;
        public Vector2 Center;
        public Angle Rotation;
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<AugmentSuppressionZoneVisualizationEvent>(OnVisualizationEvent);
        _overlayManager.AddOverlay(new AugmentSuppressionZoneOverlay(this, EntityManager));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<AugmentSuppressionZoneOverlay>();
    }

    private void OnVisualizationEvent(AugmentSuppressionZoneVisualizationEvent ev)
    {
        if (!ev.Enabled)
        {
            _zones.Clear();
            return;
        }

        _zones.Clear();
        foreach (var zone in ev.Zones)
        {
            var uid = GetEntity(zone.Projector);
            _zones[uid] = new ZoneDrawData
            {
                Radius = MathF.Max(0f, zone.Radius),
                Shape = zone.Shape,
                Active = zone.Active,
                Center = zone.Center,
                Rotation = new Angle(zone.Rotation),
            };
        }
    }

    private sealed class AugmentSuppressionZoneOverlay : Overlay
    {
        private readonly AugmentSuppressionZoneVisualizerSystem _system;
        private readonly IEntityManager _entityManager;
        private readonly Vector2[] _circleVertices = new Vector2[CircleSegments + 2];
        private readonly Vector2[] _squareVertices = new Vector2[6];

        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        public AugmentSuppressionZoneOverlay(AugmentSuppressionZoneVisualizerSystem system, IEntityManager entityManager)
        {
            _system = system;
            _entityManager = entityManager;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (_system._zones.Count == 0)
                return;
            var handle = args.WorldHandle;
            foreach (var (projector, zone) in _system._zones)
            {
                if (zone.Radius <= 0f)
                    continue;

                if (!_entityManager.EntityExists(projector))
                    continue;

                var center = zone.Center;
                var rotation = zone.Rotation;
                var baseColor = zone.Active ? ActiveZoneColor : InactiveZoneColor;
                var fill = baseColor.WithAlpha(zone.Active ? ActiveFillAlpha : InactiveFillAlpha);
                var centerColor = zone.Active
                    ? new Color(255, 255, 120, 240)
                    : new Color(210, 210, 210, 200);

                switch (zone.Shape)
                {
                    case AugmentSuppressionFieldShape.Circle:
                        DrawCircleFan(handle, center, zone.Radius, fill);
                        break;

                    case AugmentSuppressionFieldShape.Square:
                        DrawSquareFan(handle, center, zone.Radius, rotation, fill);
                        break;
                }

                handle.DrawCircle(center, 0.15f, centerColor, true);
            }
        }

        private void DrawCircleFan(DrawingHandleWorld handle, Vector2 center, float radius, Color color)
        {
            _circleVertices[0] = center;

            for (var i = 0; i <= CircleSegments; i++)
            {
                var angle = MathF.Tau * i / CircleSegments;
                var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                _circleVertices[i + 1] = center + offset;
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _circleVertices, color);
        }

        private void DrawSquareFan(DrawingHandleWorld handle, Vector2 center, float radius, Angle rotation, Color color)
        {
            _squareVertices[0] = center;
            _squareVertices[1] = center + rotation.RotateVec(new Vector2(-radius, -radius));
            _squareVertices[2] = center + rotation.RotateVec(new Vector2(radius, -radius));
            _squareVertices[3] = center + rotation.RotateVec(new Vector2(radius, radius));
            _squareVertices[4] = center + rotation.RotateVec(new Vector2(-radius, radius));
            _squareVertices[5] = _squareVertices[1];

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _squareVertices, color);
        }
    }
}
