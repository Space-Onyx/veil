using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared._Utopia.ZLevels.Systems;

namespace Content.Server._Utopia.ZLevels.Systems;

public sealed class GridMotionLinkSystem : SharedGridMotionLinkSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridMotionLinkComponent, GridFixtureChangeEvent>(OnGridFixtureChange);
    }

    protected override void OnGridMotionLinkMapInit(Entity<GridMotionLinkComponent> ent, ref MapInitEvent args)
    {
        UpdateOffset(ent);
    }

    private void OnGridFixtureChange(Entity<GridMotionLinkComponent> ent, ref GridFixtureChangeEvent args)
    {
        UpdateOffset(ent);
        Dirty(ent);
    }
}
