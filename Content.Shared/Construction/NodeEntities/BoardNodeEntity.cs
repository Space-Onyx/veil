// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Vera Aguilera Puerto <6766154+Zumorica@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Shared.Construction.NodeEntities;

/// <summary>
///     Works for both <see cref="ComputerBoardComponent"/> and <see cref="MachineBoardComponent"/>
///     because duplicating code just for this is really stinky.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class BoardNodeEntity : IGraphNodeEntity
{
    [DataField("container")] public string Container { get; private set; } = string.Empty;
    [DataField("prototypeSuffix")] public string? PrototypeSuffix { get; private set; } // <Onyx-Tweak>
    [DataField("computer")] public string? LegacyComputerSuffix { get; private set; } // <Onyx-Tweak>

    public string? GetId(EntityUid? uid, EntityUid? userUid, GraphNodeEntityArgs args)
    {
        if (uid == null)
            return null;

        var containerSystem = args.EntityManager.EntitySysManager.GetEntitySystem<SharedContainerSystem>();

        if (!containerSystem.TryGetContainer(uid.Value, Container, out var container)
            || container.ContainedEntities.Count == 0)
            return null;

        var board = container.ContainedEntities[0];

        // There should not be a case where both of these components exist on the same entity...
        if (args.EntityManager.TryGetComponent(board, out MachineBoardComponent? machine))
            return machine.Prototype;

        if (args.EntityManager.TryGetComponent(board, out ComputerBoardComponent? computer))
            return GetComputerPrototype(computer.Prototype); // <Onyx-Tweak edited>

        return null;
    }

    // <Onyx-Tweak>
    private string? GetComputerPrototype(string? prototype)
    {
        if (string.IsNullOrEmpty(prototype))
            return prototype;

        var suffix = !string.IsNullOrWhiteSpace(PrototypeSuffix)
            ? PrototypeSuffix
            : LegacyComputerSuffix;

        if (string.IsNullOrWhiteSpace(suffix))
            return prototype;

        var tabletopPrototype = $"{prototype}{suffix}";
        var prototypeManager = IoCManager.Resolve<IPrototypeManager>();

        return prototypeManager.HasIndex<EntityPrototype>(tabletopPrototype)
            ? tabletopPrototype
            : prototype;
    }
    // </Onyx-Tweak>
}
