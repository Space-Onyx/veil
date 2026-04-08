/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Diagnostics.CodeAnalysis;

using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Gravity;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using JetBrains.Annotations;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared._Onyx.ZLevels.Roof.EntitySystems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Onyx.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] protected readonly IConfigurationManager Cfg = default!;
    [Dependency] private readonly FixtureSystem _fix = default!; // ECHO-Tweak: для улучшения системы
    [Dependency] protected readonly IMapManager _mapManager = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] protected readonly SharedTileZRoofSystem TileZRoof = default!;

    private EntityQuery<MapComponent> _mapQuery;
    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    protected EntityQuery<CEZPhysicsComponent> ZPhyzQuery = default!;
    private EntityQuery<GravityComponent> _gravityQuery;
    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;
    private readonly Dictionary<EntityUid, EntityUid> _mapToNetwork = new();
    private readonly Dictionary<EntityUid, List<(int Depth, EntityUid MapUid)>> _sortedMaps = new();
    private bool _networkCacheDirty = true;
    private readonly Dictionary<MapId, List<Entity<MapGridComponent>>> _gridsPerMapCache = new();
    private bool _gridsPerMapDirty = true;
    private readonly Dictionary<MapId, TimeSpan> _nextEmptyGridProbe = new();
    private readonly Dictionary<MapId, bool> _mapGravityCache = new();
    private static readonly TimeSpan EmptyGridProbeInterval = TimeSpan.FromSeconds(0.5f);

    public override void Initialize()
    {
        base.Initialize();

        _mapQuery = GetEntityQuery<MapComponent>();
        _zMapQuery = GetEntityQuery<CEZLevelMapComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        ZPhyzQuery = GetEntityQuery<CEZPhysicsComponent>();
        _gravityQuery = GetEntityQuery<GravityComponent>();
        _motionLinkQuery = GetEntityQuery<GridMotionLinkComponent>();
        SubscribeLocalEvent<CEZLevelsNetworkComponent, ComponentStartup>(OnNetworkStartup);
        SubscribeLocalEvent<CEZLevelsNetworkComponent, ComponentShutdown>(OnNetworkShutdown);
        SubscribeLocalEvent<GravityChangedEvent>(OnGravityChanged);
        SubscribeLocalEvent<GridAddEvent>(OnGridAdded);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);

        InitMovement();
        InitView();
        InitializeActivation();
    }

    private void OnNetworkStartup(Entity<CEZLevelsNetworkComponent> ent, ref ComponentStartup args)
    {
        InvalidateNetworkCache();
    }

    private void OnNetworkShutdown(Entity<CEZLevelsNetworkComponent> ent, ref ComponentShutdown args)
    {
        InvalidateNetworkCache();
    }

    public void InvalidateNetworkCache()
    {
        _networkCacheDirty = true;
        _mapGravityCache.Clear();
        InvalidateAllGroundCaches();
    }

    private void OnGridAdded(GridAddEvent ev)
    {
        _gridsPerMapDirty = true;
        _nextEmptyGridProbe.Clear();
        _mapGravityCache.Clear();

        if (Transform(ev.EntityUid).MapUid is { } mapUid)
            InvalidateGroundCacheForMap(mapUid, includeAdjacentMaps: true);
    }

    private void OnGridRemoved(GridRemovalEvent ev)
    {
        _gridsPerMapDirty = true;
        _nextEmptyGridProbe.Clear();
        _mapGravityCache.Clear();

        if (Transform(ev.EntityUid).MapUid is { } mapUid)
            InvalidateGroundCacheForMap(mapUid, includeAdjacentMaps: true);
    }

    private void OnGravityChanged(ref GravityChangedEvent ev)
    {
        _mapGravityCache.Clear();
    }

    protected List<Entity<MapGridComponent>> GetCachedGrids(MapId mapId)
    {
        if (_gridsPerMapDirty)
        {
            _gridsPerMapDirty = false;
            _nextEmptyGridProbe.Clear();
            foreach (var list in _gridsPerMapCache.Values)
                list.Clear();

            var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (gridQuery.MoveNext(out var uid, out var grid, out var xform))
            {
                var gridMapId = xform.MapID;
                if (!_gridsPerMapCache.TryGetValue(gridMapId, out var list))
                {
                    list = new List<Entity<MapGridComponent>>();
                    _gridsPerMapCache[gridMapId] = list;
                }
                list.Add((uid, grid));
            }
        }

        if (_gridsPerMapCache.TryGetValue(mapId, out var cached))
        {
            if (cached.Count > 0)
                return cached;

            if (!_nextEmptyGridProbe.TryGetValue(mapId, out var nextProbeAt) || _timing.CurTime >= nextProbeAt)
            {
                _nextEmptyGridProbe[mapId] = _timing.CurTime + EmptyGridProbeInterval;
                return RebuildSingleMapGridCache(mapId);
            }

            return cached;
        }

        return RebuildSingleMapGridCache(mapId);
    }

    private List<Entity<MapGridComponent>> RebuildSingleMapGridCache(MapId mapId)
    {
        if (!_gridsPerMapCache.TryGetValue(mapId, out var list))
        {
            list = new List<Entity<MapGridComponent>>();
            _gridsPerMapCache[mapId] = list;
        }
        else
        {
            list.Clear();
        }

        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            list.Add((uid, grid));
        }

        return list;
    }
    private void EnsureNetworkCache()
    {
        if (!_networkCacheDirty)
            return;

        _networkCacheDirty = false;
        _mapToNetwork.Clear();
        _sortedMaps.Clear();

        var query = EntityQueryEnumerator<CEZLevelsNetworkComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var sorted = new List<(int Depth, EntityUid MapUid)>();

            foreach (var (depth, mapUid) in comp.ZLevels)
            {
                if (!mapUid.HasValue)
                    continue;

                _mapToNetwork[mapUid.Value] = uid;
                sorted.Add((depth, mapUid.Value));
            }

            sorted.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            _sortedMaps[uid] = sorted;
        }
    }

    private static int CountInitializedNetworkMaps(CEZLevelsNetworkComponent network)
    {
        var count = 0;
        foreach (var (_, mapUid) in network.ZLevels)
        {
            if (mapUid.HasValue)
                count++;
        }

        return count;
    }

    [PublicAPI]
    public bool AreOnSameZNetwork(EntityUid mapUidA, EntityUid mapUidB)
    {
        if (mapUidA == mapUidB)
            return true;

        EnsureNetworkCache();

        return _mapToNetwork.TryGetValue(mapUidA, out var netA)
            && _mapToNetwork.TryGetValue(mapUidB, out var netB)
            && netA == netB;
    }

    /// <summary>
    /// Checks whether the map is in the zLevels network. If so, returns true and the current depth + Entity of the current zLevels network.
    /// </summary>
    [PublicAPI]
    public bool TryGetZNetwork(EntityUid mapUid, [NotNullWhen(true)] out Entity<CEZLevelsNetworkComponent>? zLevel)
    {
        zLevel = null;
        EnsureNetworkCache();

        if (!_mapToNetwork.TryGetValue(mapUid, out var networkUid))
        {
            _networkCacheDirty = true;
            EnsureNetworkCache();
            if (!_mapToNetwork.TryGetValue(mapUid, out networkUid))
                return false;
        }

        if (!TryComp<CEZLevelsNetworkComponent>(networkUid, out var comp))
            return false;

        zLevel = (networkUid, comp);
        return true;
    }

    [PublicAPI]
    public bool TryMapOffset(Entity<CEZLevelMapComponent?> inputMapUid,
        int offset,
        [NotNullWhen(true)] out Entity<CEZLevelMapComponent>? outputMapUid)
    {
        outputMapUid = null;
        if (!Resolve(inputMapUid, ref inputMapUid.Comp, false))
            return false;

        EnsureNetworkCache();

        if (!_mapToNetwork.TryGetValue(inputMapUid, out var networkUid))
        {
            _networkCacheDirty = true;
            EnsureNetworkCache();
            if (!_mapToNetwork.TryGetValue(inputMapUid, out networkUid))
                return false;
        }

        if (!TryComp<CEZLevelsNetworkComponent>(networkUid, out var networkComp))
            return false;

        if (!networkComp.ZLevels.TryGetValue(inputMapUid.Comp.Depth + offset, out var targetMapUid))
            return false;

        if (!_zMapQuery.TryComp(targetMapUid, out var targetZLevelComp))
            return false;

        outputMapUid = (targetMapUid.Value, targetZLevelComp);
        return true;
    }

    [PublicAPI]
    public bool TryMapUp(Entity<CEZLevelMapComponent?> inputMapUid,
        [NotNullWhen(true)] out Entity<CEZLevelMapComponent>? aboveMapUid)
    {
        return TryMapOffset(inputMapUid, 1, out aboveMapUid);
    }

    [PublicAPI]
    public bool TryMapDown(Entity<CEZLevelMapComponent?> inputMapUid,
        [NotNullWhen(true)] out Entity<CEZLevelMapComponent>? belowMapUid)
    {
        return TryMapOffset(inputMapUid, -1, out belowMapUid);
    }

    /// <summary>
    /// Returns a list of all maps above the specified map. The closest map at the top is returned first.
    /// </summary>
    [PublicAPI]
    public List<EntityUid> GetAllMapsAbove(Entity<CEZLevelMapComponent> inputMapUid)
    {
        var result = new List<EntityUid>();
        EnsureNetworkCache();

        if (!_mapToNetwork.TryGetValue(inputMapUid, out var networkUid))
            return result;

        if (!TryComp<CEZLevelsNetworkComponent>(networkUid, out var networkComp))
            return result;

        if (!_sortedMaps.TryGetValue(networkUid, out var sortedMaps) ||
            sortedMaps.Count != CountInitializedNetworkMaps(networkComp))
        {
            _networkCacheDirty = true;
            EnsureNetworkCache();
        }

        if (!_sortedMaps.TryGetValue(networkUid, out sortedMaps))
            return result;

        var inputDepth = inputMapUid.Comp.Depth;
        foreach (var (depth, mapUid) in sortedMaps)
        {
            if (depth > inputDepth)
                result.Add(mapUid);
        }

        return result;
    }

    /// <summary>
    /// Returns a list of all maps below the specified map. The closest map at the bottom is returned first.
    /// </summary>
    [PublicAPI]
    public List<EntityUid> GetAllMapsBelow(Entity<CEZLevelMapComponent> inputMapUid)
    {
        var result = new List<EntityUid>();
        EnsureNetworkCache();

        if (!_mapToNetwork.TryGetValue(inputMapUid, out var networkUid))
            return result;

        if (!TryComp<CEZLevelsNetworkComponent>(networkUid, out var networkComp))
            return result;

        if (!_sortedMaps.TryGetValue(networkUid, out var sortedMaps) ||
            sortedMaps.Count != CountInitializedNetworkMaps(networkComp))
        {
            _networkCacheDirty = true;
            EnsureNetworkCache();
        }

        if (!_sortedMaps.TryGetValue(networkUid, out sortedMaps))
            return result;

        var inputDepth = inputMapUid.Comp.Depth;
        for (var i = sortedMaps.Count - 1; i >= 0; i--)
        {
            var (depth, mapUid) = sortedMaps[i];
            if (depth < inputDepth)
                result.Add(mapUid);
        }

        return result;
    }
}
