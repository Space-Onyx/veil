using System.Globalization;
using System.Numerics;
using Content.Server.Administration;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._Onyx.ZLevels.Mapping;

[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed class OnyxGridMotionLinkOffsetCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override string Command => "gridmotionlink-offset";
    public override string Description => "Set offset for a grid and reposition it relative to a root grid. Usage: gridmotionlink-offset <gridNetEntity> <rootNetEntity> <offsetX> <offsetY>";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
            case 2:
            {
                var options = new List<CompletionOption>();
                var query = _entities.EntityQueryEnumerator<MapGridComponent, MetaDataComponent>();
                while (query.MoveNext(out var uid, out _, out var meta))
                {
                    options.Add(new CompletionOption(_entities.GetNetEntity(uid).ToString(), meta.EntityName));
                }
                var hint = args.Length == 1 ? "Grid to move" : "Root grid (reference point)";
                return CompletionResult.FromHintOptions(options, hint);
            }
            case 3:
                return CompletionResult.FromHint("offsetX (float)");
            case 4:
                return CompletionResult.FromHint("offsetY (float)");
            default:
                return CompletionResult.Empty;
        }
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 4)
        {
            shell.WriteError("Usage: gridmotionlink-offset <gridNetEntity> <rootNetEntity> <offsetX> <offsetY>");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) ||
            !_entities.TryGetEntity(netEntity, out var uid))
        {
            shell.WriteError($"Invalid grid entity: {args[0]}");
            return;
        }

        if (!NetEntity.TryParse(args[1], out var rootNetEntity) ||
            !_entities.TryGetEntity(rootNetEntity, out var rootUid))
        {
            shell.WriteError($"Invalid root entity: {args[1]}");
            return;
        }

        if (!float.TryParse(args[2], CultureInfo.InvariantCulture, out var offsetX) ||
            !float.TryParse(args[3], CultureInfo.InvariantCulture, out var offsetY))
        {
            shell.WriteError("Invalid offset values. Expected floats.");
            return;
        }

        var offset = new Vector2(offsetX, offsetY);

        if (_entities.TryGetComponent<GridMotionLinkComponent>(uid, out var link))
        {
            link.AutoCalculateOffset = false;
            link.Offset = offset;
            link.Root = rootUid.Value;
            _entities.Dirty(uid.Value, link);
        }

        var rootPos = _transform.GetWorldPosition(rootUid.Value);
        var rootRot = _transform.GetWorldRotation(rootUid.Value);
        var q = new Quaternion2D(rootRot);
        var newPos = rootPos + Quaternion2D.RotateVector(q, offset);

        _transform.SetWorldPosition(uid.Value, newPos);
        _transform.SetWorldRotation(uid.Value, rootRot);

        shell.WriteLine($"Grid {args[0]} moved to ({newPos.X:F2}, {newPos.Y:F2}) with offset ({offsetX:F2}, {offsetY:F2}) relative to root {args[1]}.");
    }
}
