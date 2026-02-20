namespace Content._Onyx.Shared.Inventory;

/// <summary>
/// Позволяет сущности обходить ограничения SlotBlockComponent, что дает возможность снимать предметы, даже если слоты заблокированы.
/// </summary>
[RegisterComponent]
public sealed partial class StopBlockBypassComponent : Component
{
}