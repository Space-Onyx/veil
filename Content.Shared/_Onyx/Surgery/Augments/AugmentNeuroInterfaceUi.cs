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
}

[Serializable, NetSerializable]
public sealed class NeuroInterfaceAugmentEntry
{
    public NetEntity Augment;
    public NeuroInterfaceBodyCategory Category;
    public string Name;
    public bool Enabled;
    public bool CanToggle;
    public bool CanConfigure;
    public NeuroInterfaceAugmentStatus Status;

    public NeuroInterfaceAugmentEntry(
        NetEntity augment,
        NeuroInterfaceBodyCategory category,
        string name,
        bool enabled,
        bool canToggle,
        bool canConfigure,
        NeuroInterfaceAugmentStatus status)
    {
        Augment = augment;
        Category = category;
        Name = name;
        Enabled = enabled;
        CanToggle = canToggle;
        CanConfigure = canConfigure;
        Status = status;
    }
}

[Serializable, NetSerializable]
public sealed class NeuroInterfaceBuiState : BoundUserInterfaceState
{
    public string HexCode;
    public bool HasBattery;
    public float BatteryCurrent;
    public float BatteryMax;
    public List<NeuroInterfaceAugmentEntry> Augments;

    public NeuroInterfaceBuiState(string hexCode, bool hasBattery, float batteryCurrent, float batteryMax, List<NeuroInterfaceAugmentEntry> augments)
    {
        HexCode = hexCode;
        HasBattery = hasBattery;
        BatteryCurrent = batteryCurrent;
        BatteryMax = batteryMax;
        Augments = augments;
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



