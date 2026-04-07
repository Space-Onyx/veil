using System.Linq;
using Content.Server.Administration;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared._Utopia.ZLevels.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._Onyx.ZLevels.Mapping;

[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed class OnyxGridMotionLinkCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly SharedGridMotionLinkSystem _motionLink = default!;

    public override string Command => "gridmotionlink-link";
    public override string Description => "Link a grid to a GridMotionLink group. Usage: gridmotionlink-link <gridNetEntity> <groupId> [autoOffset=true]";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
            {
                var options = new List<CompletionOption>();
                var query = _entities.EntityQueryEnumerator<MapGridComponent, MetaDataComponent>();
                while (query.MoveNext(out var uid, out _, out var meta))
                {
                    options.Add(new CompletionOption(_entities.GetNetEntity(uid).ToString(), meta.EntityName));
                }
                return CompletionResult.FromHintOptions(options, "Grid entity");
            }
            case 2:
            {
                var options = new HashSet<string>();
                var query = _entities.EntityQueryEnumerator<GridMotionLinkComponent>();
                while (query.MoveNext(out _, out var link))
                {
                    if (!string.IsNullOrEmpty(link.GroupId))
                        options.Add(link.GroupId);
                }
                return CompletionResult.FromHintOptions(options.Select(g => new CompletionOption(g)).ToList(), "Group ID");
            }
            case 3:
                return CompletionResult.FromHintOptions(new List<CompletionOption>
                {
                    new("true", "Auto-calculate offset (default)"),
                    new("false", "Keep current/zero offset"),
                }, "Auto offset");
            default:
                return CompletionResult.Empty;
        }
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || args.Length > 3)
        {
            shell.WriteError("Usage: gridmotionlink-link <gridNetEntity> <groupId> [autoOffset=true]");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) ||
            !_entities.TryGetEntity(netEntity, out var uid))
        {
            shell.WriteError($"Invalid entity: {args[0]}");
            return;
        }

        if (!_entities.HasComponent<MapGridComponent>(uid))
        {
            shell.WriteError($"Entity {args[0]} is not a grid.");
            return;
        }

        var groupId = args[1];
        var autoOffset = true;

        if (args.Length >= 3 && bool.TryParse(args[2], out var parsed))
            autoOffset = parsed;

        var link = _entities.EnsureComponent<GridMotionLinkComponent>(uid.Value);
        link.GroupId = groupId;
        link.AutoCalculateOffset = autoOffset;
        _entities.Dirty(uid.Value, link);

        _motionLink.UpdateOffset((uid.Value, link));

        shell.WriteLine($"Grid {args[0]} linked to group '{groupId}'. AutoOffset={autoOffset}, Offset=({link.Offset.X:F2}, {link.Offset.Y:F2}), Root={_entities.GetNetEntity(link.Root)}");
    }
}
