using Content.Shared._Utopia.ZLevels.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Content.Shared._Utopia.ZLevels.Systems;

public abstract class SharedGridMotionLinkSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private const string GlobalGroupId = "ZZZ";
    // <Onyx-Tweak>
    private readonly Dictionary<EntityUid, int> _gridTileCountCache = new();
    private readonly Dictionary<string, EntityUid> _biggestGridCache = new();
    private bool _biggestGridCacheDirty = true;
    private readonly List<Entity<GridMotionLinkComponent, MapGridComponent, PhysicsComponent>> _groupMatchBuffer = new();
    private readonly HashSet<string> _groupsMovedSet = new();
    // <Onyx-Tweak>
    private readonly Dictionary<string, List<Entity<GridMotionLinkComponent, MapGridComponent, PhysicsComponent>>> _groupDictionary = new();
    private bool _groupDictionaryBuilt;
    // </Onyx-Tweak>
    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(SharedPhysicsSystem));
        // <Onyx-Tweak>
        SubscribeLocalEvent<GridMotionLinkComponent, MapInitEvent>(OnGridMotionLinkInit);
        SubscribeLocalEvent<GridMotionLinkComponent, ComponentRemove>(OnGridMotionLinkRemove);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChangedGridMotion);
        // </Onyx-Tweak>
    }
    // <Onyx-Tweak Edited>

    private void OnGridMotionLinkInit(Entity<GridMotionLinkComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<MapGridComponent>(ent, out var grid))
        {
            var count = 0;
            var enumerator = _map.GetAllTilesEnumerator(ent, grid, true);
            while (enumerator.MoveNext(out _))
                count++;
            _gridTileCountCache[ent] = count;
        }
        _biggestGridCacheDirty = true;
        OnGridMotionLinkMapInit(ent, ref args);
    }

    private void OnGridMotionLinkRemove(Entity<GridMotionLinkComponent> ent, ref ComponentRemove args)
    {
        _gridTileCountCache.Remove(ent);
        _biggestGridCacheDirty = true;
    }

    protected virtual void OnGridMotionLinkMapInit(Entity<GridMotionLinkComponent> ent, ref MapInitEvent args)
    {
    }

    private void OnTileChangedGridMotion(ref TileChangedEvent ev)
    {
        if (!HasComp<GridMotionLinkComponent>(ev.Entity))
            return;

        foreach (var change in ev.Changes)
        {
            var wasEmpty = change.OldTile.IsEmpty;
            var isNowEmpty = change.NewTile.IsEmpty;

            if (wasEmpty && !isNowEmpty)
            {
                _gridTileCountCache.TryGetValue(ev.Entity, out var count);
                _gridTileCountCache[ev.Entity] = count + 1;
            }
            else if (!wasEmpty && isNowEmpty)
            {
                if (_gridTileCountCache.TryGetValue(ev.Entity, out var count))
                    _gridTileCountCache[ev.Entity] = Math.Max(0, count - 1);
            }
        }

        _biggestGridCacheDirty = true;
    }

    private int GetCachedTileCount(EntityUid uid, MapGridComponent grid)
    {
        if (_gridTileCountCache.TryGetValue(uid, out var cached))
            return cached;

        var count = 0;
        var enumerator = _map.GetAllTilesEnumerator(uid, grid, true);
        while (enumerator.MoveNext(out _))
            count++;
        _gridTileCountCache[uid] = count;
        return count;
    }
    // </Onyx-Tweak Edited>


    public void UpdateOffset(Entity<GridMotionLinkComponent> ent)
    {
        // <Onyx-Tweak>
        var oldRoot = ent.Comp.Root;
        var oldOffset = ent.Comp.Offset;
        // <Onyx-Tweak>

        ent.Comp.Root = GetBiggestGridOfGroup(ent.Comp.GroupId);

        if (ent.Comp.Root is not { Valid: true })
            ent.Comp.Root = ent.Owner;

        // <Onyx-Tweak>
        if (!ent.Comp.AutoCalculateOffset)
        {
            if (ent.Comp.Root != ent.Owner)
                SetOffsetPos(ent.Comp.Root, ent);

            if (oldRoot != ent.Comp.Root)
                Dirty(ent);
            return;
        }
        // <Onyx-Tweak>

        if (ent.Comp.Root == ent.Owner)
            ent.Comp.Offset = Vector2.Zero;
        else
        {
            var rootRot = _transformSystem.GetWorldRotation(ent.Comp.Root);
            var rootPos = _transformSystem.GetWorldPosition(ent.Comp.Root);
            var entPos = _transformSystem.GetWorldPosition(ent.Owner);

            var q = new Quaternion2D(rootRot);
            ent.Comp.Offset = Quaternion2D.InvRotateVector(q, entPos - rootPos);

            _transformSystem.SetWorldRotation(ent.Owner, rootRot);
        }

        if (oldRoot != ent.Comp.Root || oldOffset != ent.Comp.Offset) // <Onyx-Tweak>
            Dirty(ent);
    }

    public void InitializeGrid(EntityUid gridUid)
    {
        var link = EnsureComp<GridMotionLinkComponent>(gridUid);
        link.GroupId = GlobalGroupId;
    }

    private void RelayMotion(Vector2 linear, float angular,
                             EntityUid biggest,
                             List<Entity<GridMotionLinkComponent, MapGridComponent, PhysicsComponent>> matches)
    {
        foreach (var (targetUid, link, grid, phys) in matches)
        {
            _physics.SetLinearVelocity(targetUid, linear, body: phys);
            _physics.SetAngularVelocity(targetUid, angular, body: phys);

            if (biggest != targetUid)
                SetOffsetPos(biggest, (targetUid, link));
        }
    }

    private void SetOffsetPos(EntityUid biggest, Entity<GridMotionLinkComponent> ent)
    {
        var rootRot = _transformSystem.GetWorldRotation(biggest);
        var rootPos = _transformSystem.GetWorldPosition(biggest);

        var q = new Quaternion2D(rootRot);
        var newPos = rootPos + Quaternion2D.RotateVector(q, ent.Comp.Offset);

        _transformSystem.SetWorldPosition(ent.Owner, newPos);
        _transformSystem.SetWorldRotation(ent.Owner, rootRot);
        _physics.WakeBody(ent.Owner);
    }

    private bool TryGetMotionData(EntityUid uid,
                                 [NotNullWhen(true)] out Vector2? linearSpeed,
                                 [NotNullWhen(true)] out float? angularSpeed,
                                 [NotNullWhen(true)] out EntityUid? biggestGrid,
                                  GridMotionLinkComponent? comp = null)
    {
        linearSpeed = Vector2.Zero;
        angularSpeed = 0f;
        biggestGrid = null;

        if (!Resolve(uid, ref comp))
            return false;

        GetGridsOfGroup(comp.GroupId); // <Onyx-Tweak Edited>

        if (_groupMatchBuffer.Count == 0) // <Onyx-Tweak Edited>
            return false;

        var biggest = new KeyValuePair<int, EntityUid>(int.MinValue, EntityUid.Invalid); // <Onyx-Tweak>
        foreach (var (targetUid, link, grid, phys) in _groupMatchBuffer) // <Onyx-Tweak Edited>
        {
            if (link.GroupId != comp.GroupId)
                continue;

            linearSpeed += phys.LinearVelocity;
            angularSpeed += phys.AngularVelocity;

            var tilesCount = GetCachedTileCount(targetUid, grid); // <Onyx-Tweak Edited>

            if (!biggest.Value.Valid || biggest.Key < tilesCount) // <Onyx-Tweak>
                biggest = new(tilesCount, targetUid);
        }

        // <Onyx-Tweak Edited>
        linearSpeed /= _groupMatchBuffer.Count;
        angularSpeed /= _groupMatchBuffer.Count;
        // </Onyx-Tweak Edited>
        biggestGrid = biggest.Value;
        return true;
    }

    // <Onyx-Tweak>
    private void EnsureGroupDictionary()
    {
        if (_groupDictionaryBuilt)
            return;
        _groupDictionaryBuilt = true;

        foreach (var list in _groupDictionary.Values)
            list.Clear();

        var query = EntityQueryEnumerator<GridMotionLinkComponent, MapGridComponent, PhysicsComponent>();
        while (query.MoveNext(out var targetUid, out var link, out var grid, out var phys))
        {
            if (string.IsNullOrEmpty(link.GroupId))
                continue;

            if (!_groupDictionary.TryGetValue(link.GroupId, out var list))
            {
                list = new List<Entity<GridMotionLinkComponent, MapGridComponent, PhysicsComponent>>();
                _groupDictionary[link.GroupId] = list;
            }
            list.Add((targetUid, link, grid, phys));
        }
    }

    private void GetGridsOfGroup(string groupId)
    {
        _groupMatchBuffer.Clear();

        EnsureGroupDictionary();

        if (_groupDictionary.TryGetValue(groupId, out var cached))
        {
            _groupMatchBuffer.AddRange(cached);
        }
    }
    // </Onyx-Tweak>

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _groupDictionaryBuilt = false; // <Onyx-Tweak>
        // <Onyx-Tweak>
        if (_biggestGridCacheDirty)
        {
            _biggestGridCache.Clear();
            _biggestGridCacheDirty = false;
        }
        // </Onyx-Tweak>

        var query = EntityQueryEnumerator<GridMotionLinkComponent, PhysicsComponent>();
        _groupsMovedSet.Clear(); // <Onyx-Tweak>

        while (query.MoveNext(out var uid, out var comp, out var phys))
        {
            if (!_groupsMovedSet.Add(comp.GroupId)) // <Onyx-Tweak Edited>
                continue;

            if (!TryGetMotionData(uid, out var linear, out var angular, out var biggest, comp)) // <Onyx-Tweak Edited>
                continue;

            RelayMotion(linear.Value, angular.Value, biggest.Value, _groupMatchBuffer); // <Onyx-Tweak Edited>
        }

        // <Onyx-Tweak>
        var linkQuery = EntityQueryEnumerator<GridMotionLinkComponent>();
        while (linkQuery.MoveNext(out var uid, out var link))
        {
            if (link.AutoCalculateOffset)
                continue;

            UpdateOffset((uid, link));
        }
        // </Onyx-Tweak>
    }

    public void SetGridPosition(EntityUid origin, Vector2 position, GridMotionLinkComponent? link = null)
    {
        if (!Resolve(origin, ref link, false))
        {
            _transformSystem.SetWorldPosition(origin, position);
            return;
        }

        var diff = position - _transformSystem.GetWorldPosition(origin);

        var query = EntityQueryEnumerator<GridMotionLinkComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (link.GroupId != comp.GroupId)
                continue;

            var targetPos = _transformSystem.GetWorldPosition(uid) + diff;
            _transformSystem.SetWorldPosition(uid, targetPos);
            _physics.SetLinearVelocity(uid, Vector2.Zero);
        }
    }

    private EntityUid GetBiggestGridOfGroup(string group)
    {
        // <Onyx-Tweak Edited>
        if (!_biggestGridCacheDirty &&
            _biggestGridCache.TryGetValue(group, out var cachedBiggest) &&
            cachedBiggest.Valid)
        {
            return cachedBiggest;
        }

        var biggest = new KeyValuePair<int, EntityUid>(int.MinValue, EntityUid.Invalid);
        var query = EntityQueryEnumerator<GridMotionLinkComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var link, out var grid))
        {
            if (link.GroupId != group)
                continue;

            var tilesCount = GetCachedTileCount(uid, grid);

            if (!biggest.Value.Valid || biggest.Key < tilesCount)
                biggest = new(tilesCount, uid);
        }

        _biggestGridCache[group] = biggest.Value;
        return biggest.Value;
        // </Onyx-Tweak Edited>
    }
}
