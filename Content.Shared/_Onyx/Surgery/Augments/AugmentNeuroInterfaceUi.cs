using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;
using Robust.Shared.GameObjects;

namespace Content.Shared._Onyx.Surgery.Augments;

[Serializable, NetSerializable]
public enum NeuroInterfaceUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum NeuroInterfaceBodyCategory : byte
{
    Head,
    Torso,
    RightArm,
    LeftArm,
    RightHand,
    LeftHand,
    Groin,
    RightLeg,
    LeftLeg,
    RightFoot,
    LeftFoot,
}

[Serializable, NetSerializable]
public enum NeuroInterfaceAugmentStatus : byte
{
    Enabled,
    Disabled,
    Deactivated,
    NoPower,
}

[Serializable, NetSerializable]
public enum NeuroInterfaceBulkTarget : byte
{
    Implants,
    Limbs,
    All,
}

[Serializable, NetSerializable]
public sealed class NeuroInterfaceMetricEntry
{
    public string Label;
    public float Value;

    public NeuroInterfaceMetricEntry(string label, float value)
    {
        Label = label;
        Value = value;
    }
}

[Serializable, NetSerializable]
public sealed class NeuroInterfaceModuleEntry
{
    public NetEntity Module;
    public string SlotId;
    public string SlotName;
    public string Name;
    public string Description;

    public NeuroInterfaceModuleEntry(
        NetEntity module,
        string slotId,
        string slotName,
        string name,
        string description)
    {
        Module = module;
        SlotId = slotId;
        SlotName = slotName;
        Name = name;
        Description = description;
    }
}

[Serializable, NetSerializable]
public sealed class NeuroInterfaceAugmentEntry
{
    public NetEntity Augment;
    public NetEntity Part;
    public NeuroInterfaceBodyCategory Category;
    public string Name;
    public bool Enabled;
    public bool CanToggle;
    public bool CanConfigure;
    public NeuroInterfaceAugmentStatus Status;
    public string Description;
    public List<NeuroInterfaceMetricEntry> PassivePowerEntries;
    public List<NeuroInterfaceMetricEntry> ActivePowerEntries;
    public List<NeuroInterfaceMetricEntry> PassiveNeuroLoadEntries;
    public List<NeuroInterfaceMetricEntry> ActiveNeuroLoadEntries;
    public List<NeuroInterfaceModuleEntry> Modules;

    public NeuroInterfaceAugmentEntry(
        NetEntity augment,
        NetEntity part,
        NeuroInterfaceBodyCategory category,
        string name,
        bool enabled,
        bool canToggle,
        bool canConfigure,
        NeuroInterfaceAugmentStatus status,
        string description,
        List<NeuroInterfaceMetricEntry> passivePowerEntries,
        List<NeuroInterfaceMetricEntry> activePowerEntries,
        List<NeuroInterfaceMetricEntry> passiveNeuroLoadEntries,
        List<NeuroInterfaceMetricEntry> activeNeuroLoadEntries,
        List<NeuroInterfaceModuleEntry>? modules = null)
    {
        Augment = augment;
        Part = part;
        Category = category;
        Name = name;
        Enabled = enabled;
        CanToggle = canToggle;
        CanConfigure = canConfigure;
        Status = status;
        Description = description;
        PassivePowerEntries = passivePowerEntries;
        ActivePowerEntries = activePowerEntries;
        PassiveNeuroLoadEntries = passiveNeuroLoadEntries;
        ActiveNeuroLoadEntries = activeNeuroLoadEntries;
        Modules = modules ?? new List<NeuroInterfaceModuleEntry>();
    }
}

[Serializable, NetSerializable]
public sealed class NeuroInterfaceBuiState : BoundUserInterfaceState
{
    public string HexCode;
    public string PowerSourceName;
    public float PowerOutputPerSecond;
    public float PowerConsumptionPerSecond;
    public bool HasBattery;
    public float BatteryCurrent;
    public float BatteryMax;
    public float NeuroLoadCurrent;
    public float NeuroLoadMax;
    public List<NeuroInterfaceAugmentEntry> Augments;

    public NeuroInterfaceBuiState(
        string hexCode,
        string powerSourceName,
        float powerOutputPerSecond,
        float powerConsumptionPerSecond,
        bool hasBattery,
        float batteryCurrent,
        float batteryMax,
        float neuroLoadCurrent,
        float neuroLoadMax,
        List<NeuroInterfaceAugmentEntry> augments)
    {
        HexCode = hexCode;
        PowerSourceName = powerSourceName;
        PowerOutputPerSecond = powerOutputPerSecond;
        PowerConsumptionPerSecond = powerConsumptionPerSecond;
        HasBattery = hasBattery;
        BatteryCurrent = batteryCurrent;
        BatteryMax = batteryMax;
        NeuroLoadCurrent = neuroLoadCurrent;
        NeuroLoadMax = neuroLoadMax;
        Augments = augments;
    }
}

[Serializable, NetSerializable]
public sealed class NeuroInterfaceBulkToggleMessage : BoundUserInterfaceMessage
{
    public NeuroInterfaceBulkTarget Target;
    public bool Enable;

    public NeuroInterfaceBulkToggleMessage(NeuroInterfaceBulkTarget target, bool enable)
    {
        Target = target;
        Enable = enable;
    }
}

[Serializable, NetSerializable]
public sealed class NeuroInterfaceToggleAugmentMessage : BoundUserInterfaceMessage
{
    public NetEntity Augment;
    public bool Enable;

    public NeuroInterfaceToggleAugmentMessage(NetEntity augment, bool enable)
    {
        Augment = augment;
        Enable = enable;
    }
}