using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

[Serializable, NetSerializable]
public enum AugmentVisionType : byte
{
    NightVision,
    ThermalVision,
    FlashProtection,
    MedicalHUD,
    SecurityHUD,
    DiagnosticHUD,
    SyndicateHUD,
    MindShieldHUD,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentVisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public AugmentVisionType? VisionType;

    [DataField, AutoNetworkedField]
    public HashSet<AugmentVisionType> VisionTypes = new();
    public IEnumerable<AugmentVisionType> GetAllVisionTypes()
    {
        if (VisionType.HasValue)
            yield return VisionType.Value;

        foreach (var type in VisionTypes)
        {
            if (!VisionType.HasValue || type != VisionType.Value)
                yield return type;
        }
    }
    public bool HasVisionType(AugmentVisionType type)
    {
        return VisionType == type || VisionTypes.Contains(type);
    }
}
