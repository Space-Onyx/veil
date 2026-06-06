using Content.Goobstation.Maths.FixedPoint;
using Content.Shared._Onyx.Clothing;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Interaction;

namespace Content.Server._Onyx.Clothing;

public sealed class ClothingDirtWasherSystem : EntitySystem
{
    [Dependency] private readonly ClothingDirtSystem _clothingDirt = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingDirtWasherComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
    }

    private void OnAfterInteractUsing(Entity<ClothingDirtWasherComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (!HasComp<ClothingDirtableComponent>(args.Used)
            || !_solutions.TryGetDrainableSolution(ent.Owner, out var washerSolution, out var washer)
            || washer.GetTotalPrototypeQuantity(ent.Comp.CleanerReagent) <= FixedPoint2.Zero)
        {
            return;
        }

        var amount = FixedPoint2.Min(ent.Comp.Amount, washer.GetTotalPrototypeQuantity(ent.Comp.CleanerReagent));
        if (!_clothingDirt.TryWashClothing(args.Used, new ReagentId(ent.Comp.CleanerReagent, null), amount))
            return;

        washer.RemoveReagent(ent.Comp.CleanerReagent, amount, ignoreReagentData: true);
        _solutions.UpdateChemicals(washerSolution.Value);
        args.Handled = true;
    }
}
