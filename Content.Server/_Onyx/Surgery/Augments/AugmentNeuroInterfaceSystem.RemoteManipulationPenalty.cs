using System;
using System.Collections.Generic;
using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed partial class AugmentNeuroInterfaceSystem
{
    private sealed class RemoteManipulationPenaltyEntry
    {
        public float NeuroLoad;
        public float PowerDraw;
        public TimeSpan ExpiresAt;
    }

    private readonly Dictionary<EntityUid, List<RemoteManipulationPenaltyEntry>> _remoteManipulationPenalties = new();

    private void OnGetRemoteManipulationPenaltyPowerDraw(Entity<AugmentNeuroInterfaceComponent> ent, ref GetAugmentsPowerDrawEvent args)
    {
        if (_augment.GetBody(ent) is not { } body || body != args.Body)
            return;

        args.TotalDraw += GetRemoteManipulationPenaltyPowerDraw(body, _timing.CurTime);
    }

    private void ApplyForeignInterfaceManipulationPenalty(EntityUid actorBody)
    {
        if (!TryGetActiveInterceptPenalty(actorBody, out var neuroLoad, out var powerDraw, out var duration))
            return;

        if (duration <= TimeSpan.Zero || (neuroLoad <= 0f && powerDraw <= 0f))
            return;

        if (!_remoteManipulationPenalties.TryGetValue(actorBody, out var entries))
        {
            entries = new List<RemoteManipulationPenaltyEntry>();
            _remoteManipulationPenalties[actorBody] = entries;
        }

        entries.Add(new RemoteManipulationPenaltyEntry
        {
            NeuroLoad = neuroLoad,
            PowerDraw = powerDraw,
            ExpiresAt = _timing.CurTime + duration
        });

        UpdatePowerDraw(actorBody);

        if (TryGetBodyNeuroInterface(actorBody, out var neuroInterface))
            UpdateUi(neuroInterface, actorBody);
    }

    private float GetRemoteManipulationPenaltyNeuroLoad(EntityUid body, TimeSpan now)
    {
        if (!_remoteManipulationPenalties.TryGetValue(body, out var entries))
            return 0f;

        var total = 0f;
        foreach (var entry in entries)
        {
            if (entry.ExpiresAt > now)
                total += entry.NeuroLoad;
        }

        return total;
    }

    private float GetRemoteManipulationPenaltyPowerDraw(EntityUid body, TimeSpan now)
    {
        if (!_remoteManipulationPenalties.TryGetValue(body, out var entries))
            return 0f;

        var total = 0f;
        foreach (var entry in entries)
        {
            if (entry.ExpiresAt > now)
                total += entry.PowerDraw;
        }

        return total;
    }

    private void PruneRemoteManipulationPenalties(TimeSpan now)
    {
        if (_remoteManipulationPenalties.Count == 0)
            return;

        List<EntityUid>? bodiesToClear = null;

        foreach (var (body, entries) in _remoteManipulationPenalties)
        {
            if (Deleted(body))
            {
                bodiesToClear ??= new List<EntityUid>();
                bodiesToClear.Add(body);
                continue;
            }

            var hadActive = false;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].ExpiresAt > now)
                {
                    hadActive = true;
                    break;
                }
            }

            entries.RemoveAll(entry => entry.ExpiresAt <= now);

            if (entries.Count == 0)
            {
                bodiesToClear ??= new List<EntityUid>();
                bodiesToClear.Add(body);

                if (hadActive)
                {
                    UpdatePowerDraw(body);
                    if (TryGetBodyNeuroInterface(body, out var neuroInterface))
                        UpdateUi(neuroInterface, body);
                }
            }
        }

        if (bodiesToClear == null)
            return;

        foreach (var body in bodiesToClear)
        {
            _remoteManipulationPenalties.Remove(body);
        }
    }

    private bool TryGetActiveInterceptPenalty(EntityUid body, out float neuroLoad, out float powerDraw, out TimeSpan duration)
    {
        neuroLoad = 0f;
        powerDraw = 0f;
        duration = TimeSpan.Zero;

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentInterceptComponent>(organUid, out var intercept))
                    continue;

                if (!IsOrganActive(organUid, organComp))
                    continue;

                neuroLoad = intercept.ForeignManipulationNeuroLoad;
                powerDraw = intercept.ForeignManipulationPowerDraw;
                duration = TimeSpan.FromSeconds(Math.Max(0f, intercept.ForeignManipulationDurationSeconds));
                return true;
            }
        }

        return false;
    }

    private bool TryGetBodyNeuroInterface(EntityUid body, out Entity<AugmentNeuroInterfaceComponent> neuroInterface)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentNeuroInterfaceComponent>(organUid, out var neuroComp))
                    continue;

                neuroInterface = (organUid, neuroComp);
                return true;
            }
        }

        neuroInterface = default;
        return false;
    }
}
