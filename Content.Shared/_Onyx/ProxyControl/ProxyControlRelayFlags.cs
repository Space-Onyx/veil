using System;

namespace Content.Shared._Onyx.ProxyControl;

[Flags]
public enum ProxyControlRelayFlags : byte
{
    None = 0,
    Camera = 1 << 0,
    Movement = 1 << 1,
    Interaction = 1 << 2,
    Hands = 1 << 3,
    Inventory = 1 << 4,
    Actions = 1 << 5,
    Speech = 1 << 6,

    UserInterface = Interaction | Hands | Inventory | Actions,
}
