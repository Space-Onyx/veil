using Content.Server._Onyx.ProxyControl.Systems;
using Content.Server.Administration;
using Content.Shared._Onyx.ProxyControl;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Onyx.ProxyControl.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class ProxyControlLinkCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "proxycontrol_link";
    public string Description => "Relays a controller entity's camera/input/actions to a target entity.";
    public string Help => $"{Command} <controller entity> <target entity> [camera=true] [movement=false] [interaction=false] [hands=true] [inventory=true] [actions=true] [speech=true]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || args.Length > 9)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!TryParseEntity(args[0], out var controller) ||
            !TryParseEntity(args[1], out var target))
        {
            shell.WriteLine("Invalid entity uid.");
            return;
        }

        if (!TryParseBoolArg(shell, args, 2, true, out var relayCamera) ||
            !TryParseBoolArg(shell, args, 3, false, out var relayMovement) ||
            !TryParseBoolArg(shell, args, 4, false, out var relayInteraction) ||
            !TryParseBoolArg(shell, args, 5, true, out var relayHands) ||
            !TryParseBoolArg(shell, args, 6, true, out var relayInventory) ||
            !TryParseBoolArg(shell, args, 7, true, out var relayActions) ||
            !TryParseBoolArg(shell, args, 8, true, out var relaySpeech))
        {
            return;
        }

        if (!_entities.System<ProxyControlSystem>().Link(
                controller,
                target,
                relayCamera,
                relayMovement,
                relayInteraction,
                relayHands,
                relayInventory,
                relayActions,
                relaySpeech))
        {
            shell.WriteLine("Failed to link proxy controller.");
            return;
        }

        shell.WriteLine($"Linked proxy controller {controller} to target {target}.");
    }

    private bool TryParseEntity(string value, out EntityUid uid)
    {
        uid = default;
        if (!NetEntity.TryParse(value, out var netEntity) ||
            !_entities.TryGetEntity(netEntity, out var parsed) ||
            parsed is not { } entity)
            return false;

        uid = entity;
        return true;
    }

    private static bool TryParseBoolArg(IConsoleShell shell, string[] args, int index, bool fallback, out bool value)
    {
        value = fallback;
        if (args.Length <= index)
            return true;

        if (bool.TryParse(args[index], out value))
            return true;

        shell.WriteLine($"Invalid boolean value: {args[index]}.");
        return false;
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ProxyControlUnlinkCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "proxycontrol_unlink";
    public string Description => "Stops relaying a proxy controller entity.";
    public string Help => $"{Command} <controller entity>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEntity) ||
            !_entities.TryGetEntity(netEntity, out var controller) ||
            controller is not { } controllerUid ||
            !_entities.TryGetComponent<ProxyControlComponent>(controllerUid, out var proxy))
        {
            shell.WriteLine("Invalid proxy controller entity.");
            return;
        }

        _entities.System<ProxyControlSystem>().Unlink((controllerUid, proxy));
        shell.WriteLine($"Unlinked proxy controller {controllerUid}.");
    }
}
