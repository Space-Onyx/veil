using System.Collections.Generic;
using System.Linq;
using Content.Goobstation.Shared.Augments;
using Content.Server.Administration;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Administration;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Console;

namespace Content.Server._Onyx.Surgery.Augments.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class AdminSuppressEnhancementCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ent = default!;

    public string Command => "augmentsuppress";
    public string Description => "Toggles suppression for installed augmentation/cybernetic enhancement on target body like perimeter suppressor.";
    public string Help => "augmentsuppress <bodyUid> [enhancementUid]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (!TryParseEntity(args[0], out var bodyUid))
        {
            shell.WriteError($"Could not find entity with uid '{args[0]}'.");
            return;
        }

        var enhancements = GetEnhancements(bodyUid);
        if (enhancements.Count == 0)
        {
            shell.WriteError("No installed augmentations/cybernetic limbs/modules found in this body.");
            return;
        }

        if (args.Length == 1)
        {
            shell.WriteLine("Installed augmentations/cybernetic enhancements:");
            foreach (var enhancement in enhancements)
            {
                shell.WriteLine($"- {enhancement} | {GetEnhancementKind(enhancement)} | {GetEntityName(enhancement)}");
            }

            shell.WriteLine($"Usage: {Help}");
            return;
        }

        if (!TryParseEntity(args[1], out var enhancementUid))
        {
            shell.WriteError($"Could not find entity with uid '{args[1]}'.");
            return;
        }

        if (!enhancements.Contains(enhancementUid))
        {
            shell.WriteError("Target enhancement is not installed in this body. Run command with only <bodyUid> to see available options.");
            return;
        }

        var suppressSystem = _ent.System<AugmentSuppressionProjectorSystem>();
        if (!suppressSystem.TryToggleAdminSuppression(bodyUid, enhancementUid, out var suppressed, out var error))
        {
            shell.WriteError(error);
            return;
        }

        var state = suppressed ? "suppressed" : "restored";
        shell.WriteLine($"Enhancement {enhancementUid} ({GetEntityName(enhancementUid)}) is now {state}. Target body: {bodyUid}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHint("<bodyUid>");

        if (args.Length == 2)
        {
            if (!TryParseEntity(args[0], out var bodyUid))
                return CompletionResult.Empty;

            var options = GetEnhancements(bodyUid)
                .Select(uid => new CompletionOption(uid.ToString(), $"{GetEnhancementKind(uid)} | {GetEntityName(uid)}"))
                .ToList();

            return CompletionResult.FromHintOptions(options, "<enhancementUid>");
        }

        return CompletionResult.Empty;
    }

    private List<EntityUid> GetEnhancements(EntityUid bodyUid)
    {
        var bodySystem = _ent.System<BodySystem>();
        var itemSlotsSystem = _ent.System<ItemSlotsSystem>();
        var result = new List<EntityUid>();
        var seen = new HashSet<EntityUid>();

        foreach (var uid in AugmentEnhancementHelpers.EnumerateEnhancements(bodyUid, bodySystem, itemSlotsSystem, _ent))
        {
            if (!_ent.EntityExists(uid) || !seen.Add(uid))
                continue;

            result.Add(uid);
        }

        return result;
    }

    private string GetEnhancementKind(EntityUid uid)
    {
        if (_ent.HasComponent<BodyPartComponent>(uid) && _ent.HasComponent<CyberneticsComponent>(uid))
            return "Cyber Limb";

        if (_ent.HasComponent<OrganComponent>(uid) && _ent.HasComponent<CyberneticsComponent>(uid))
            return "Cyber Organ";

        if (_ent.HasComponent<AugmentUniversalModuleComponent>(uid) || _ent.HasComponent<AugmentModuleSlotsComponent>(uid))
            return "Augment Module";

        if (_ent.HasComponent<AugmentComponent>(uid))
            return "Augment";

        return "Enhancement";
    }

    private string GetEntityName(EntityUid uid)
    {
        return _ent.TryGetComponent<MetaDataComponent>(uid, out var meta)
            ? meta.EntityName
            : uid.ToString();
    }

    private bool TryParseEntity(string uid, out EntityUid entity)
    {
        entity = default;
        if (!NetEntity.TryParse(uid, out var net) || !_ent.TryGetEntity(net, out var parsed))
            return false;

        entity = parsed.Value;
        return true;
    }
}



