// SPDX-FileCopyrightText: 2019 Pieter-Jan Briers <pieterjan.briers@gmail.com>
// SPDX-FileCopyrightText: 2020 ComicIronic <comicironic@gmail.com>
// SPDX-FileCopyrightText: 2020 Pieter-Jan Briers <pieterjan.briers+git@gmail.com>
// SPDX-FileCopyrightText: 2020 Víctor Aguilera Puerto <6766154+Zumorica@users.noreply.github.com>
// SPDX-FileCopyrightText: 2020 chairbender <kwhipke1@gmail.com>
// SPDX-FileCopyrightText: 2021 Acruid <shatter66@gmail.com>
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto <6766154+Zumorica@users.noreply.github.com>
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto <gradientvera@outlook.com>
// SPDX-FileCopyrightText: 2022 Chris <HoofedEar@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Rane <60792108+Elijahrane@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2023 Chief-Engineer <119664036+Chief-Engineer@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.Research.Systems
{
    [UsedImplicitly]
    public sealed partial class ResearchSystem : SharedResearchSystem
    {
        [Dependency] private readonly IAdminLogManager _adminLog = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly AccessReaderSystem _accessReader = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
        [Dependency] private readonly SharedPopupSystem _popup = default!;
        [Dependency] private readonly RadioSystem _radio = default!;

        public override void Initialize()
        {
            base.Initialize();
            InitializeClient();
            InitializeConsole();
            InitializeSource();
            InitializeServer();

            SubscribeLocalEvent<TechnologyDatabaseComponent, ResearchRegistrationChangedEvent>(OnDatabaseRegistrationChanged);
        }

        /// <summary>
        /// Gets a server based on its unique numeric id.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="id"></param>
        /// <param name="serverUid"></param>
        /// <param name="serverComponent"></param>
        /// <returns></returns>
        public bool TryGetServerById(EntityUid client, int id, [NotNullWhen(true)] out EntityUid? serverUid, [NotNullWhen(true)] out ResearchServerComponent? serverComponent)
        {
            serverUid = null;
            serverComponent = null;

            var query = GetServers(client);
            foreach (var (uid, server) in query)
            {
                if (server.Id != id)
                    continue;
                serverUid = uid;
                serverComponent = server;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the names of all the servers.
        /// </summary>
        /// <returns></returns>
        // <Onyx-ZLevels Edited>
        public string[] GetServerNames(EntityUid client)
        {
            var sourceDepth = GetEntityMapDepth(client);
            return GetServers(client)
                .OrderBy(x => x.Comp.Id)
                .Select(x =>
                {
                    var serverDepth = GetEntityMapDepth(x);
                    if (sourceDepth == null || serverDepth == null || sourceDepth.Value == serverDepth.Value)
                        return x.Comp.ServerName;

                    var relativeFloor = serverDepth.Value - sourceDepth.Value;
                    return $"{x.Comp.ServerName} ({(relativeFloor > 0 ? $"+{relativeFloor}" : relativeFloor.ToString())})";
                })
                .ToArray();
        }
        // </Onyx-ZLevels Edited>

        /// <summary>
        /// Gets the ids of all the servers
        /// </summary>
        /// <returns></returns>
        // <Onyx-ZLevels Edited>
        public int[] GetServerIds(EntityUid client)
        {
            return GetServers(client)
                .OrderBy(x => x.Comp.Id)
                .Select(x => x.Comp.Id)
                .ToArray();
        }
        // </Onyx-ZLevels Edited>
        
        public HashSet<Entity<ResearchServerComponent>> GetServers(EntityUid client)
        {
            var clientXform = Transform(client);
            if (clientXform.GridUid is not { } grid)
                return [];

            // <Onyx-ZLevels Edited>
            var linkedGrids = GetLinkedGridFloors(grid);
            var set = new HashSet<Entity<ResearchServerComponent>>();
            var query = EntityQueryEnumerator<ResearchServerComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var server, out var xform))
            {
                if (xform.GridUid is not { } serverGrid)
                    continue;

                if (!linkedGrids.Contains(serverGrid))
                    continue;

                set.Add((uid, server));
            }
            // </Onyx-ZLevels Edited>
            return set;
        }

        // <Onyx-ZLevels>
        private HashSet<EntityUid> GetLinkedGridFloors(EntityUid sourceGrid)
        {
            var result = new HashSet<EntityUid> { sourceGrid };
            if (!TryComp<GridMotionLinkComponent>(sourceGrid, out var sourceMotion) ||
                string.IsNullOrWhiteSpace(sourceMotion.GroupId))
            {
                return result;
            }

            var query = EntityQueryEnumerator<GridMotionLinkComponent>();
            while (query.MoveNext(out var uid, out var motion))
            {
                if (motion.GroupId == sourceMotion.GroupId)
                    result.Add(uid);
            }

            return result;
        }

        private int? GetEntityMapDepth(EntityUid uid)
        {
            if (!TryComp<TransformComponent>(uid, out var xform) || xform.MapUid is not { } mapUid)
                return null;

            if (!TryComp<CEZLevelMapComponent>(mapUid, out var zMap))
                return null;

            return zMap.Depth;
        }
        // </Onyx-ZLevels>

        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<ResearchServerComponent>();
            while (query.MoveNext(out var uid, out var server))
            {
                if (server.NextUpdateTime > _timing.CurTime)
                    continue;
                server.NextUpdateTime = _timing.CurTime + server.ResearchConsoleUpdateTime;

                UpdateServer(uid, (int) server.ResearchConsoleUpdateTime.TotalSeconds, server);
            }
        }
    }
}