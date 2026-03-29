/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */


using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Silicons.StationAi;

namespace Content.Shared._CE.ZLevels.Ghost;

public abstract class CESharedZLevelGhostMoverSystem : EntitySystem
{
    [Dependency] private readonly CESharedZLevelsSystem _zLevel = default!;
    [Dependency] private readonly SharedStationAiSystem _stationAi = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZLevelGhostMoverComponent, CEZLevelActionUp>(OnZLevelUp);
        SubscribeLocalEvent<CEZLevelGhostMoverComponent, CEZLevelActionDown>(OnZLevelDown);
        SubscribeLocalEvent<StationAiHeldComponent, CEZLevelActionUp>(OnHeldAiZLevelUp); // <Onyx-Tweak>
        SubscribeLocalEvent<StationAiHeldComponent, CEZLevelActionDown>(OnHeldAiZLevelDown); // <Onyx-Tweak>
    }

    private void OnZLevelDown(Entity<CEZLevelGhostMoverComponent> ent, ref CEZLevelActionDown args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveDown(ent);
    }

    private void OnZLevelUp(Entity<CEZLevelGhostMoverComponent> ent, ref CEZLevelActionUp args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveUp(ent);
    }

    // <Onyx-Tweak>
    private void OnHeldAiZLevelDown(Entity<StationAiHeldComponent> ent, ref CEZLevelActionDown args)
    {
        if (args.Handled)
            return;

        if (!_stationAi.TryGetCore(ent.Owner, out var core) || core.Comp?.RemoteEntity is not { } remoteEye)
            return;

        args.Handled = _zLevel.TryMoveDown(remoteEye);
    }

    private void OnHeldAiZLevelUp(Entity<StationAiHeldComponent> ent, ref CEZLevelActionUp args)
    {
        if (args.Handled)
            return;

        if (!_stationAi.TryGetCore(ent.Owner, out var core) || core.Comp?.RemoteEntity is not { } remoteEye)
            return;

        args.Handled = _zLevel.TryMoveUp(remoteEye);
    }
    // </Onyx-Tweak>
}
