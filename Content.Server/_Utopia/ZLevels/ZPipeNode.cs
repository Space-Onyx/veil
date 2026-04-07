using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Utopia.ZLevels.Nodes;

[DataDefinition]
public sealed partial class ZPipeNode : PipeNode
{
    [DataField(required: true)]
    public ZNodeDirection ZDirection;

    // <Onyx-Zlevels>
    public override IEnumerable<Node> GetReachableNodes(TransformComponent xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        MapGridComponent? grid,
        IEntityManager entMan)
    {
        foreach (var node in base.GetReachableNodes(xform, nodeQuery, xformQuery, grid, entMan))
        {
            yield return node;
        }

        if (nodeQuery.TryGetComponent(Owner, out var container))
        {
            foreach (var node in container.Nodes.Values)
            {
                if (node == this)
                    continue;
                if (node is PipeNode sibling && sibling.NodeGroupID == NodeGroupID)
                    yield return sibling;
            }
        }
    }
    // </Onyx-Zlevels>
}

[DataDefinition]
public sealed partial class ZCableNode : CableNode
{
    [DataField(required: true)]
    public ZNodeDirection ZDirection;
}

public enum ZNodeDirection
{
    Up,
    Down
}
