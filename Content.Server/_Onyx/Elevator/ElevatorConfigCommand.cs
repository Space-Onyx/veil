using System.Collections.Generic;
using Content.Server._Onyx.Elevator;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Mapping)]
public sealed class ElevatorConfigCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly EuiManager _euiManager = default!;

    public string Command => "elevatorconfig";
    public string Description => Loc.GetString("elevator-config-desc");
    public string Help => Loc.GetString("elevator-config-help", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("elevator-config-not-player"));
            return;
        }

        if (!int.TryParse(args[0], out var netId))
        {
            shell.WriteError(Loc.GetString("elevator-config-invalid-netid"));
            return;
        }

        var netEntity = new NetEntity(netId);
        if (!_entityManager.TryGetEntity(netEntity, out var target))
        {
            shell.WriteError(Loc.GetString("elevator-config-invalid-entity"));
            return;
        }

        if (!HasConfigurableComponent(target.Value))
        {
            shell.WriteError(Loc.GetString("elevator-config-no-components"));
            return;
        }

        var eui = new ElevatorConfigEui(target.Value);
        _euiManager.OpenEui(eui, player);
    }

    private bool HasConfigurableComponent(EntityUid uid)
    {
        return _entityManager.HasComponent<ElevatorComponent>(uid) ||
               _entityManager.HasComponent<ElevatorButtonComponent>(uid) ||
               _entityManager.HasComponent<ElevatorDoorComponent>(uid) ||
               _entityManager.HasComponent<ElevatorPointComponent>(uid);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = GetEntityCompletions(args[0]);
        return CompletionResult.FromHintOptions(options, Loc.GetString("elevator-config-entity-hint"));
    }

    private List<CompletionOption> GetEntityCompletions(string text)
    {
        var options = new List<CompletionOption>();

        if (text.Length > 0 && !NetEntity.TryParse(text, out _))
            return options;

        var query = _entityManager.AllEntityQueryEnumerator<MetaDataComponent>();
        while (options.Count < 64 && query.MoveNext(out var uid, out var metadata))
        {
            var tags = GetEntityTags(uid);
            if (tags.Count == 0)
                continue;

            var netId = metadata.NetEntity.ToString();
            if (!netId.StartsWith(text))
                continue;

            var hint = Loc.GetString(
                "elevator-config-completion-entry",
                ("types", string.Join(", ", tags)),
                ("name", metadata.EntityName));

            options.Add(new CompletionOption(netId, hint));
        }

        options.Sort();
        return options;
    }

    private List<string> GetEntityTags(EntityUid uid)
    {
        var tags = new List<string>();

        if (_entityManager.HasComponent<ElevatorComponent>(uid))
            tags.Add(Loc.GetString("elevator-config-type-elevator"));

        if (_entityManager.TryGetComponent<ElevatorButtonComponent>(uid, out var buttonComp))
        {
            tags.Add(buttonComp.ButtonType switch
            {
                ElevatorButtonType.CallButton => Loc.GetString("elevator-config-type-call-button"),
                ElevatorButtonType.SendElevatorUp => Loc.GetString("elevator-config-type-up-button"),
                ElevatorButtonType.SendElevatorDown => Loc.GetString("elevator-config-type-down-button"),
                _ => Loc.GetString("elevator-config-type-button")
            });
        }

        if (_entityManager.HasComponent<ElevatorDoorComponent>(uid))
            tags.Add(Loc.GetString("elevator-config-type-door"));

        if (_entityManager.HasComponent<ElevatorPointComponent>(uid))
            tags.Add(Loc.GetString("elevator-config-type-point"));

        return tags;
    }
}
