using System;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentItemPanelComponent : Component
{
    [DataField(required: true)]
    public EntProtoId ItemPrototype = string.Empty;

    [DataField]
    public SpriteSpecifier? Icon;

    [DataField, AutoNetworkedField]
    public EntityUid? SpawnedItem;

    [DataField, AutoNetworkedField]
    public bool IsEquipped = false;

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField]
    public float ExtendPowerCost = 2f;

    [DataField]
    public float RetractPowerCost = 2f;

    [DataField]
    public float EquippedNeuroLoad = 0f;

    [DataField]
    public bool RequiresPower = true;

    [DataField]
    public TimeSpan ActionCooldown = TimeSpan.FromSeconds(2f);

    [DataField]
    public SoundSpecifier? ExtendSound;

    [DataField]
    public SoundSpecifier? RetractSound;

    /// <summary>
    /// Temporary held prefix applied when deploying an item from the panel.
    /// Useful for popout animations in in-hand states.
    /// </summary>
    [DataField]
    public string? ExtendHeldPrefix;

    /// <summary>
    /// How long the deploy held prefix should stay before resetting.
    /// </summary>
    [DataField]
    public TimeSpan ExtendHeldPrefixDuration = TimeSpan.FromSeconds(0.3f);

    /// <summary>
    /// Held prefix to apply after deploy animation finishes.
    /// Null resets to default in-hand states.
    /// </summary>
    [DataField]
    public string? ExtendHeldPrefixAfter;

    // Backwards-compatible aliases for existing prototypes.
    [DataField("deploySound")]
    public SoundSpecifier? LegacyDeploySound
    {
        get => ExtendSound;
        set => ExtendSound = value;
    }

    [DataField("deployHeldPrefix")]
    public string? LegacyDeployHeldPrefix
    {
        get => ExtendHeldPrefix;
        set => ExtendHeldPrefix = value;
    }

    [DataField("deployHeldPrefixDuration")]
    public TimeSpan LegacyDeployHeldPrefixDuration
    {
        get => ExtendHeldPrefixDuration;
        set => ExtendHeldPrefixDuration = value;
    }

    [DataField("deployHeldPrefixAfter")]
    public string? LegacyDeployHeldPrefixAfter
    {
        get => ExtendHeldPrefixAfter;
        set => ExtendHeldPrefixAfter = value;
    }

    // Backwards-compatible alias for older prototypes that used one shared power cost.
    [DataField("powerCost")]
    public float LegacyPowerCost
    {
        get => ExtendPowerCost;
        set
        {
            ExtendPowerCost = value;
            RetractPowerCost = value;
        }
    }
}
