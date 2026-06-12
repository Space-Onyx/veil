using Content.Goobstation.Maths.FixedPoint;
using Content.Server.Popups;
using Content.Shared._Onyx.Clothing;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._Onyx.Clothing;

public sealed class ClothingDirtWasherSystem : EntitySystem
{
    [Dependency] private readonly ClothingDirtSystem _clothingDirt = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingDirtWasherComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<ClothingDirtWasherComponent, WashClothingDoAfterEvent>(OnWashDoAfter);
    }

    private void OnAfterInteractUsing(Entity<ClothingDirtWasherComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (!HasComp<ClothingDirtableComponent>(args.Used)
            || !_solutions.TryGetDrainableSolution(ent.Owner, out _, out var washer)
            || washer.GetTotalPrototypeQuantity(ent.Comp.CleanerReagent) <= FixedPoint2.Zero)
        {
            return;
        }

        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            args.User,
            ent.Comp.WashTime,
            new WashClothingDoAfterEvent(),
            ent.Owner,
            target: ent.Owner,
            used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        }))
        {
            return;
        }

        args.Handled = true;

        var selfMessage = Loc.GetString("clothing-dirt-washing-self",
            ("clothing", Identity.Entity(args.Used, EntityManager)));
        _popup.PopupEntity(selfMessage, args.User, args.User, PopupType.Medium);

        var othersMessage = Loc.GetString("clothing-dirt-washing-others",
            ("user", Identity.Entity(args.User, EntityManager)),
            ("clothing", Identity.Entity(args.Used, EntityManager)));
        _popup.PopupEntity(othersMessage, args.User, Filter.PvsExcept(args.User), true, PopupType.Medium);
    }

    private void OnWashDoAfter(Entity<ClothingDirtWasherComponent> ent, ref WashClothingDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } clothing)
            return;

        if (!HasComp<ClothingDirtableComponent>(clothing)
            || !_solutions.TryGetDrainableSolution(ent.Owner, out var washerSolution, out var washer)
            || washer.GetTotalPrototypeQuantity(ent.Comp.CleanerReagent) <= FixedPoint2.Zero)
        {
            return;
        }

        var amount = FixedPoint2.Min(ent.Comp.Amount, washer.GetTotalPrototypeQuantity(ent.Comp.CleanerReagent));
        if (!_clothingDirt.TryWashClothing(clothing, new ReagentId(ent.Comp.CleanerReagent, null), amount))
            return;

        washer.RemoveReagent(ent.Comp.CleanerReagent, amount, ignoreReagentData: true);
        _solutions.UpdateChemicals(washerSolution.Value);
        args.Handled = true;
    }
}
