using Content.Server.Popups;
using Content.Shared._Onyx.Clothing;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Server.GameObjects;

namespace Content.Server._Onyx.Clothing;

public sealed class ShowerSystem : EntitySystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly ClothingDirtSystem _clothingDirt = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private readonly HashSet<EntityUid> _entities = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ShowerComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<ShowerComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShowerComponent>();
        while (query.MoveNext(out var uid, out var shower))
        {
            if (!shower.Enabled)
                continue;

            shower.WashAccumulator += frameTime;
            if (shower.WashAccumulator < shower.WashInterval)
                continue;

            shower.WashAccumulator %= shower.WashInterval;
            WashArea(uid, shower);
        }
    }

    private void OnStartup(Entity<ShowerComponent> ent, ref ComponentStartup args)
    {
        UpdateVisuals(ent);
    }

    private void OnInteractHand(Entity<ShowerComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        SetEnabled(ent, !ent.Comp.Enabled);
        args.Handled = true;

        var message = ent.Comp.Enabled
            ? Loc.GetString("shower-component-switched-on")
            : Loc.GetString("shower-component-switched-off");

        _popup.PopupEntity(message, ent.Owner, args.User, PopupType.Small);
    }

    private void OnExamined(Entity<ShowerComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Enabled
            ? "shower-component-examine-on"
            : "shower-component-examine-off"));
    }

    private void SetEnabled(Entity<ShowerComponent> ent, bool enabled)
    {
        if (ent.Comp.Enabled == enabled)
            return;

        ent.Comp.Enabled = enabled;
        ent.Comp.WashAccumulator = 0f;
        Dirty(ent);
        UpdateVisuals(ent);
    }

    private void UpdateVisuals(Entity<ShowerComponent> ent)
    {
        _appearance.SetData(ent.Owner, ShowerVisuals.Enabled, ent.Comp.Enabled);
    }

    private void WashArea(EntityUid uid, ShowerComponent shower)
    {
        _entities.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, shower.WashRange, _entities, LookupFlags.Dynamic);

        foreach (var entity in _entities)
        {
            if (entity == uid)
                continue;

            WashWornClothing(entity, shower);
        }
    }

    private void WashWornClothing(EntityUid wearer, ShowerComponent shower)
    {
        if (!_inventory.TryGetContainerSlotEnumerator(wearer, out var enumerator, shower.TargetSlots))
            return;

        var cleaner = new ReagentId(shower.CleanerReagent, null);
        while (enumerator.NextItem(out var item))
        {
            if (!HasComp<ClothingDirtableComponent>(item))
                continue;

            _clothingDirt.TryAddCleanerToClothing(item, cleaner, shower.WashAmount);
        }
    }
}
