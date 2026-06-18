using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Clothing;

public sealed class ClothingDirtSystem : EntitySystem
{
    public const string DefaultSolutionName = "dirt";
    private const float DryUpdateInterval = 5f;

    private static readonly SlotFlags UnderwearSlots = SlotFlags.UNDERWEART | SlotFlags.UNDERWEARB;

    private static readonly SlotFlags SplashPreferredSlots = SlotFlags.OUTERCLOTHING;
    private static readonly SlotFlags SplashFallbackSlots = SlotFlags.INNERCLOTHING;
    private static readonly SlotFlags SplashAdditionalSlots = SlotFlags.GLOVES;

    private static readonly SlotFlags PuddleCrawlPreferredSlots = SlotFlags.OUTERCLOTHING;
    private static readonly SlotFlags PuddleCrawlFallbackSlots = SlotFlags.INNERCLOTHING;
    private static readonly SlotFlags PuddleCrawlAdditionalSlots =
        SlotFlags.GLOVES | SlotFlags.HEAD | SlotFlags.MASK;
    public static readonly SlotFlags BleedSlots = SlotFlags.INNERCLOTHING;

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    private readonly HashSet<EntityUid> _dryingClothing = new();
    private readonly List<EntityUid> _dryingBuffer = new();
    private readonly List<DirtCleaner> _dirtCleaners = new();
    private readonly List<ReagentQuantity> _sourceBuffer = new();
    private float _dryUpdateAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingDirtableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ClothingDirtableComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ClothingDirtableComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ClothingDirtableComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
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

        if (_dryingClothing.Count == 0)
            return;

        _dryingBuffer.Clear();
        _dryingBuffer.AddRange(_dryingClothing);

        foreach (var uid in _dryingBuffer)
        {
            if (!TryComp(uid, out ClothingDirtableComponent? component))
            {
                _dryingClothing.Remove(uid);
                continue;
            }

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
            SetDryingActive(ent, HasDryableDirt(solution.Value.Comp.Solution, ent.Comp));
            UpdateDirtVisuals(ent, solution.Value.Comp.Solution);
        }
    }

    private void OnShutdown(Entity<ClothingDirtableComponent> ent, ref ComponentShutdown args)
    {
        _dryingClothing.Remove(ent.Owner);
    }

    private void OnSolutionChanged(Entity<ClothingDirtableComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (!_net.IsServer || args.SolutionId != ent.Comp.Solution)
            return;

        SetDryingActive(ent, HasDryableDirt(args.Solution, ent.Comp));
        UpdateDirtVisuals(ent, args.Solution);
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
        return TryDirtyClothingDetailed(clothing, source, amount, removeFromSource, component).Dirtied;
    }

    private DirtApplicationResult TryDirtyClothingDetailed(
        EntityUid clothing,
        Solution source,
        FixedPoint2 amount,
        bool removeFromSource = false,
        ClothingDirtableComponent? component = null)
    {
        if (!Resolve(clothing, ref component, logMissing: false) || source.Volume <= FixedPoint2.Zero)
            return DirtApplicationResult.Empty;

        if (!_solutions.EnsureSolutionEntity(clothing, component.Solution, out var dirtSolution, component.Capacity)
            || dirtSolution == null)
            return DirtApplicationResult.Empty;

        var dirt = dirtSolution.Value.Comp.Solution;
        var sample = MakeCappedDirtSample(source, dirt, amount, component, removeFromSource);
        if (sample.Volume <= FixedPoint2.Zero)
            return DirtApplicationResult.Empty;

        if (!_solutions.TryAddSolution(dirtSolution.Value, sample))
            return DirtApplicationResult.Empty;

        SetDryingActive((clothing, component), HasDryableDirt(dirt, component));
        UpdateDirtVisuals((clothing, component), dirt);
        return new DirtApplicationResult(true, IsDeepDirty(dirt, component));
    }

    public bool TryAddCleanerToClothing(
        EntityUid clothing,
        ReagentId cleanerReagent,
        FixedPoint2 amount,
        ClothingDirtableComponent? component = null)
    {
        if (amount <= FixedPoint2.Zero)
            return false;

        var cleaner = new Solution();
        cleaner.AddReagent(cleanerReagent, amount);
        if (TryDirtyClothing(clothing, cleaner, amount, component: component))
        {
            if (Resolve(clothing, ref component, logMissing: false))
                ProcessDirtCleaners((clothing, component));

            return true;
        }

        if (!Resolve(clothing, ref component, logMissing: false)
            || !_prototype.Resolve<ReagentPrototype>(cleanerReagent.Prototype, out var cleanerPrototype)
            || cleanerPrototype.ClothingDirtCleanMultiplier <= FixedPoint2.Zero
            || !_solutions.TryGetSolution(clothing, component.Solution, out var dirtSolution, out var dirt))
        {
            return false;
        }

        var cleanerRoom = component.MaxReagentAmount - dirt.GetReagentQuantity(cleanerReagent);
        var roomNeeded = FixedPoint2.Min(amount, cleanerRoom);
        if (roomNeeded <= FixedPoint2.Zero)
            return false;

        _dirtCleaners.Clear();
        _dirtCleaners.Add(new DirtCleaner(cleanerReagent, roomNeeded, cleanerPrototype.ClothingDirtCleanMultiplier));

        var washableVolume = GetWashableDirtVolume(dirt, _dirtCleaners);
        var removed = RemoveWashableDirt(dirt, _dirtCleaners, FixedPoint2.Min(roomNeeded, washableVolume), washableVolume);
        if (removed <= FixedPoint2.Zero)
            return false;

        _solutions.UpdateChemicals(dirtSolution.Value);
        UpdateDirtVisuals((clothing, component), dirt);

        cleaner = new Solution();
        cleaner.AddReagent(cleanerReagent, roomNeeded);
        if (!TryDirtyClothing(clothing, cleaner, roomNeeded, component: component))
            return false;

        ProcessDirtCleaners((clothing, component));
        return true;
    }

    public bool TryWashClothing(
        EntityUid clothing,
        ReagentId cleanerReagent,
        FixedPoint2 amount,
        ClothingDirtableComponent? component = null)
    {
        if (!_net.IsServer
            || amount <= FixedPoint2.Zero
            || !Resolve(clothing, ref component, logMissing: false)
            || !_prototype.Resolve<ReagentPrototype>(cleanerReagent.Prototype, out var cleanerPrototype)
            || cleanerPrototype.ClothingDirtCleanMultiplier <= FixedPoint2.Zero)
        {
            return false;
        }

        if (!_solutions.TryGetSolution(clothing, component.Solution, out var dirtSolution, out var dirt)
            || dirt.Volume <= FixedPoint2.Zero)
        {
            return false;
        }

        _dirtCleaners.Clear();
        _dirtCleaners.Add(new DirtCleaner(cleanerReagent, amount, cleanerPrototype.ClothingDirtCleanMultiplier));

        var washableVolume = GetWashableDirtVolume(dirt, _dirtCleaners);
        if (washableVolume <= FixedPoint2.Zero)
            return false;

        var washAmount = FixedPoint2.Min(amount * cleanerPrototype.ClothingDirtCleanMultiplier, washableVolume);
        var removed = RemoveWashableDirt(dirt, _dirtCleaners, washAmount, washableVolume);
        if (removed <= FixedPoint2.Zero)
            return false;

        _solutions.UpdateChemicals(dirtSolution.Value);
        UpdateDirtVisuals((clothing, component), dirt);
        SetDryingActive((clothing, component), HasDryableDirt(dirt, component));
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

        if (!removeFromSource)
        {
            AddCappedDirtFromContents(source.Contents, source, dirt, sample, target, component, false);
            return sample;
        }

        _sourceBuffer.Clear();
        _sourceBuffer.AddRange(source.Contents);
        AddCappedDirtFromContents(_sourceBuffer, source, dirt, sample, target, component, true);
        _sourceBuffer.Clear();

        return sample;
    }

    private void AddCappedDirtFromContents(
        List<ReagentQuantity> sourceContents,
        Solution source,
        Solution dirt,
        Solution sample,
        FixedPoint2 target,
        ClothingDirtableComponent component,
        bool removeFromSource)
    {
        var remainingVolume = target;
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
    }

    private void DryClothing(Entity<ClothingDirtableComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var dirtSolution, out var dirt))
        {
            SetDryingActive(ent, false);
            ClearDirtVisuals(ent);
            return;
        }

        if (dirt.Volume <= FixedPoint2.Zero)
        {
            SetDryingActive(ent, false);
            UpdateDirtVisuals(ent, dirt);
            return;
        }

        var dried = false;
        dried |= WashDirt(dirt);

        for (var i = dirt.Contents.Count - 1; i >= 0; i--)
        {
            var reagent = dirt.Contents[i];
            var dryMinimum = GetDryMinimum(reagent.Reagent, ent.Comp);
            var excess = reagent.Quantity - dryMinimum;
            if (excess <= FixedPoint2.Zero)
                continue;

            var removed = dirt.RemoveReagent(reagent.Reagent, FixedPoint2.Min(ent.Comp.DryAmount, excess));
            if (removed > FixedPoint2.Zero)
                dried = true;
        }

        if (dried)
        {
            _solutions.UpdateChemicals(dirtSolution.Value);
            UpdateDirtVisuals(ent, dirt);
        }

        SetDryingActive(ent, HasDryableDirt(dirt, ent.Comp));
        if (!ent.Comp.DryingActive)
            ent.Comp.DryAccumulator = 0f;
    }

    private bool WashDirt(Solution dirt)
    {
        var cleaners = GetDirtCleaners(dirt);
        if (cleaners.Count == 0)
            return false;

        var washableVolume = GetWashableDirtVolume(dirt, cleaners);
        if (washableVolume <= FixedPoint2.Zero)
            return false;

        var washed = false;
        foreach (var cleaner in cleaners)
        {
            if (washableVolume <= FixedPoint2.Zero)
                break;

            var cleanCapacity = cleaner.Quantity * cleaner.CleanMultiplier;
            var washAmount = FixedPoint2.Min(cleanCapacity, washableVolume);
            if (washAmount <= FixedPoint2.Zero)
                continue;

            var removed = RemoveWashableDirt(dirt, cleaners, washAmount, washableVolume);
            if (removed <= FixedPoint2.Zero)
                continue;

            var cleanerUsed = FixedPoint2.Min(cleaner.Quantity, removed / cleaner.CleanMultiplier);
            dirt.RemoveReagent(cleaner.Reagent, cleanerUsed);
            washableVolume -= removed;
            washed = true;
        }

        return washed;
    }

    private void ProcessDirtCleaners(Entity<ClothingDirtableComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out var dirtSolution, out var dirt))
            return;

        if (!WashDirt(dirt))
            return;

        _solutions.UpdateChemicals(dirtSolution.Value);
        UpdateDirtVisuals(ent, dirt);
        SetDryingActive(ent, HasDryableDirt(dirt, ent.Comp));
    }

    private bool HasDryableDirt(Solution dirt, ClothingDirtableComponent component)
    {
        if (CanWashDirt(dirt))
            return true;

        foreach (var reagent in dirt.Contents)
        {
            if (reagent.Quantity > GetDryMinimum(reagent.Reagent, component))
                return true;
        }

        return false;
    }

    private FixedPoint2 GetDryMinimum(ReagentId reagent, ClothingDirtableComponent component)
    {
        return _prototype.Resolve<ReagentPrototype>(reagent.Prototype, out var prototype)
               && prototype.EvaporationSpeed > FixedPoint2.Zero
            ? FixedPoint2.Zero
            : component.DryMinimum;
    }

    private bool IsDeepDirty(Solution dirt, ClothingDirtableComponent component)
    {
        var thresholdCapacity = FixedPoint2.Min(component.Capacity, component.MaxReagentAmount);
        if (thresholdCapacity <= FixedPoint2.Zero)
            return false;

        var threshold = FixedPoint2.New(thresholdCapacity.Float() * component.DeepDirtThreshold);
        return dirt.Volume >= threshold;
    }

    private void UpdateDirtVisuals(Entity<ClothingDirtableComponent> ent, Solution dirt)
    {
        Color? color = null;
        if (dirt.Volume > FixedPoint2.Zero)
        {
            var alpha = Math.Clamp(dirt.Volume.Float() / ent.Comp.Capacity.Float(),
                ent.Comp.MinVisualAlpha,
                ent.Comp.MaxVisualAlpha);

            color = dirt.GetColor(_prototype).WithAlpha(alpha);
        }

        if (ent.Comp.DirtColor == color)
            return;

        ent.Comp.DirtColor = color;
        Dirty(ent);
        _item.VisualsChanged(ent.Owner);
    }

    private void ClearDirtVisuals(Entity<ClothingDirtableComponent> ent)
    {
        if (ent.Comp.DirtColor == null)
            return;

        ent.Comp.DirtColor = null;
        Dirty(ent);
        _item.VisualsChanged(ent.Owner);
    }

    private void SetDryingActive(Entity<ClothingDirtableComponent> ent, bool active)
    {
        ent.Comp.DryingActive = active;

        if (!_net.IsServer)
            return;

        if (active)
            _dryingClothing.Add(ent.Owner);
        else
            _dryingClothing.Remove(ent.Owner);
    }

    private bool CanWashDirt(Solution dirt)
    {
        var hasCleaner = false;
        var hasWashableDirt = false;

        foreach (var reagent in dirt.Contents)
        {
            if (reagent.Quantity <= FixedPoint2.Zero)
                continue;

            if (IsCleanerReagent(reagent.Reagent))
                hasCleaner = true;
            else
                hasWashableDirt = true;

            if (hasCleaner && hasWashableDirt)
                return true;
        }

        return false;
    }

    private List<DirtCleaner> GetDirtCleaners(Solution dirt)
    {
        _dirtCleaners.Clear();
        foreach (var reagent in dirt.Contents)
        {
            if (reagent.Quantity <= FixedPoint2.Zero
                || !_prototype.Resolve<ReagentPrototype>(reagent.Reagent.Prototype, out var prototype)
                || prototype.ClothingDirtCleanMultiplier <= FixedPoint2.Zero)
                continue;

            _dirtCleaners.Add(new DirtCleaner(reagent.Reagent, reagent.Quantity, prototype.ClothingDirtCleanMultiplier));
        }

        return _dirtCleaners;
    }

    private FixedPoint2 GetWashableDirtVolume(Solution dirt, List<DirtCleaner> cleaners)
    {
        var washableVolume = FixedPoint2.Zero;
        foreach (var reagent in dirt.Contents)
        {
            if (IsCleanerReagent(reagent.Reagent, cleaners))
                continue;

            washableVolume += reagent.Quantity;
        }

        return washableVolume;
    }

    private FixedPoint2 RemoveWashableDirt(
        Solution dirt,
        List<DirtCleaner> cleaners,
        FixedPoint2 amount,
        FixedPoint2 washableVolume)
    {
        var removedTotal = FixedPoint2.Zero;
        var remaining = amount;
        for (var i = dirt.Contents.Count - 1; i >= 0; i--)
        {
            if (remaining <= FixedPoint2.Zero)
                break;

            var reagent = dirt.Contents[i];
            if (IsCleanerReagent(reagent.Reagent, cleaners))
                continue;

            var remove = FixedPoint2.Min(reagent.Quantity / washableVolume * amount, reagent.Quantity, remaining);
            if (remove <= FixedPoint2.Zero)
                continue;

            var removed = dirt.RemoveReagent(reagent.Reagent, remove);
            removedTotal += removed;
            remaining -= removed;
        }

        if (remaining > FixedPoint2.Zero)
        {
            for (var i = dirt.Contents.Count - 1; i >= 0; i--)
            {
                if (remaining <= FixedPoint2.Zero)
                    break;

                var reagent = dirt.Contents[i];
                if (IsCleanerReagent(reagent.Reagent, cleaners))
                    continue;

                var removed = dirt.RemoveReagent(reagent.Reagent, FixedPoint2.Min(reagent.Quantity, remaining));
                removedTotal += removed;
                remaining -= removed;
            }
        }

        return removedTotal;
    }

    private static bool IsCleanerReagent(ReagentId reagent, List<DirtCleaner> cleaners)
    {
        foreach (var cleaner in cleaners)
        {
            if (cleaner.Reagent.Equals(reagent))
                return true;
        }

        return false;
    }

    private bool IsCleanerReagent(ReagentId reagent)
    {
        return _prototype.Resolve<ReagentPrototype>(reagent.Prototype, out var prototype)
               && prototype.ClothingDirtCleanMultiplier > FixedPoint2.Zero;
    }

    public bool TryDirtyWorn(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        SlotFlags slots,
        bool removeFromSource = false)
    {
        return TryDirtyWornLayer(wearer, source, amount, slots, removeFromSource).Dirtied;
    }

    public bool TryDirtyWornSplash(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        bool removeFromSource = false)
    {
        var dirtied = TryDirtyWornLayered(
            wearer,
            source,
            amount,
            removeFromSource,
            SplashPreferredSlots,
            SplashFallbackSlots,
            UnderwearSlots);

        dirtied |= TryDirtyWornLayered(
            wearer,
            source,
            amount,
            removeFromSource,
            SlotFlags.FEET,
            SlotFlags.SOCKS);

        dirtied |= TryDirtyWorn(wearer, source, amount, SplashAdditionalSlots, removeFromSource);
        return dirtied;
    }

    public bool TryDirtyWornPuddleStep(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        bool removeFromSource = false)
    {
        return TryDirtyWornPuddleStep(wearer, source, amount, out _, removeFromSource);
    }

    public bool TryDirtyWornPuddleStep(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        out FixedPoint2 bodyAmount,
        bool removeFromSource = false)
    {
        var result = TryDirtyWornLayeredDetailed(
            wearer,
            source,
            amount,
            removeFromSource,
            SlotFlags.FEET,
            SlotFlags.SOCKS);
        bodyAmount = result.PassedAmount;
        return result.Dirtied;
    }

    public bool TryDirtyWornPuddleCrawl(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        bool removeFromSource = false)
    {
        var dirtied = TryDirtyWornLayered(
            wearer,
            source,
            amount,
            removeFromSource,
            PuddleCrawlPreferredSlots,
            PuddleCrawlFallbackSlots,
            UnderwearSlots);

        dirtied |= TryDirtyWorn(wearer, source, amount, PuddleCrawlAdditionalSlots, removeFromSource);
        return dirtied;
    }

    private bool TryDirtyWornLayered(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        bool removeFromSource = false,
        params SlotFlags[] layers)
    {
        return TryDirtyWornLayeredDetailed(wearer, source, amount, removeFromSource, layers).Dirtied;
    }

    private LayeredDirtResult TryDirtyWornLayeredDetailed(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        bool removeFromSource,
        params SlotFlags[] layers)
    {
        if (source.Volume <= FixedPoint2.Zero || amount <= FixedPoint2.Zero || layers.Length == 0)
            return LayeredDirtResult.Empty;

        var currentAmount = amount;
        var dirtied = false;

        foreach (var layer in layers)
        {
            var result = TryDirtyWornLayer(wearer, source, currentAmount, layer, removeFromSource);
            if (!result.HadItem)
                continue;

            dirtied |= result.Dirtied;
            if (!result.DeepDirty || result.DeepAmount <= FixedPoint2.Zero)
                return new LayeredDirtResult(dirtied, FixedPoint2.Zero);

            currentAmount = FixedPoint2.Min(result.DeepAmount, source.Volume);
            if (currentAmount <= FixedPoint2.Zero)
                return new LayeredDirtResult(dirtied, FixedPoint2.Zero);
        }

        return new LayeredDirtResult(dirtied, currentAmount);
    }

    private LayerDirtResult TryDirtyWornLayer(
        EntityUid wearer,
        Solution source,
        FixedPoint2 amount,
        SlotFlags slots,
        bool removeFromSource)
    {
        if (source.Volume <= FixedPoint2.Zero || amount <= FixedPoint2.Zero)
            return LayerDirtResult.Empty;

        if (!_inventory.TryGetContainerSlotEnumerator(wearer, out var enumerator, slots))
            return LayerDirtResult.Empty;

        var hadItem = false;
        var dirtied = false;
        var deepDirty = false;
        var deepAmount = FixedPoint2.Zero;
        while (enumerator.NextItem(out var item))
        {
            hadItem = true;

            if (TryComp(item, out ClothingDirtableComponent? dirtable))
            {
                var alreadyDeepDirty = _solutions.TryGetSolution(item, dirtable.Solution, out _, out var dirt)
                                       && IsDeepDirty(dirt, dirtable);
                var result = TryDirtyClothingDetailed(item, source, amount, removeFromSource, dirtable);
                dirtied |= result.Dirtied;

                if (alreadyDeepDirty || result.DeepDirty)
                {
                    deepDirty = true;
                    deepAmount = FixedPoint2.Max(deepAmount, amount * dirtable.DeepDirtTransferFraction);
                }
            }

            if (source.Volume <= FixedPoint2.Zero)
                break;
        }

        return new LayerDirtResult(hadItem, dirtied, deepDirty, deepAmount);
    }

    public Solution MakeBloodDirt(string reagent, FixedPoint2 amount, List<ReagentData>? data)
    {
        var solution = new Solution();
        solution.AddReagent(new ReagentId(reagent, data), amount);
        return solution;
    }

    private readonly record struct DirtCleaner(ReagentId Reagent, FixedPoint2 Quantity, FixedPoint2 CleanMultiplier);
    private readonly record struct DirtApplicationResult(bool Dirtied, bool DeepDirty)
    {
        public static readonly DirtApplicationResult Empty = new(false, false);
    }

    private readonly record struct LayerDirtResult(
        bool HadItem,
        bool Dirtied,
        bool DeepDirty,
        FixedPoint2 DeepAmount)
    {
        public static readonly LayerDirtResult Empty = new(false, false, false, FixedPoint2.Zero);
    }

    private readonly record struct LayeredDirtResult(bool Dirtied, FixedPoint2 PassedAmount)
    {
        public static readonly LayeredDirtResult Empty = new(false, FixedPoint2.Zero);
    }
}
