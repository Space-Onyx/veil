using System.Collections.Generic;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Content.Server._Utopia.ZLevels.Nodes;
using Content.Shared.NodeContainer;
using Content.Shared._Utopia.ZLevels.Cables.Components;
using Robust.Shared.GameObjects;

namespace Content.Server._Utopia.ZLevels.Power;

public sealed class ZCableSystem : EntitySystem
{
    private readonly Dictionary<ZCableNode, HashSet<ZCableNode>> _connections = new();

    // <Onyx-Zlevels>
    public override void Initialize()
    {
        SubscribeLocalEvent<ZCableComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, ZCableComponent comp, ComponentShutdown args)
    {
        if (TryComp(uid, out NodeContainerComponent? container))
            ClearAll(container);
    }
    public IEnumerable<Node> GetZReachable(ZCableNode node)
    {
        if (!_connections.TryGetValue(node, out var set))
            yield break;

        foreach (var other in set)
        {
            if (!EntityManager.Deleted(other.Owner))
                yield return other;
        }
    }
    // </Onyx-Zlevels>

    public void AddZConnection(ZCableNode a, ZCableNode b)
    {
        GetOrAdd(a).Add(b);
        GetOrAdd(b).Add(a);
    }

    public void ClearConnections(ZCableNode node)
    {
        if (!_connections.Remove(node, out var set))
            return;

        foreach (var other in set)
        {
            if (_connections.TryGetValue(other, out var otherSet))
                otherSet.Remove(node);
        }
    }

    public void ClearAll(NodeContainerComponent container)
    {
        foreach (var node in container.Nodes.Values)
        {
            if (node is ZCableNode z)
                ClearConnections(z);
        }
    }

    private HashSet<ZCableNode> GetOrAdd(ZCableNode node)
    {
        if (!_connections.TryGetValue(node, out var set))
        {
            set = new HashSet<ZCableNode>();
            _connections[node] = set;
        }

        return set;
    }
}