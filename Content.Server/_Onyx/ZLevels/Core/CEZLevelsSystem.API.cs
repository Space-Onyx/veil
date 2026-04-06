/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._Onyx.PVS;
using Content.Shared._Onyx.ZLevels.Core.Components;
using JetBrains.Annotations;

namespace Content.Server._Onyx.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>
    /// Creates a new entity zLevelNetwork
    /// </summary>
    [PublicAPI]
    public Entity<CEZLevelsNetworkComponent> CreateZNetwork()
    {
        var ent = Spawn();

        var zLevel = EnsureComp<CEZLevelsNetworkComponent>(ent);
        EnsureComp<CEPvsOverrideComponent>(ent);

        return (ent, zLevel);
    }

    /// <summary>
    /// Attempts to add the specified map to the zNetwork network at the specified depth
    /// </summary>
    private bool TryAddMapIntoZNetwork(Entity<CEZLevelsNetworkComponent> network, EntityUid mapUid, int depth)
    {
        if (network.Comp.ZLevels.ContainsKey(depth))
        {
            Log.Error($"Failed to add map {mapUid} to ZLevelNetwork {network}: This depth is already occupied.");
            return false;
        }

        if (TryGetZNetwork(mapUid, out _))
        {
            Log.Error($"Failed attempt to add map {mapUid} to ZLevelNetwork {network} at depth {depth}: This map is already in this network.");
            return false;
        }

        network.Comp.ZLevels.Add(depth, mapUid);
        EnsureComp<CEZLevelMapComponent>(mapUid).Depth = depth;

        return true;
    }

    public bool TryAddMapsIntoZNetwork(Entity<CEZLevelsNetworkComponent> network, Dictionary<EntityUid, int> maps)
    {
        var success = true;
        foreach (var (ent, depth) in maps)
        {
            if (!TryAddMapIntoZNetwork(network, ent, depth))
                success = false;
        }

        InvalidateNetworkCache();
        RaiseLocalEvent(network, new CEZLevelNetworkUpdatedEvent());
        // Backward compatibility for older CE mapping systems still listening to the legacy event.
        RaiseLocalEvent(network, new CEMapAddedIntoZNetworkEvent());

        return success;
    }

    private static readonly TimeSpan PostInitFallSuppressionDuration = TimeSpan.FromSeconds(2.0);

    public void StabilizeZPhysicsAfterMapInit(HashSet<EntityUid> initializedMaps)
    {
        if (initializedMaps.Count == 0)
            return;

        foreach (var mapUid in initializedMaps)
        {
            if (!TryComp<CEZLevelMapComponent>(mapUid, out var zMap))
                continue;

            zMap.SuppressFallsUntil = _timing.CurTime + PostInitFallSuppressionDuration;
        }

        var query = EntityQueryEnumerator<CEZPhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var zPhys, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !initializedMaps.Contains(mapUid))
                continue;

            if (Math.Abs(zPhys.Velocity) > 0.001f)
            {
                zPhys.Velocity = 0f;
                Dirty(uid, zPhys);
            }

            if (Math.Abs(zPhys.LocalPosition) > 0.001f)
            {
                zPhys.LocalPosition = 0f;
                Dirty(uid, zPhys);
            }

            RemCompDeferred<CEActiveZPhysicsComponent>(uid);
        }
    }
}

/// <summary>
/// Called on ZLevel Network Entity, when maps added or removed from network
/// </summary>
public sealed class CEZLevelNetworkUpdatedEvent : EntityEventArgs;

/// <summary>
/// Legacy compatibility event for older code that expects map-add notifications by this name.
/// </summary>
public sealed class CEMapAddedIntoZNetworkEvent : EntityEventArgs;
