using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Onyx.ZLevels.Core.Components;

[RegisterComponent]
public sealed partial class CEZCrossMapLinksComponent : Component
{
    [DataField]
    public List<CEZCrossMapDeviceLink> DeviceLinks = new();
}

[DataDefinition]
public sealed partial class CEZCrossMapDeviceLink
{
    [DataField] public int SourceDepth;
    [DataField] public float SourceX;
    [DataField] public float SourceY;
    [DataField] public string SourcePrototype = "";
    [DataField] public int SinkDepth;
    [DataField] public float SinkX;
    [DataField] public float SinkY;
    [DataField] public string SinkPrototype = "";
    [DataField] public List<string> PortLinks = new();
}
