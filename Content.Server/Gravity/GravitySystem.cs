// SPDX-FileCopyrightText: 2022 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 20kdc <asdd2808@gmail.com>
// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Tayrtahn <tayrtahn@gmail.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Gravity;
using Content.Shared._Utopia.ZLevels.Components;
using JetBrains.Annotations;
using Robust.Shared.Map.Components;

namespace Content.Server.Gravity
{
    [UsedImplicitly]
    public sealed class GravitySystem : SharedGravitySystem
    {
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<GravityComponent, ComponentInit>(OnGravityInit);
        }

        /// <summary>
        /// Iterates gravity components and checks if this entity can have gravity applied.
        /// </summary>
        public void RefreshGravity(EntityUid uid, GravityComponent? gravity = null)
        {
            if (!Resolve(uid, ref gravity))
                return;

            if (gravity.Inherent)
                return;

            var enabled = HasActiveGeneratorOnLinkedGrids(uid); // <Onyx-Tweak Edited>

            if (enabled != gravity.Enabled)
            {
                gravity.Enabled = enabled;
                var ev = new GravityChangedEvent(uid, enabled);
                RaiseLocalEvent(uid, ref ev, true);
                Dirty(uid, gravity);

                if (HasComp<MapGridComponent>(uid))
                {
                    StartGridShake(uid);
                }
            }
        }

        private void OnGravityInit(EntityUid uid, GravityComponent component, ComponentInit args)
        {
            RefreshGravity(uid);
        }

        /// <summary>
        /// Enables gravity. Note that this is a fast-path for GravityGeneratorSystem.
        /// This means it does nothing if Inherent is set and it might be wiped away with a refresh
        ///  if you're not supposed to be doing whatever you're doing.
        /// </summary>
        public void EnableGravity(EntityUid uid, GravityComponent? gravity = null)
        {
            if (!Resolve(uid, ref gravity))
                return;

            if (gravity.Enabled || gravity.Inherent)
                return;

            gravity.Enabled = true;
            var ev = new GravityChangedEvent(uid, true);
            RaiseLocalEvent(uid, ref ev, true);
            Dirty(uid, gravity);

            if (HasComp<MapGridComponent>(uid))
            {
                StartGridShake(uid);
            }
        }

        // <Onyx-Tweak>
        private bool HasActiveGeneratorOnLinkedGrids(EntityUid gridUid)
        {
            foreach (var (comp, xform) in EntityQuery<GravityGeneratorComponent, TransformComponent>(true))
            {
                if (!comp.GravityActive || xform.ParentUid != gridUid)
                    continue;

                return true;
            }

            if (!TryComp<GridMotionLinkComponent>(gridUid, out var link) || string.IsNullOrEmpty(link.GroupId))
                return false;

            var query = EntityQueryEnumerator<GridMotionLinkComponent, MapGridComponent>();
            while (query.MoveNext(out var otherGridUid, out var otherLink, out _))
            {
                if (otherGridUid == gridUid || otherLink.GroupId != link.GroupId)
                    continue;

                foreach (var (comp, xform) in EntityQuery<GravityGeneratorComponent, TransformComponent>(true))
                {
                    if (!comp.GravityActive || xform.ParentUid != otherGridUid)
                        continue;

                    return true;
                }
            }

            return false;
        }
        public void RefreshLinkedGravity(EntityUid gridUid)
        {
            RefreshGravity(gridUid);

            if (!TryComp<GridMotionLinkComponent>(gridUid, out var link) || string.IsNullOrEmpty(link.GroupId))
                return;

            var query = EntityQueryEnumerator<GridMotionLinkComponent, GravityComponent>();
            while (query.MoveNext(out var otherUid, out var otherLink, out var otherGravity))
            {
                if (otherUid == gridUid || otherLink.GroupId != link.GroupId)
                    continue;

                RefreshGravity(otherUid, otherGravity);
            }
        }
        // </Onyx-Tweak>
    }
}
