using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Damage.Prototypes;

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
    SolutionScanner,
}

[Serializable, NetSerializable]
public enum AugmentVisionOverlayType : byte
{
    HealthBars,
    HealthIcons,
    DiseaseIcons,
    JobIcons,
    CriminalRecordIcons,
    MindShieldIcons,
    SyndicateIcons,
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class AugmentVisionSettings
{
    [DataField]
    public float FlashDurationMultiplier = 1f;

    [DataField]
    public float PulseTime;

    [DataField]
    public bool DrawOverlay = true;

    [DataField]
    public float OverlayOpacity = 0.5f;

    [DataField]
    public float LightRadius = 2f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentVisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public AugmentVisionType? VisionType;

    [DataField, AutoNetworkedField]
    public HashSet<AugmentVisionType> VisionTypes = new();

    [DataField, AutoNetworkedField]
    public Dictionary<AugmentVisionType, AugmentVisionSettings> VisionSettings = new();

    [DataField, AutoNetworkedField]
    public float PowerDraw;

    [DataField, AutoNetworkedField]
    public Dictionary<AugmentVisionType, float> ActivePowerDrawByType = new();

    [DataField, AutoNetworkedField]
    public Dictionary<AugmentVisionType, float> ActiveNeuroLoadByType = new();

    [DataField, AutoNetworkedField]
    public bool RequiresPower = true;

    [DataField, AutoNetworkedField]
    public HashSet<AugmentVisionOverlayType> OverlayTypes = new();

    [DataField, AutoNetworkedField]
    public List<ProtoId<DamageContainerPrototype>> HealthBarDamageContainers = new()
    {
        "Biological",
    };

    [DataField, AutoNetworkedField]
    public List<ProtoId<DamageContainerPrototype>> HealthIconDamageContainers = new()
    {
        "Biological",
    };

    public AugmentVisionSettings GetSettings(AugmentVisionType type)
    {
        return VisionSettings.TryGetValue(type, out var settings) ? settings : new AugmentVisionSettings();
    }

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

    public float GetActivePowerDraw(AugmentVisionType type)
    {
        return ActivePowerDrawByType.GetValueOrDefault(type, PowerDraw);
    }

    public float GetActiveNeuroLoad(AugmentVisionType type)
    {
        return ActiveNeuroLoadByType.GetValueOrDefault(type, 0f);
    }

    public static bool IsToggleable(AugmentVisionType type)
    {
        return type is AugmentVisionType.NightVision or AugmentVisionType.ThermalVision;
    }
}
