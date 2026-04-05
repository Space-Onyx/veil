using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.ZLevels.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZSpaceMoverComponent : Component
{
    public EntProtoId UpActionProto = "ActionZSpaceMoveUp";

    [AutoNetworkedField]
    public EntityUid? UpActionEntity;

    public EntProtoId DownActionProto = "ActionZSpaceMoveDown";

    [AutoNetworkedField]
    public EntityUid? DownActionEntity;
}
