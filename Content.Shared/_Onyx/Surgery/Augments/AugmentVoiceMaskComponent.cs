using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared.Speech;
using Content.Shared.Humanoid;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentVoiceMaskComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? VoiceMaskName = null;

    [DataField, AutoNetworkedField]
    public ProtoId<SpeechVerbPrototype>? VoiceMaskSpeechVerb;

    [DataField, AutoNetworkedField]
    public string VoiceId = SharedHumanoidAppearanceSystem.DefaultVoice;

    [DataField, AutoNetworkedField]
    public string BarkId = "Human1";

    [DataField, AutoNetworkedField]
    public float BarkPitch = 1.0f;

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;
}
