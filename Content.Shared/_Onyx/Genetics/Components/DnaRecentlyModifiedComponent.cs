namespace Content.Shared.Genetics;

[RegisterComponent]
public sealed partial class DnaRecentlyModifiedComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), DataField]
    public TimeSpan ExpiresAt;
}
