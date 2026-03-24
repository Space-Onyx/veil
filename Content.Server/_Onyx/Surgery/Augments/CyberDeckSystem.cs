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

    public override void Initialize()
    {
        base.Initialize();

        _scriptSystem = EntityManager.System<CyberDeckScriptSystem>();

        SubscribeLocalEvent<CyberDeckComponent, AugmentModuleInsertedEvent>(OnModuleInserted);
        SubscribeLocalEvent<CyberDeckComponent, AugmentModuleRemovedEvent>(OnModuleRemoved);
        SubscribeLocalEvent<CyberDeckComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<CyberDeckComponent> ent, ref ComponentStartup args)
    {
        RecalculateRam(ent);
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
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CyberDeckComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.CurrentRam >= comp.MaxRam)
                continue;

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
        return true;
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
