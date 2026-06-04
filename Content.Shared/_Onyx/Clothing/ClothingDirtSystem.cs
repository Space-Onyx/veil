using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Clothing;

public sealed class ClothingDirtSystem : EntitySystem
{
    public const string DefaultSolutionName = "dirt";
    private const float DryUpdateInterval = 5f;

    public static readonly SlotFlags SplashSlots =
        SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING | SlotFlags.FEET | SlotFlags.GLOVES;

    public static readonly SlotFlags PuddleStepSlots = SlotFlags.INNERCLOTHING | SlotFlags.FEET;
    public static readonly SlotFlags BleedSlots = SlotFlags.INNERCLOTHING;

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    private float _dryUpdateAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingDirtableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ClothingDirtableComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        _dryUpdateAccumulator += frameTime;
        if (_dryUpdateAccumulator < DryUpdateInterval)
            return;

        var elapsed = _dryUpdateAccumulator;
        _dryUpdateAccumulator = 0f;

        var query = EntityQueryEnumerator<ClothingDirtableComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.DryingActive)
                continue;

            component.DryAccumulator += elapsed;
            if (component.DryAccumulator < component.DryInterval)
                continue;

            component.DryAccumulator %= component.DryInterval;
            DryClothing((uid, component));
        }
    }

    private void OnMapInit(Entity<ClothingDirtableComponent> ent, ref MapInitEvent args)
    {
        if (_solutions.EnsureSolutionEntity(ent.Owner, ent.Comp.Solution, out var solution, ent.Comp.Capacity)
            && solution != null)
        {
            ent.Comp.DryingActive = HasDryableDirt(solution.Value.Comp.Solution, ent.Comp);
        }
    }

    private void OnExamined(Entity<ClothingDirtableComponent> ent, ref ExaminedEvent args)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution)
            || solution.Volume <= FixedPoint2.Zero)
            return;

        if (solution.GetPrimaryReagentId() is not { } primaryReagent
            || !_prototype.Resolve<ReagentPrototype>(primaryReagent.Prototype, out var primary))
            return;

        var color = solution.GetColor(_prototype).ToHexNoAlpha();
        args.PushMarkup(Loc.GetString("clothing-dirtable-examine",
            ("color", color),
            ("desc", primary.LocalizedPhysicalDescription),
            ("chemCount", solution.Contents.Count)));
    }

    public bool TryDirtyClothing(
        EntityUid clothing,
        Solution source,
        FixedPoint2 amount,
        bool removeFromSource = false,
        ClothingDirtableComponent? component = null)
    {
        if (!Resolve(clothing, ref component, logMissing: false) || source.Volume <= FixedPoint2.Zero)
            return false;

        if (!_solutions.EnsureSolutionEntity(clothing, component.Solution, out var dirtSolution, component.Capacity)
            || dirtSolution == null)
            return false;

        var dirt = dirtSolution.Value.Comp.Solution;
        var sample = MakeCappedDirtSample(source, dirt, amount, component, removeFromSource);
        if (sample.Volume <= FixedPoint2.Zero)
            return false;

        if (!_solutions.TryAddSolution(dirtSolution.Value, sample))
            return false;

        component.DryingActive = HasDryableDirt(dirt, component);
        return true;
    }

    private Solution MakeCappedDirtSample(
        Solution source,
        Solution dirt,
        FixedPoint2 amount,
        ClothingDirtableComponent component,
        bool removeFromSource)
    {
        var sample = new Solution();
        var target = FixedPoint2.Min(amount, source.Volume, dirt.AvailableVolume);
        if (target <= FixedPoint2.Zero)
            return sample;

        var remainingVolume = target;
        var sourceContents = new List<ReagentQuantity>(source.Contents);
        foreach (var reagent in sourceContents)
        {
            if (remainingVolume <= FixedPoint2.Zero)
                break;

            var alreadyDirty = dirt.GetReagentQuantity(reagent.Reagent);
            var availableForReagent = component.MaxReagentAmount - alreadyDirty;
            if (availableForReagent <= FixedPoint2.Zero)
                continue;

            var proportionalAmount = reagent.Quantity / source.Volume * target;
            var accepted = FixedPoint2.Min(proportionalAmount, availableForReagent, remainingVolume);
            if (accepted <= FixedPoint2.Zero)
                continue;

            sample.AddReagent(reagent.Reagent, accepted);
            remainingVolume -= accepted;

            if (removeFromSource)
                source.RemoveReagent(reagent.Reagent, accepted);
        }

        return sample;
    }

    private void DryClothing(Entity<ClothingDirtableComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var dirtSolution, out var dirt)
            || dirt.Volume <= FixedPoint2.Zero)
        {
            ent.Comp.DryingActive = false;
            return;
        }

        var dried = false;
        for (var i = dirt.Contents.Count - 1; i >= 0; i--)
        {
            var reagent = dirt.Contents[i];
            var excess = reagent.Quantity - ent.Comp.DryMinimum;
            if (excess <= FixedPoint2.Zero)
                continue;

            var removed = dirt.RemoveReagent(reagent.Reagent, FixedPoint2.Min(ent.Comp.DryAmount, excess));
            if (removed > FixedPoint2.Zero)
                dried = true;
        }

        if (dried)
            _solutions.UpdateChemicals(dirtSolution.Value);

        ent.Comp.DryingActive = HasDryableDirt(dirt, ent.Comp);
        if (!ent.Comp.DryingActive)
            ent.Comp.DryAccumulator = 0f;
    }

    private bool HasDryableDirt(Solution dirt, ClothingDirtableComponent component)
    {
        foreach (var reagent in dirt.Contents)
        {
            if (reagent.Quantity > component.DryMinimum)
                return true;
        }

        return false;
    }

    public bool TryDirtyWorn(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        SlotFlags slots,
        bool removeFromSource = false)
    {
        if (source.Volume <= FixedPoint2.Zero || amount <= FixedPoint2.Zero)
            return false;

        if (!_inventory.TryGetContainerSlotEnumerator(wearer, out var enumerator, slots))
            return false;

        var clothing = new List<Entity<ClothingDirtableComponent>>();
        while (enumerator.NextItem(out var item))
        {
            if (TryComp(item, out ClothingDirtableComponent? dirtable))
                clothing.Add((item, dirtable));
        }

        if (clothing.Count == 0)
            return false;

        var dirtied = false;
        foreach (var item in clothing)
        {
            if (TryDirtyClothing(item.Owner, source, amount, removeFromSource, item.Comp))
                dirtied = true;
        }

        return dirtied;
    }

    public Solution MakeBloodDirt(string reagent, FixedPoint2 amount, List<ReagentData>? data)
    {
        var solution = new Solution();
        solution.AddReagent(new ReagentId(reagent, data), amount);
        return solution;
    }
}
