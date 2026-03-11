using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

[Serializable, NetSerializable]
public sealed class AugmentSuppressionZoneVisualizationEvent : EntityEventArgs
{
    [Serializable, NetSerializable]
    public sealed class ZoneData
    {
        public NetEntity Projector { get; }
        public float Radius { get; }
        public AugmentSuppressionFieldShape Shape { get; }
        public bool Active { get; }
        public Vector2 Center { get; }
        public float Rotation { get; }

        public ZoneData(
            NetEntity projector,
            float radius,
            AugmentSuppressionFieldShape shape,
            bool active,
            Vector2 center,
            float rotation)
        {
            Projector = projector;
            Radius = radius;
            Shape = shape;
            Active = active;
            Center = center;
            Rotation = rotation;
        }
    }

    public bool Enabled { get; }
    public List<ZoneData> Zones { get; }

    public AugmentSuppressionZoneVisualizationEvent(List<ZoneData> zones)
    {
        Enabled = true;
        Zones = zones;
    }

    public AugmentSuppressionZoneVisualizationEvent()
    {
        Enabled = false;
        Zones = new List<ZoneData>();
    }
}
