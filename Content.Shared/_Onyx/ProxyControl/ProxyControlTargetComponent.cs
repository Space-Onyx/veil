namespace Content.Shared._Onyx.ProxyControl;

[RegisterComponent]
public sealed partial class ProxyControlTargetComponent : Component
{
    [DataField]
    public HashSet<EntityUid> Controllers = new();
}
