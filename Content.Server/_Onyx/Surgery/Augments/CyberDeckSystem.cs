using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class CyberDeckSystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private CyberDeckScriptSystem _scriptSystem = default!;
    private const float MinRegenTime = 0.01f;

    private readonly HashSet<EntityUid> _regeneratingDecks = new();
    private readonly List<EntityUid> _regeneratingDeckBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        _scriptSystem = EntityManager.System<CyberDeckScriptSystem>();

        SubscribeLocalEvent<CyberDeckComponent, AugmentModuleInsertedEvent>(OnModuleInserted);
        SubscribeLocalEvent<CyberDeckComponent, AugmentModuleRemovedEvent>(OnModuleRemoved);
        SubscribeLocalEvent<CyberDeckComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CyberDeckComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<CyberDeckComponent> ent, ref ComponentStartup args)
    {
        RecalculateRam(ent);
        UpdateRegenTracking(ent);
    }

    private void OnShutdown(Entity<CyberDeckComponent> ent, ref ComponentShutdown args)
    {
        _regeneratingDecks.Remove(ent.Owner);
    }

    private void OnModuleInserted(Entity<CyberDeckComponent> ent, ref AugmentModuleInsertedEvent args)
    {
        RecalculateRam(ent);

        if (TryComp<CyberDeckScriptComponent>(args.Module, out var script))
        {
            var body = _augment.GetBody(ent);
            if (body != null)
                _scriptSystem.OnScriptInserted(args.Module, script, body.Value);
        }
    }

    private void OnModuleRemoved(Entity<CyberDeckComponent> ent, ref AugmentModuleRemovedEvent args)
    {
        RecalculateRam(ent);

        if (TryComp<CyberDeckScriptComponent>(args.Module, out var script))
        {
            var body = _augment.GetBody(ent);
            _scriptSystem.OnScriptRemoved(args.Module, script, body);
        }
    }

    private void RecalculateRam(Entity<CyberDeckComponent> ent)
    {
        var comp = ent.Comp;
        var oldMax = comp.MaxRam;
        var newMax = comp.BaseMaxRam;

        if (TryComp<AugmentModuleSlotsComponent>(ent, out var slots) &&
            TryComp<ItemSlotsComponent>(ent, out var itemSlotsComp))
        {
            foreach (var def in slots.Slots)
            {
                if (!_itemSlots.TryGetSlot(ent, def.Id, out var slot, itemSlotsComp))
                    continue;

                if (slot.Item is not { } moduleUid)
                    continue;

                if (TryComp<CyberDeckRamModuleComponent>(moduleUid, out var ramModule))
                    newMax += ramModule.RamIncrease;
            }
        }

        newMax = MathF.Max(0f, newMax);

        var newCurrent = comp.CurrentRam;
        if (newMax > oldMax)
            newCurrent = MathF.Min(newCurrent + (newMax - oldMax), newMax);
        else
            newCurrent = MathF.Min(newCurrent, newMax);

        if (comp.MaxRam == newMax && comp.CurrentRam == newCurrent)
            return;

        comp.MaxRam = newMax;
        comp.CurrentRam = newCurrent;
        Dirty(ent);
        UpdateRegenTracking(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_regeneratingDecks.Count == 0)
            return;

        _regeneratingDeckBuffer.Clear();
        foreach (var uid in _regeneratingDecks)
        {
            _regeneratingDeckBuffer.Add(uid);
        }

        foreach (var uid in _regeneratingDeckBuffer)
        {
            if (!TryComp<CyberDeckComponent>(uid, out var comp))
            {
                _regeneratingDecks.Remove(uid);
                continue;
            }

            if (comp.CurrentRam >= comp.MaxRam)
            {
                _regeneratingDecks.Remove(uid);
                continue;
            }

            var regenTime = MathF.Max(MinRegenTime, comp.RamRegenTime);
            comp.RegenAccumulator += frameTime;

            if (comp.RegenAccumulator < regenTime)
                continue;

            var regeneratedSteps = (int) (comp.RegenAccumulator / regenTime);
            if (regeneratedSteps <= 0)
                continue;

            comp.RegenAccumulator -= regeneratedSteps * regenTime;
            comp.CurrentRam = MathF.Min(comp.CurrentRam + regeneratedSteps, comp.MaxRam);
            Dirty(uid, comp);

            UpdateRegenTracking((uid, comp));
        }
    }

    public bool TrySpendRam(EntityUid uid, float amount, CyberDeckComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return false;

        if (amount < 0f)
            return false;

        if (amount == 0f)
            return true;

        if (comp.CurrentRam < amount)
            return false;

        comp.CurrentRam -= amount;
        comp.RegenAccumulator = 0f;
        Dirty(uid, comp);
        UpdateRegenTracking((uid, comp));
        return true;
    }

    private void UpdateRegenTracking(Entity<CyberDeckComponent> ent)
    {
        if (ent.Comp.CurrentRam < ent.Comp.MaxRam)
            _regeneratingDecks.Add(ent.Owner);
        else
            _regeneratingDecks.Remove(ent.Owner);
    }

    public float GetCurrentRam(EntityUid uid, CyberDeckComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return 0f;

        return comp.CurrentRam;
    }

    public float GetMaxRam(EntityUid uid, CyberDeckComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return 0f;

        return comp.MaxRam;
    }
}
