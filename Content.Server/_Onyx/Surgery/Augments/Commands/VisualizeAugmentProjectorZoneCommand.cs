using System;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Onyx.Surgery.Augments.Commands;

[AdminCommand(AdminFlags.Debug)]
public sealed class VisualizeAugmentProjectorZoneCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _systems = default!;

    public string Command => "showaugsuppresszone";
    public string Description => "Toggles in-game visualization for all augment suppression zones.";
    public string Help => "showaugsuppresszone | showaugsuppresszone off";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError("You must be a player to use this command.");
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteLine($"Usage: {Help}");
            return;
        }

        var system = _systems.GetEntitySystem<AugmentSuppressionProjectorSystem>();
        if (IsOffCommand(args))
        {
            var disabled = system.DisableVisualization(player);
            shell.WriteLine(disabled
                ? "Augment zone visualization disabled."
                : "Augment zone visualization was not enabled.");
            return;
        }

        var enabled = system.ToggleVisualization(player);
        shell.WriteLine(enabled
            ? "Augment zone visualization enabled for all projectors."
            : "Augment zone visualization disabled.");
    }

    private static bool IsOffCommand(string[] args)
    {
        return args.Length == 1 && args[0].Equals("off", StringComparison.OrdinalIgnoreCase);
    }
}

