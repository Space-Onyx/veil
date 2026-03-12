using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Onyx.Surgery.Augments.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class AdminOpenNeuroInterfaceCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ent = default!;

    public string Command => "neuroopen";
    public string Description => "Opens installed neuro-interface UI of a target body for the executing admin.";
    public string Help => "neuroopen <bodyUid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (!TryGetActor(shell, out var actor))
        {
            shell.WriteError("This command can only be used by an in-game admin.");
            return;
        }

        if (!TryParseEntity(args[0], out var body))
        {
            shell.WriteError($"Could not find entity with uid '{args[0]}'.");
            return;
        }

        var neuroSystem = _ent.System<AugmentNeuroInterfaceSystem>();
        if (!neuroSystem.TryOpenInterfaceForRemoteController(body, actor, out var error))
        {
            shell.WriteError(error);
            return;
        }

        shell.WriteLine($"Opened neuro-interface for body {body}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHint("<bodyUid>")
            : CompletionResult.Empty;
    }

    private bool TryParseEntity(string uid, out EntityUid entity)
    {
        entity = default;
        if (!NetEntity.TryParse(uid, out var net) || !_ent.TryGetEntity(net, out var parsed))
            return false;

        entity = parsed.Value;
        return true;
    }

    private static bool TryGetActor(IConsoleShell shell, out EntityUid actor)
    {
        if (shell.Player?.AttachedEntity is { Valid: true } attached)
        {
            actor = attached;
            return true;
        }

        actor = default;
        return false;
    }
}

