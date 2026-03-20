/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System;
using Content.Server._CE.PVS;
using Content.Shared._CE.ZLevels.Core.Components;
using JetBrains.Annotations;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    private static readonly TimeSpan PostInitFallSuppressionDuration = TimeSpan.FromSeconds(2.0); // <Onyx-Tweak>

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

        if (TryGetZNetwork(mapUid, out _)) // <Onyx-Tweak Edited>
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

        InvalidateNetworkCache(); // <Onyx-Tweak>
        RaiseLocalEvent(network, new CEZLevelNetworkUpdatedEvent());
        // Backward compatibility for older CE mapping systems still listening to the legacy event.
        RaiseLocalEvent(network, new CEMapAddedIntoZNetworkEvent());

        return success;
    }

    // <Onyx-Tweak>
    public void MarkMapPostInitFallSuppression(EntityUid mapUid)
    {
        if (!TryComp<CEZLevelMapComponent>(mapUid, out var zMap))
            return;

        zMap.SuppressFallsUntil = _timing.CurTime + PostInitFallSuppressionDuration;
    }

    public void StabilizeZPhysicsAfterMapInit(HashSet<EntityUid> initializedMaps)
    {
        if (initializedMaps.Count == 0)
            return;

        foreach (var mapUid in initializedMaps)
        {
            MarkMapPostInitFallSuppression(mapUid);
        }

        var query = EntityQueryEnumerator<CEZPhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var zPhys, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !initializedMaps.Contains(mapUid))
                continue;

            if (Math.Abs(zPhys.Velocity) > 0.001f)
            {
                zPhys.Velocity = 0f;
                DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.Velocity));
            }
            zPhys.GroundCacheValid = false;

            RemComp<CEActiveZPhysicsComponent>(uid);
        }
    }
    // <Onyx-Tweak>
}

/// <summary>
/// Called on ZLevel Network Entity, when maps added or removed from network
/// </summary>
public sealed class CEZLevelNetworkUpdatedEvent : EntityEventArgs;

/// <summary>
/// Legacy compatibility event for older code that expects map-add notifications by this name.
/// </summary>
public sealed class CEMapAddedIntoZNetworkEvent : EntityEventArgs;
