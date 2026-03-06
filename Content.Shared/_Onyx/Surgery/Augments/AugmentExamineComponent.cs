using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentExamineComponent : Component
{
    [DataField, AutoNetworkedField]
    public string ExamineText = string.Empty;

    [DataField, AutoNetworkedField]
    public string? ExaminePartText;

    [DataField, AutoNetworkedField]
    public string Color = "#5bcefa";

    [DataField, AutoNetworkedField]
    public bool Visible = true;
}
