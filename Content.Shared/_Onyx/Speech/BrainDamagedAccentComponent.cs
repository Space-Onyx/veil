using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Speech;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BrainDamagedAccentComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MessageReplaceChance = 0.1f;

    [DataField, AutoNetworkedField]
    public float LetterSwapChance = 0.2f;
}
