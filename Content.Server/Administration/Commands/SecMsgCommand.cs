using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Popups;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class SecMsgCommand : LocalizedCommands
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private static readonly string[] PopupTypeOptions = Enum.GetNames<PopupType>();

    public override string Command => "secmsg";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHint(Loc.GetString("secmsg-command-arg-message"));
        }

        if (args.Length == 2)
        {
            var options = PopupTypeOptions.Concat(new[] { "All" }).Concat(_playerManager.Sessions.Select(s => s.Name));
            return CompletionResult.FromHintOptions(options, Loc.GetString("secmsg-command-arg-target-or-type"));
        }

        if (args.Length >= 3)
        {
            var options = _playerManager.Sessions.Select(s => s.Name);
            return CompletionResult.FromHintOptions(options, Loc.GetString("secmsg-command-arg-target-n", ("target", args.Length - 2)));
        }

        return CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Loc.GetString("secmsg-command-error-args"));
            return;
        }

        var message = args[0];

        if (string.IsNullOrEmpty(message))
        {
            shell.WriteLine(Loc.GetString("secmsg-command-error-empty-message"));
            return;
        }
        var popupType = PopupType.Large;
        var targetStartIndex = 2;
        var isFirstArgPopupType = Enum.TryParse<PopupType>(args[1], true, out var parsedType);

        if (isFirstArgPopupType)
        {
            popupType = parsedType;
            targetStartIndex = 2;
        }
        else
        {
            targetStartIndex = 1;
        }

        var targets = new List<ICommonSession>();

        var firstArgIsAll = isFirstArgPopupType
            ? args.Length > 2 && args[2].Equals("All", StringComparison.OrdinalIgnoreCase)
            : args[1].Equals("All", StringComparison.OrdinalIgnoreCase);

        if (firstArgIsAll)
        {
            if (args.Length > targetStartIndex + 1)
            {
                shell.WriteLine(Loc.GetString("secmsg-command-error-all-with-extra"));
                return;
            }
            targets.AddRange(_playerManager.Sessions);
        }
        else
        {
            if (args.Length > 22)
            {
                shell.WriteLine(Loc.GetString("secmsg-command-error-too-many-targets"));
                return;
            }

            for (var i = targetStartIndex; i < args.Length; i++)
            {
                var username = args[i];
                if (!_playerManager.TryGetSessionByUsername(username, out var session))
                {
                    shell.WriteError(Loc.GetString("secmsg-command-error-player-not-found", ("username", username)));
                    continue;
                }
                targets.Add(session);
            }

            if (targets.Count == 0)
            {
                shell.WriteLine(Loc.GetString("secmsg-command-error-no-valid-targets"));
                return;
            }
        }

        var senderName = shell.Player?.Name ?? "An administrator";

        foreach (var target in targets)
        {
            if (target.AttachedEntity != null)
            {
                _entityManager.System<PopupSystem>().PopupEntity(message, target.AttachedEntity.Value, target, popupType);
            }
        }

        var firstTargetArg = firstArgIsAll
            ? (isFirstArgPopupType ? args[2] : args[1])
            : (isFirstArgPopupType ? args[2] : args[1]);

        var targetNames = firstTargetArg.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? "all players"
            : string.Join(", ", targets.Select(t => t.Name));

        _adminLogger.Add(LogType.AdminMessage, LogImpact.Low, $"{senderName} sent security message to {targetNames}: {message}");

        shell.WriteLine(Loc.GetString("secmsg-command-success", ("count", targets.Count)));
    }
}
