using Content.Server._Onyx.Medical;
using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;

namespace Content.Server._Onyx.Medical.Components;

[RegisterComponent]
[Access(typeof(BodyScannerConsoleSystem))]
public sealed partial class BodyScannerConsoleComponent : Component
{
    [DataField]
    public ProtoId<SinkPortPrototype> OperatingTablePort = "OperatingTableReceiver";

    [DataField]
    public EntityUid? LinkedSource;
}

