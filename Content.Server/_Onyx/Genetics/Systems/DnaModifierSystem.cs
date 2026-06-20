using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Inventory;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.Prayer;
using Content.Shared._EinsteinEngines.HeightAdjust;
using Content.Shared.Buckle;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Forensics.Components;
using Content.Shared.Genetics;
using Content.Shared.Genetics.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Content.Shared.CCVar;
using Robust.Shared.Timing;

namespace Content.Server.Genetics.System;

public sealed partial class DnaModifierSystem : SharedDnaModifierSystem
{
    [Dependency] private readonly IAdminLogManager _admin = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly EnsureMarkingSystem _ensureMarking = default!;
    [Dependency] private readonly StructuralEnzymesIndexerSystem _enzymesIndexer = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly HeightAdjustSystem _heightAdjust = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoidAppearance = default!;
    [Dependency] private readonly ServerInventorySystem _inventory = default!;
    [Dependency] private readonly MarkingPrototypesIndexerSystem _markingIndexer = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly PrayerSystem _prayerSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly ProtoId<EmotePrototype> Scream = "Scream";
    private static readonly TimeSpan DnaModificationCooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EvolutionKnockdownTime = TimeSpan.FromSeconds(4);
    private readonly HashSet<EntityUid> _entitiesUndergoingDnaChange = new();
    private readonly HashSet<EntityUid> _entitiesApplyingStoredDna = new();
    private readonly HashSet<EntityUid> _entitiesApplyingIdentityStabilizer = new();
    private readonly HashSet<EntityUid> _entitiesApplyingEvolutionStabilizer = new();
    private readonly HashSet<EntityUid> _entitiesReceivingConsoleRadiation = new();

    public override void Initialize()
    {
        base.Initialize();

        InitializeInjector();
        InitializeMap();

        SubscribeLocalEvent<DnaModifierComponent, ComponentInit>(OnDnaModifierInit);
        SubscribeLocalEvent<DnaModifierDeviationComponent, ComponentStartup>(OnDnaDeviation);

        SubscribeLocalEvent<DnaModifierComponent, CureDnaDiseaseAttemptEvent>(OnTryCureDnaDisease);
        SubscribeLocalEvent<DnaModifierComponent, MutateDnaAttemptEvent>(OnTryMutateDna);

        SubscribeLocalEvent<DnaModifierComponent, DamageChangedEvent>(OnDamageChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var instabilityQuery = EntityQueryEnumerator<DnaInstabilityComponent>();
        while (instabilityQuery.MoveNext(out var uid, out var instabilityComponent))
        {
            if (instabilityComponent.NextTimeTick <= 0)
            {
                instabilityComponent.NextTimeTick = 10;
                if (!TryComp<MobThresholdsComponent>(uid, out var uidThresholds)
                    || uidThresholds.CurrentThresholdState is MobState.Dead)
                    return;

                switch (instabilityComponent.Stage)
                {
                    case 1: InstabilityStageOne(uid); break;
                    case 2: InstabilityStageTwo(uid); break;
                    case 3: InstabilityStageThree(uid); break;
                    default: break;
                }
            }
            instabilityComponent.NextTimeTick -= frameTime;
        }

        var cooldownQuery = EntityQueryEnumerator<DnaRecentlyModifiedComponent>();
        while (cooldownQuery.MoveNext(out var uid, out var cooldown))
        {
            if (_timing.CurTime >= cooldown.ExpiresAt)
                RemCompDeferred<DnaRecentlyModifiedComponent>(uid);
        }
    }

    private void OnDnaModifierInit(EntityUid uid, DnaModifierComponent component, ComponentInit args)
    {
        InitializeStructuralEnzymes(uid, component);

        _ = InitializeDelayAsync(uid, component);
    }

    private void OnDnaDeviation(EntityUid uid, DnaModifierDeviationComponent component, ComponentStartup args)
    {
        if (!TryComp<DnaModifierComponent>(uid, out var dnaModifier) || dnaModifier.EnzymesPrototypes == null)
            return;

        var diseaseEnzymes = dnaModifier.EnzymesPrototypes
            .Where(enzyme =>
            {
                if (!_prototype.TryIndex<StructuralEnzymesPrototype>(enzyme.EnzymesPrototypeId, out var enzymePrototype))
                    return false;

                return enzymePrototype.TypeDeviation == EnzymesType.Disease;
            })
            .ToList();

        if (diseaseEnzymes.Count == 0)
            return;

        int countToModify = _random.Next(1, Math.Min(3, diseaseEnzymes.Count + 1));

        var enzymesToModify = diseaseEnzymes
            .OrderBy(_ => _random.Next())
            .Take(countToModify)
            .ToList();

        foreach (var enzyme in enzymesToModify)
        {
            enzyme.HexCode = GetHexCodeDisease();
        }

        TryChangeStructuralEnzymes((uid, dnaModifier));

        Dirty(uid, dnaModifier);
    }

    private async Task InitializeDelayAsync(EntityUid uid, DnaModifierComponent component)
    {
        await Task.Delay(1);
        InitializeUniqueIdentifiers(uid, component);

        await Task.Delay(1);
        CheckDeviations(uid, component);

        Dirty(uid, component);
    }

    #region Deep Cloning
    public UniqueIdentifiersData? CloneUniqueIdentifiers(UniqueIdentifiersData? source)
    {
        if (source == null)
            return null;

        return source.Clone(source);
    }

    public List<EnzymesPrototypeInfo>? CloneEnzymesPrototypes(List<EnzymesPrototypeInfo>? source)
    {
        if (source == null)
            return null;

        return source.Select(e => (EnzymesPrototypeInfo)e.Clone()).ToList();
    }
    #endregion

    #region Initialize U.I.
    private void InitializeUniqueIdentifiers(EntityUid uid, DnaModifierComponent component)
    {
        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            var uniqueIdentifiers = new UniqueIdentifiersData
            {
                ID = $"UniqueIdentifiers{uid}",
            };

            var markingSet = humanoid.MarkingSet;
            var markingPrototypes = _markingIndexer.GetAllMarkingPrototypes();

            var empty = new[] { "0", "0", "0" };

            // Цвет волос (блоки 1-3) и Вторичный цвет волос (блоки 4-6)
            if (markingSet.TryGetCategory(MarkingCategories.Hair, out var hairMarkings))
            {
                // блоки 1-3
                var hairColor = GetFirstMarkingColor(hairMarkings);
                var hairColorArray = ConvertColorToHexArray(hairColor);
                uniqueIdentifiers.HairColorR = new[] { hairColorArray[0], hairColorArray[1], hairColorArray[2] };
                uniqueIdentifiers.HairColorG = new[] { hairColorArray[3], hairColorArray[4], hairColorArray[5] };
                uniqueIdentifiers.HairColorB = new[] { hairColorArray[6], hairColorArray[7], hairColorArray[8] };

                // блок 34
                var markingId = hairMarkings.FirstOrDefault()?.MarkingId;
                var markingPrototype = markingPrototypes
                    .FirstOrDefault(m => m.MarkingPrototypeId == markingId);

                uniqueIdentifiers.HairStyle = markingPrototype != null
                    ? markingPrototype.HexValue
                    : empty;

                // блоки 4-6
                if (hairMarkings.Count > 1)
                {
                    var secondaryHairColor = hairMarkings[1].MarkingColors.Count > 0
                        ? hairMarkings[1].MarkingColors[0]
                        : Color.White;
                    var secondaryHairColorArray = ConvertColorToHexArray(secondaryHairColor);
                    uniqueIdentifiers.SecondaryHairColorR = new[] { secondaryHairColorArray[0], secondaryHairColorArray[1], secondaryHairColorArray[2] };
                    uniqueIdentifiers.SecondaryHairColorG = new[] { secondaryHairColorArray[3], secondaryHairColorArray[4], secondaryHairColorArray[5] };
                    uniqueIdentifiers.SecondaryHairColorB = new[] { secondaryHairColorArray[6], secondaryHairColorArray[7], secondaryHairColorArray[8] };
                }
                else
                {
                    uniqueIdentifiers.SecondaryHairColorR = GenerateRandomHexValues();
                    uniqueIdentifiers.SecondaryHairColorG = GenerateRandomHexValues();
                    uniqueIdentifiers.SecondaryHairColorB = GenerateRandomHexValues();
                }
            }
            else
            {
                // блоки 1-3
                uniqueIdentifiers.HairColorR = GenerateRandomHexValues();
                uniqueIdentifiers.HairColorG = GenerateRandomHexValues();
                uniqueIdentifiers.HairColorB = GenerateRandomHexValues();
                // блоки 4-6
                uniqueIdentifiers.SecondaryHairColorR = GenerateRandomHexValues();
                uniqueIdentifiers.SecondaryHairColorG = GenerateRandomHexValues();
                uniqueIdentifiers.SecondaryHairColorB = GenerateRandomHexValues();

                // блок 34
                uniqueIdentifiers.HairStyle = empty;
            }

            // Цвет бороды (блоки 7-9)
            if (markingSet.TryGetCategory(MarkingCategories.FacialHair, out var facialHairMarkings))
            {
                var facialHairColor = GetFirstMarkingColor(facialHairMarkings);
                var facialHairColorArray = ConvertColorToHexArray(facialHairColor);
                uniqueIdentifiers.BeardColorR = new[] { facialHairColorArray[0], facialHairColorArray[1], facialHairColorArray[2] };
                uniqueIdentifiers.BeardColorG = new[] { facialHairColorArray[3], facialHairColorArray[4], facialHairColorArray[5] };
                uniqueIdentifiers.BeardColorB = new[] { facialHairColorArray[6], facialHairColorArray[7], facialHairColorArray[8] };

                // блок 33
                var markingId = facialHairMarkings.FirstOrDefault()?.MarkingId;
                var markingPrototype = markingPrototypes
                    .FirstOrDefault(m => m.MarkingPrototypeId == markingId);

                uniqueIdentifiers.BeardStyle = markingPrototype != null
                    ? markingPrototype.HexValue
                    : empty;
            }
            else
            {
                uniqueIdentifiers.BeardColorR = GenerateRandomHexValues();
                uniqueIdentifiers.BeardColorG = GenerateRandomHexValues();
                uniqueIdentifiers.BeardColorB = GenerateRandomHexValues();

                // блок 33
                uniqueIdentifiers.BeardStyle = empty;
            }

            // Skin RGB (blocks 10-12).
            var (skinColorR, skinColorG, skinColorB) = ConvertColorToRgbBlocks(humanoid.SkinColor);
            uniqueIdentifiers.SkinColorR = skinColorR;
            uniqueIdentifiers.SkinColorG = skinColorG;
            uniqueIdentifiers.SkinColorB = skinColorB;
            uniqueIdentifiers.Race = GetSpeciesBlockOrEmpty(humanoid.Species);

            // Цвет головного аксессуара (блоки 17-19)
            if (markingSet.TryGetCategory(MarkingCategories.HeadTop, out var headTopMarkings))
            {
                var headTopColor = GetFirstMarkingColor(headTopMarkings);
                var headTopColorArray = ConvertColorToHexArray(headTopColor);
                uniqueIdentifiers.HeadAccessoryColorR = new[] { headTopColorArray[0], headTopColorArray[1], headTopColorArray[2] };
                uniqueIdentifiers.HeadAccessoryColorG = new[] { headTopColorArray[3], headTopColorArray[4], headTopColorArray[5] };
                uniqueIdentifiers.HeadAccessoryColorB = new[] { headTopColorArray[6], headTopColorArray[7], headTopColorArray[8] };

                // блок 35
                var markingId = headTopMarkings.FirstOrDefault()?.MarkingId;
                var markingPrototype = markingPrototypes
                    .FirstOrDefault(m => m.MarkingPrototypeId == markingId);

                uniqueIdentifiers.HeadAccessoryStyle = markingPrototype != null
                    ? markingPrototype.HexValue
                    : empty;
            }
            else
            {
                uniqueIdentifiers.HeadAccessoryColorR = GenerateRandomHexValues();
                uniqueIdentifiers.HeadAccessoryColorG = GenerateRandomHexValues();
                uniqueIdentifiers.HeadAccessoryColorB = GenerateRandomHexValues();

                // блок 35
                uniqueIdentifiers.HeadAccessoryStyle = empty;
            }

            // Цвет разметки головы (блоки 20-22)
            if (markingSet.TryGetCategory(MarkingCategories.Head, out var headMarkings))
            {
                var headColor = GetFirstMarkingColor(headMarkings);
                var headColorArray = ConvertColorToHexArray(headColor);
                uniqueIdentifiers.HeadMarkingColorR = new[] { headColorArray[0], headColorArray[1], headColorArray[2] };
                uniqueIdentifiers.HeadMarkingColorG = new[] { headColorArray[3], headColorArray[4], headColorArray[5] };
                uniqueIdentifiers.HeadMarkingColorB = new[] { headColorArray[6], headColorArray[7], headColorArray[8] };

                // блок 36
                var markingId = headMarkings.FirstOrDefault()?.MarkingId;
                var markingPrototype = markingPrototypes
                    .FirstOrDefault(m => m.MarkingPrototypeId == markingId);

                uniqueIdentifiers.HeadMarkingStyle = markingPrototype != null
                    ? markingPrototype.HexValue
                    : empty;
            }
            else
            {
                uniqueIdentifiers.HeadMarkingColorR = GenerateRandomHexValues();
                uniqueIdentifiers.HeadMarkingColorG = GenerateRandomHexValues();
                uniqueIdentifiers.HeadMarkingColorB = GenerateRandomHexValues();

                // блок 36
                uniqueIdentifiers.HeadMarkingStyle = empty;
            }

            // Цвет маркировки тела (блоки 23-25)
            if (markingSet.TryGetCategory(MarkingCategories.Chest, out var chestMarkings))
            {
                var chestColor = GetFirstMarkingColor(chestMarkings);
                var chestColorArray = ConvertColorToHexArray(chestColor);
                uniqueIdentifiers.BodyMarkingColorR = new[] { chestColorArray[0], chestColorArray[1], chestColorArray[2] };
                uniqueIdentifiers.BodyMarkingColorG = new[] { chestColorArray[3], chestColorArray[4], chestColorArray[5] };
                uniqueIdentifiers.BodyMarkingColorB = new[] { chestColorArray[6], chestColorArray[7], chestColorArray[8] };

                // блок 37
                var markingId = chestMarkings.FirstOrDefault()?.MarkingId;
                var markingPrototype = markingPrototypes
                    .FirstOrDefault(m => m.MarkingPrototypeId == markingId);

                uniqueIdentifiers.BodyMarkingStyle = markingPrototype != null
                    ? markingPrototype.HexValue
                    : empty;
            }
            else
            {
                uniqueIdentifiers.BodyMarkingColorR = GenerateRandomHexValues();
                uniqueIdentifiers.BodyMarkingColorG = GenerateRandomHexValues();
                uniqueIdentifiers.BodyMarkingColorB = GenerateRandomHexValues();

                // блок 37
                uniqueIdentifiers.BodyMarkingStyle = empty;
            }

            // Цвет маркировки хвоста (блоки 26-28)
            if (markingSet.TryGetCategory(MarkingCategories.Tail, out var tailMarkings))
            {
                var tailColor = GetFirstMarkingColor(tailMarkings);
                var tailColorArray = ConvertColorToHexArray(tailColor);
                uniqueIdentifiers.TailMarkingColorR = new[] { tailColorArray[0], tailColorArray[1], tailColorArray[2] };
                uniqueIdentifiers.TailMarkingColorG = new[] { tailColorArray[3], tailColorArray[4], tailColorArray[5] };
                uniqueIdentifiers.TailMarkingColorB = new[] { tailColorArray[6], tailColorArray[7], tailColorArray[8] };

                // блок 38
                var markingId = tailMarkings.FirstOrDefault()?.MarkingId;
                var markingPrototype = markingPrototypes
                    .FirstOrDefault(m => m.MarkingPrototypeId == markingId);

                uniqueIdentifiers.TailMarkingStyle = markingPrototype != null
                    ? markingPrototype.HexValue
                    : empty;
            }
            else
            {
                uniqueIdentifiers.TailMarkingColorR = GenerateRandomHexValues();
                uniqueIdentifiers.TailMarkingColorG = GenerateRandomHexValues();
                uniqueIdentifiers.TailMarkingColorB = GenerateRandomHexValues();

                // блок 38
                uniqueIdentifiers.TailMarkingStyle = empty;
            }

            // Цвет глаз (блоки 29-31)
            var eyeColorArray = ConvertColorToHexArray(humanoid.EyeColor);
            uniqueIdentifiers.EyeColorR = new[] { eyeColorArray[0], eyeColorArray[1], eyeColorArray[2] };
            uniqueIdentifiers.EyeColorG = new[] { eyeColorArray[3], eyeColorArray[4], eyeColorArray[5] };
            uniqueIdentifiers.EyeColorB = new[] { eyeColorArray[6], eyeColorArray[7], eyeColorArray[8] };

            // Пол (блок 32)
            uniqueIdentifiers.Gender = humanoid.Sex switch
            {
                Sex.Female => GenerateGenderBlock(Sex.Female),
                Sex.Male => GenerateGenderBlock(Sex.Male),
                Sex.Unsexed => GenerateGenderBlock(Sex.Unsexed),
                _ => GenerateRandomHexValues()
            };

            var species = _prototype.Index(humanoid.Species);
            var heightRange = GetHeightRange(species);
            var widthRange = GetWidthRange(species);
            uniqueIdentifiers.Height = EncodeRangedBlock(humanoid.Height, heightRange.Min, heightRange.Max);
            uniqueIdentifiers.Width = EncodeRangedBlock(humanoid.Width, widthRange.Min, widthRange.Max);

            component.UniqueIdentifiers = uniqueIdentifiers;
        }
        else
        {
            var empty = new[] { "0", "0", "0" };
            var uniqueIdentifiers = new UniqueIdentifiersData
            {
                ID = $"StructuralEnzymes{uid}",
                HairColorR = GenerateRandomHexValues(),
                HairColorG = GenerateRandomHexValues(),
                HairColorB = GenerateRandomHexValues(),
                SecondaryHairColorR = GenerateRandomHexValues(),
                SecondaryHairColorG = GenerateRandomHexValues(),
                SecondaryHairColorB = GenerateRandomHexValues(),
                BeardColorR = GenerateRandomHexValues(),
                BeardColorG = GenerateRandomHexValues(),
                BeardColorB = GenerateRandomHexValues(),
                SkinColorR = GenerateRandomHexValues(),
                SkinColorG = GenerateRandomHexValues(),
                SkinColorB = GenerateRandomHexValues(),
                Race = GenerateRandomHexValues(),
                HeadAccessoryColorR = GenerateRandomHexValues(),
                HeadAccessoryColorG = GenerateRandomHexValues(),
                HeadAccessoryColorB = GenerateRandomHexValues(),
                HeadMarkingColorR = GenerateRandomHexValues(),
                HeadMarkingColorG = GenerateRandomHexValues(),
                HeadMarkingColorB = GenerateRandomHexValues(),
                BodyMarkingColorR = GenerateRandomHexValues(),
                BodyMarkingColorG = GenerateRandomHexValues(),
                BodyMarkingColorB = GenerateRandomHexValues(),
                TailMarkingColorR = GenerateRandomHexValues(),
                TailMarkingColorG = GenerateRandomHexValues(),
                TailMarkingColorB = GenerateRandomHexValues(),
                EyeColorR = GenerateRandomHexValues(),
                EyeColorG = GenerateRandomHexValues(),
                EyeColorB = GenerateRandomHexValues(),
                Gender = _random.Next(0, 2) == 0
                    ? GenerateGenderBlock(Sex.Female)
                    : GenerateGenderBlock(Sex.Male),
                HairStyle = GenerateRandomHexValues(),
                BeardStyle = GenerateRandomHexValues(),
                HeadAccessoryStyle = empty,
                HeadMarkingStyle = empty,
                BodyMarkingStyle = empty,
                TailMarkingStyle = empty,
                Height = GenerateRandomHexValues(),
                Width = GenerateRandomHexValues()
            };

            component.UniqueIdentifiers = uniqueIdentifiers;
        }
    }
    #endregion

    #region Initialize S.E.
    private void InitializeStructuralEnzymes(EntityUid uid, DnaModifierComponent component)
    {
        var enzymesPrototypes = _enzymesIndexer.GetAllEnzymesPrototypes();
        var uniqueEnzymesPrototypes = new List<EnzymesPrototypeInfo>();
        foreach (var enzymePrototype in enzymesPrototypes)
        {
            var uniqueEnzyme = new EnzymesPrototypeInfo
            {
                EnzymesPrototypeId = enzymePrototype.EnzymesPrototypeId,
                Order = enzymePrototype.Order,
                HexCode = enzymePrototype.Order == 55
                    ? GenerateNeutralEvolutionHexCode(uid, component)
                    : GenerateHexCode()
            };

            uniqueEnzymesPrototypes.Add(uniqueEnzyme);
        }

        component.EnzymesPrototypes = uniqueEnzymesPrototypes;
    }

    private string[] GenerateHexCode()
    {
        var firstDigit = _random.Next(0, 3).ToString("X1");
        var secondDigit = _random.Next(0, 16).ToString("X1");
        var thirdDigit = _random.Next(0, 16).ToString("X1");

        return new[] { firstDigit, secondDigit, thirdDigit };
    }

    private string[] GenerateNeutralEvolutionHexCode(EntityUid uid, DnaModifierComponent component)
    {
        if (component.Upper != null && IsCurrentPrototype(uid, component.Upper.Value))
            return GenerateLastHexCode();

        return GenerateLowestHexCode();
    }

    private string[] GenerateLowestHexCode()
    {
        var firstDigit = _random.Next(0, 8).ToString("X1");
        var secondDigit = _random.Next(0, 16).ToString("X1");
        var thirdDigit = _random.Next(0, 16).ToString("X1");

        return new[] { firstDigit, secondDigit, thirdDigit };
    }

    private string[] GenerateLastHexCode()
    {
        var firstDigit = _random.Next(8, 16).ToString("X1");
        var secondDigit = _random.Next(0, 16).ToString("X1");
        var thirdDigit = _random.Next(0, 16).ToString("X1");

        return new[] { firstDigit, secondDigit, thirdDigit };
    }
    #endregion

    #region Instability
    private void UpdateInstability(EntityUid uid, DnaModifierComponent component, int totalInstability)
    {
        component.Instability = totalInstability;
        if (totalInstability <= 20)
        {
            if (HasComp<DnaInstabilityComponent>(uid))
                RemComp<DnaInstabilityComponent>(uid);
            return;
        }

        var instabilityComp = EnsureComp<DnaInstabilityComponent>(uid);
        switch (totalInstability)
        {
            case > 20 and <= 35:
                instabilityComp.Stage = 1;
                break;

            case > 35 and <= 65:
                instabilityComp.Stage = 2;
                break;

            case > 65:
                instabilityComp.Stage = 3;
                break;
        }

        Dirty(uid, component);
    }

    private void CheckDeviations(EntityUid uid, DnaModifierComponent component)
    {
        if (component.EnzymesPrototypes == null)
            return;

        int totalInstability = 0;
        foreach (var enzyme in component.EnzymesPrototypes)
        {
            if (!_prototype.TryIndex<StructuralEnzymesPrototype>(enzyme.EnzymesPrototypeId, out var enzymePrototype))
                continue;

            bool hasComponent = enzymePrototype.AddComponent != null && enzymePrototype.AddComponent
                .Any(componentEntry =>
                {
                    var componentType = componentEntry.Value.Component?.GetType();
                    return componentType != null && HasComp(uid, componentType);
                });

            if (hasComponent)
            {
                enzyme.HexCode = GetHexCodeForType(enzymePrototype.TypeDeviation);
                totalInstability += enzymePrototype.CostInstability;

                if (enzymePrototype.TypeDeviation != EnzymesType.Disease
                    && enzymePrototype.AddComponent != null)
                {
                    foreach (var componentEntry in enzymePrototype.AddComponent)
                    {
                        var componentType = componentEntry.Value.Component?.GetType();
                        if (componentType != null && HasComp(uid, componentType))
                            component.InitialAbilities.Add(componentType);
                    }
                }
            }
        }

        UpdateInstability(uid, component, totalInstability);
    }

    private string[] GetHexCodeForType(EnzymesType type)
    {
        int firstDigit;
        switch (type)
        {
            case EnzymesType.Disease:
            case EnzymesType.Minor:
                firstDigit = 9;
                break;

            case EnzymesType.Intermediate:
                firstDigit = 0xC;
                break;

            case EnzymesType.Base:
                firstDigit = 0xE;
                break;

            default:
                firstDigit = _random.Next(0, 16);
                break;
        }

        return new[]
        {
            firstDigit.ToString("X1"),
            _random.Next(0, 16).ToString("X1"),
            _random.Next(0, 16).ToString("X1")
        };
    }

    private string[] GetHexCodeDisease()
    {
        return new[]
        {
            _random.Next(9, 16).ToString("X1"),
            _random.Next(0, 16).ToString("X1"),
            _random.Next(2, 16).ToString("X1")
        };
    }

    private void InstabilityStageOne(EntityUid uid)
    {
        if (_random.NextFloat() < 0.05f)
        {
            var damage = new DamageSpecifier { DamageDict = { { "Heat", 2.5 } } };
            _damage.TryChangeDamage(uid, damage, true);

            _popup.PopupEntity(Loc.GetString("dna-instability-stage-one"), uid, uid, PopupType.SmallCaution);
        }
    }

    private void InstabilityStageTwo(EntityUid uid)
    {
        if (_random.NextFloat() < 0.25f)
        {
            var damage = new DamageSpecifier { DamageDict = { { "Heat", 2.5 }, { "Blunt", 10 }, { "Structural", 2 } } };

            _damage.TryChangeDamage(uid, damage, true);

            _chat.TryEmoteWithoutChat(uid, _prototype.Index(Scream), true);
            _popup.PopupEntity(Loc.GetString("dna-instability-stage-two"), uid, uid, PopupType.SmallCaution);
        }
    }

    private void InstabilityStageThree(EntityUid uid)
    {
        if (_random.NextFloat() < 0.5f)
        {
            var damage = new DamageSpecifier { DamageDict = { { "Heat", 5 }, { "Blunt", 50 }, { "Structural", 4 } } };

            _damage.TryChangeDamage(uid, damage, true);

            _chat.TryEmoteWithoutChat(uid, _prototype.Index(Scream), true);
            _popup.PopupEntity(Loc.GetString("dna-instability-stage-three"), uid, uid, PopupType.LargeCaution);
        }
    }
    #endregion

    public void ChangeDna(Entity<DnaModifierComponent> ent, EnzymeInfo enzyme)
    {
        if (enzyme.Identifier != null) ent.Comp.UniqueIdentifiers = enzyme.Identifier;
        if (enzyme.Info != null) ent.Comp.EnzymesPrototypes = enzyme.Info;

        Dirty(ent, ent.Comp);

        TryChangeUniqueIdentifiers(ent);
        TryChangeStructuralEnzymes(ent);
    }

    public bool TryApplyStoredDnaSample(Entity<DnaModifierComponent> ent, EnzymeInfo sample, bool stabilized = false)
    {
        if (!CanApplyStoredDnaSample(ent, sample.Identifier, true))
            return false;

        if (!TryStartDnaModificationCooldown(ent))
            return false;

        if (!stabilized && RequiresIdentityStabilizer(ent, sample.Identifier))
        {
            PopupDnaFailure(ent, "dna-modifier-fail-identity-stabilizer");
            return false;
        }

        _entitiesApplyingStoredDna.Add(ent);
        if (stabilized)
            _entitiesApplyingIdentityStabilizer.Add(ent);

        try
        {
            ChangeDna(ent, sample);
        }
        finally
        {
            _entitiesApplyingStoredDna.Remove(ent);
            _entitiesApplyingIdentityStabilizer.Remove(ent);
        }

        ApplyDnaInjectionDamage(ent);
        _admin.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(ent):target} was injected with a stored DNA sample. Stabilized: {stabilized}.");
        return true;
    }

    public bool TryChangeDnaWithEvolutionStabilizer(Entity<DnaModifierComponent> ent, int type, bool stabilized)
    {
        if (stabilized)
            _entitiesApplyingEvolutionStabilizer.Add(ent);

        try
        {
            ChangeDna(ent, type);
        }
        finally
        {
            _entitiesApplyingEvolutionStabilizer.Remove(ent);
        }

        return true;
    }

    public void BeginConsoleIrradiation(EntityUid target)
    {
        _entitiesReceivingConsoleRadiation.Add(target);
    }

    public void EndConsoleIrradiation(EntityUid target)
    {
        _entitiesReceivingConsoleRadiation.Remove(target);
    }

    public void ChangeDna(Entity<DnaModifierComponent> ent, int type)
    {
        switch (type)
        {
            case 0: TryChangeUniqueIdentifiers(ent); break;
            case 1: TryChangeStructuralEnzymes(ent); break;
        }
    }

    public void ChangeDna(Entity<DnaModifierComponent?> uid)
    {
        if (!Resolve(uid, ref uid.Comp) || _entitiesUndergoingDnaChange.Contains(uid))
            return;

        _entitiesUndergoingDnaChange.Add(uid);
        try
        {
            TryChangeUniqueIdentifiers((uid, uid.Comp));
            TryChangeStructuralEnzymes((uid, uid.Comp));
        }
        finally
        {
            _entitiesUndergoingDnaChange.Remove(uid);
        }
    }

    #region Modify U.I.

    private void TryChangeUniqueIdentifiers(Entity<DnaModifierComponent> ent, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(ent, ref humanoid) || ent.Comp.UniqueIdentifiers == null)
            return;

        var uniqueIdentifiers = ent.Comp.UniqueIdentifiers;
        var protectIdentity = HasProtectedIdentity(ent)
            && (!_entitiesApplyingStoredDna.Contains(ent) || !_entitiesApplyingIdentityStabilizer.Contains(ent));

        if (protectIdentity)
            PreserveProtectedIdentity((ent, humanoid), uniqueIdentifiers);
        else
            UpdateRace((ent, humanoid), uniqueIdentifiers);

        UpdateSkin((ent, humanoid), uniqueIdentifiers);
        UpdateMarkings((ent, humanoid), uniqueIdentifiers);
        UpdateEyeColor((ent, humanoid), uniqueIdentifiers);

        if (!protectIdentity)
            UpdateGender((ent, humanoid), uniqueIdentifiers);

        UpdateScale((ent, humanoid), uniqueIdentifiers);

        Dirty(ent, humanoid);
    }

    private void UpdateRace(Entity<HumanoidAppearanceComponent> humanoid, UniqueIdentifiersData uniqueIdentifiers)
    {
        if (!IsRoundStartSpecies(humanoid.Comp.Species))
        {
            uniqueIdentifiers.Race = Array.Empty<string>();
            return;
        }

        if (uniqueIdentifiers.Race.Length < 3)
        {
            if (IsRoundStartSpecies(humanoid.Comp.Species))
                uniqueIdentifiers.Race = GetSpeciesBlock(humanoid.Comp.Species);

            return;
        }

        var species = GetSpeciesFromBlock(uniqueIdentifiers.Race);
        if (species == null || humanoid.Comp.Species == species)
            return;

        _humanoidAppearance.SetSpecies(humanoid.Owner, species, humanoid: humanoid.Comp);
    }

    private void UpdateSkin(Entity<HumanoidAppearanceComponent> humanoid, UniqueIdentifiersData uniqueIdentifiers)
    {
        var speciesProto = _prototype.Index(humanoid.Comp.Species);
        EnsureSkinColorBlocks(uniqueIdentifiers);

        var newColor = ConvertRgbBlocksToColor(
            uniqueIdentifiers.SkinColorR,
            uniqueIdentifiers.SkinColorG,
            uniqueIdentifiers.SkinColorB);

        humanoid.Comp.SkinColor = SkinColor.ValidSkinTone(speciesProto.SkinColoration, newColor);
    }

    private void UpdateMarkings(Entity<HumanoidAppearanceComponent> humanoid, UniqueIdentifiersData uniqueIdentifiers)
    {
        var markingSet = humanoid.Comp.MarkingSet;
        var markingPrototypes = _markingIndexer.GetAllMarkingPrototypes();

        _ensureMarking.UpdateMarkingCategory(humanoid, markingSet, MarkingCategories.Hair, uniqueIdentifiers.HairColorR, uniqueIdentifiers.HairColorG, uniqueIdentifiers.HairColorB, uniqueIdentifiers.HairStyle, humanoid.Comp.Species, markingPrototypes, uniqueIdentifiers.SecondaryHairColorR, uniqueIdentifiers.SecondaryHairColorG, uniqueIdentifiers.SecondaryHairColorB);
        _ensureMarking.UpdateMarkingCategory(humanoid, markingSet, MarkingCategories.FacialHair, uniqueIdentifiers.BeardColorR, uniqueIdentifiers.BeardColorG, uniqueIdentifiers.BeardColorB, uniqueIdentifiers.BeardStyle, humanoid.Comp.Species, markingPrototypes);
        _ensureMarking.UpdateMarkingCategory(humanoid, markingSet, MarkingCategories.HeadTop, uniqueIdentifiers.HeadAccessoryColorR, uniqueIdentifiers.HeadAccessoryColorG, uniqueIdentifiers.HeadAccessoryColorB, uniqueIdentifiers.HeadAccessoryStyle, humanoid.Comp.Species, markingPrototypes);
        _ensureMarking.UpdateMarkingCategory(humanoid, markingSet, MarkingCategories.Head, uniqueIdentifiers.HeadMarkingColorR, uniqueIdentifiers.HeadMarkingColorG, uniqueIdentifiers.HeadMarkingColorB, uniqueIdentifiers.HeadMarkingStyle, humanoid.Comp.Species, markingPrototypes);
        _ensureMarking.UpdateMarkingCategory(humanoid, markingSet, MarkingCategories.Chest, uniqueIdentifiers.BodyMarkingColorR, uniqueIdentifiers.BodyMarkingColorG, uniqueIdentifiers.BodyMarkingColorB, uniqueIdentifiers.BodyMarkingStyle, humanoid.Comp.Species, markingPrototypes);
        _ensureMarking.UpdateMarkingCategory(humanoid, markingSet, MarkingCategories.Tail, uniqueIdentifiers.TailMarkingColorR, uniqueIdentifiers.TailMarkingColorG, uniqueIdentifiers.TailMarkingColorB, uniqueIdentifiers.TailMarkingStyle, humanoid.Comp.Species, markingPrototypes);
    }

    private void UpdateEyeColor(Entity<HumanoidAppearanceComponent> humanoid, UniqueIdentifiersData uniqueIdentifiers)
    {
        int red = ParseHexByte(uniqueIdentifiers.EyeColorR[0], uniqueIdentifiers.EyeColorR[1]);
        int green = ParseHexByte(uniqueIdentifiers.EyeColorG[0], uniqueIdentifiers.EyeColorG[1]);
        int blue = ParseHexByte(uniqueIdentifiers.EyeColorB[0], uniqueIdentifiers.EyeColorB[1]);

        float redNormalized = red / 255f;
        float greenNormalized = green / 255f;
        float blueNormalized = blue / 255f;

        var eyeColor = new Color(redNormalized, greenNormalized, blueNormalized);

        humanoid.Comp.EyeColor = eyeColor;
    }

    private void UpdateGender(Entity<HumanoidAppearanceComponent> humanoid, UniqueIdentifiersData uniqueIdentifiers)
    {
        if (uniqueIdentifiers.Gender.Length < 3)
        {
            uniqueIdentifiers.Gender = GenerateGenderBlock(humanoid.Comp.Sex);
            return;
        }

        int[] values = uniqueIdentifiers.Gender
            .Select(ParseHexDigit)
            .ToArray();

        var currentGender = (values[0], values[1], values[2]) switch
        {
            ( <= 0x5, <= 0x7, <= 0x3) => Gender.Female,
            ( < 0x8, <= 0x7, >= 0x4 and < 0x9) => Gender.Male,
            ( >= 0x8, >= 0x7, >= 0x9) => Gender.Neuter,
            _ => Gender.Neuter
        };

        var currentSex = (values[0], values[1], values[2]) switch
        {
            ( <= 0x5, <= 0x7, <= 0x3) => Sex.Female,
            ( < 0x8, <= 0x7, >= 0x4 and < 0x9) => Sex.Male,
            ( >= 0x8, >= 0x7, >= 0x9) => Sex.Unsexed,
            _ => Sex.Unsexed
        };

        humanoid.Comp.Gender = currentGender;
        humanoid.Comp.Sex = currentSex;
    }

    private void UpdateScale(Entity<HumanoidAppearanceComponent> humanoid, UniqueIdentifiersData uniqueIdentifiers)
    {
        var species = _prototype.Index(humanoid.Comp.Species);
        var heightRange = GetHeightRange(species);
        var widthRange = GetWidthRange(species);

        if (uniqueIdentifiers.Height.Length < 3)
            uniqueIdentifiers.Height = EncodeRangedBlock(humanoid.Comp.Height, heightRange.Min, heightRange.Max);

        if (uniqueIdentifiers.Width.Length < 3)
            uniqueIdentifiers.Width = EncodeRangedBlock(humanoid.Comp.Width, widthRange.Min, widthRange.Max);

        var height = DecodeRangedBlock(uniqueIdentifiers.Height, heightRange.Min, heightRange.Max, humanoid.Comp.Height);
        var width = DecodeRangedBlock(uniqueIdentifiers.Width, widthRange.Min, widthRange.Max, humanoid.Comp.Width);

        if (!species.ScaleHeight)
            height = 1f;

        if (!species.ScaleWidth)
            width = 1f;

        _heightAdjust.SetScale(humanoid.Owner, new Vector2(width, height));
    }
    #endregion Modify U.I.

    #region Modify S.E.
    private void TryChangeStructuralEnzymes(Entity<DnaModifierComponent> ent)
    {
        if (ent.Comp.EnzymesPrototypes == null)
            return;

        int totalInstability = 0;
        var enzymes = ent.Comp.EnzymesPrototypes;
        var messagesToShow = new List<string>();
        foreach (var enzyme in enzymes)
        {
            if (enzyme.Order == 55)
            {
                TryChangeLastBlock(ent, ent.Comp, enzyme);
                continue;
            }

            if (!_prototype.TryIndex<StructuralEnzymesPrototype>(enzyme.EnzymesPrototypeId, out var enzymePrototype))
                continue;

            bool meetsCondition = CheckHexCodeCondition(enzyme.HexCode, enzymePrototype.TypeDeviation);
            if (enzymePrototype.AddComponent != null)
            {
                if (meetsCondition)
                {
                    bool hasAnyComponent = enzymePrototype.AddComponent
                        .Any(componentEntry =>
                        {
                            var componentType = componentEntry.Value.Component?.GetType();
                            return componentType != null && HasComp(ent, componentType);
                        });

                    if (!hasAnyComponent && _random.NextFloat() <= enzymePrototype.ChanceAssimilation)
                    {
                        EntityManager.AddComponents(ent, enzymePrototype.AddComponent, false);
                        totalInstability += enzymePrototype.CostInstability;

                        if (!string.IsNullOrEmpty(enzymePrototype.Message))
                            messagesToShow.Add(enzymePrototype.Message);

                        _admin.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(ent):user} acquires a gene type: '{enzymePrototype.ID}'.");
                    }
                    else if (hasAnyComponent)
                    {
                        totalInstability += enzymePrototype.CostInstability;
                    }
                }
                else
                {
                    foreach (var componentEntry in enzymePrototype.AddComponent)
                    {
                        var componentType = componentEntry.Value.Component?.GetType();
                        if (componentType != null && HasComp(ent, componentType)
                            && !ent.Comp.InitialAbilities.Contains(componentType))
                        {
                            RemComp(ent, componentType);

                            _admin.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(ent):user} loses the gene type: '{enzymePrototype.ID}'.");
                        }
                    }
                }
            }
        }

        UpdateInstability(ent, ent.Comp, totalInstability);
        if (messagesToShow.Count > 0)
        {
            _ = ShowMessagesWithDelay(ent, messagesToShow);
        }
    }

    private void TryChangeLastBlock(EntityUid target, DnaModifierComponent component, EnzymesPrototypeInfo enzyme)
    {
        int hexValue = ParseHexDigit(enzyme.HexCode[0]);
        if (TryComp<DnaLowestComponent>(target, out var dnaLowest))
        {
            if (hexValue < 8 || dnaLowest.Parent == null)
                return;

            if (RequiresEvolutionStabilizer(target) && !_entitiesApplyingEvolutionStabilizer.Contains(target))
            {
                PopupDnaFailure(target, "dna-modifier-fail-evolution-stabilizer");
                return;
            }

            var parent = dnaLowest.Parent.Value;
            DropInventoryAndHands(target);

            if (_mindSystem.TryGetMind(target, out var mindIdLowest, out var mindLowest))
                _mindSystem.TransferTo(mindIdLowest, parent, mind: mindLowest);

            if (TryComp<DamageableComponent>(parent, out var damageParent)
                && _mobThreshold.GetScaledDamage(target, parent, out var damage, out _) && damage != null)
            {
                _damage.SetDamage(parent, damageParent, damage);
            }

            if (TryComp<DnaModifierComponent>(parent, out var dnaModifier))
            {
                dnaModifier.UniqueIdentifiers = CloneUniqueIdentifiersForTarget(component.UniqueIdentifiers, parent);
                dnaModifier.EnzymesPrototypes = component.EnzymesPrototypes?.ToList();
                dnaModifier.Instability = component.Instability;

                Dirty(parent, dnaModifier);
                ChangeDnaWithStoredSampleContext(target, (parent, dnaModifier));
            }

            ApplyEvolutionCost(parent);

            var parentXform = Transform(parent);
            _transform.SetCoordinates(parent, parentXform, Transform(target).Coordinates);
            _transform.AttachToGridOrMap(parent, parentXform);

            _entManager.DeleteEntity(target);
            _admin.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(target):target} evolved back into {ToPrettyString(parent):parent} through DNA block 55.");
            return;
        }

        if (hexValue < 8)
        {
            if (string.IsNullOrEmpty(component.Lowest) || IsCurrentPrototype(target, component.Lowest.Value))
                return;

            if (!HasComp<HumanoidAppearanceComponent>(target))
                return;

            if (RequiresEvolutionStabilizer(target) && !_entitiesApplyingEvolutionStabilizer.Contains(target))
            {
                PopupDnaFailure(target, "dna-modifier-fail-evolution-stabilizer");
                return;
            }

            // Degrade into the configured lower form and leave all carried items behind.
            _buckle.TryUnbuckle(target, target, true);
            var child = _entManager.SpawnEntity(component.Lowest, Transform(target).Coordinates);
            PreserveNpcState(target, child);
            if (TryComp<DamageableComponent>(child, out var damageParent)
                && _mobThreshold.GetScaledDamage(target, child, out var damage, out _) && damage != null)
            {
                _damage.SetDamage(child, damageParent, damage);
            }

            EnsureComp<DnaLowestComponent>(child).Parent = target;

            DropInventoryAndHands(target);

            // Copy identity and genetics before applying DNA visuals to the new body.
            if (TryComp(target, out MetaDataComponent? targetMeta))
                _metaData.SetEntityName(child, targetMeta.EntityName);

            if (_mindSystem.TryGetMind(target, out var mindId, out var mind))
                _mindSystem.TransferTo(mindId, child, mind: mind);

            if (TryComp(target, out DnaComponent? targetDna))
                EnsureComp<DnaComponent>(child).DNA = targetDna.DNA;

            var childDnaModifier = EnsureComp<DnaModifierComponent>(child);
            childDnaModifier.UniqueIdentifiers = CloneUniqueIdentifiersForTarget(component.UniqueIdentifiers, child);
            childDnaModifier.EnzymesPrototypes = component.EnzymesPrototypes?.ToList();
            childDnaModifier.Instability = component.Instability;
            EnsureEvolutionEndpoints(childDnaModifier, component);

            Dirty(child, childDnaModifier);
            ChangeDnaWithStoredSampleContext(target, (child, childDnaModifier));
            ApplyEvolutionCost(child);

            _admin.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(target):user} gene down up a step.");

            // Keep the original body off-map so the lower form can evolve back into it.
            EnsurePausedMap();
            if (PausedMap != null)
            {
                _transform.SetParent(target, Transform(target), PausedMap.Value);
            }
        }
        else
        {
            if (string.IsNullOrEmpty(component.Upper) || IsCurrentPrototype(target, component.Upper.Value))
                return;

            if (RequiresEvolutionStabilizer(target) && !_entitiesApplyingEvolutionStabilizer.Contains(target))
            {
                PopupDnaFailure(target, "dna-modifier-fail-evolution-stabilizer");
                return;
            }

            // Evolve into the configured upper form and leave all carried items behind.
            _buckle.TryUnbuckle(target, target, true);
            var child = _entManager.SpawnEntity(component.Upper, Transform(target).Coordinates);
            PreserveNpcState(target, child);
            if (TryComp<DamageableComponent>(child, out var parentDamage)
                && _mobThreshold.GetScaledDamage(target, child, out var damageLowest, out _) && damageLowest != null)
            {
                _damage.SetDamage(child, parentDamage, damageLowest);
            }
            DropInventoryAndHands(target);

            // Copy identity and genetics before applying DNA visuals to the new body.
            if (TryComp(target, out MetaDataComponent? targetMeta))
                _metaData.SetEntityName(child, targetMeta.EntityName);

            if (_mindSystem.TryGetMind(target, out var mindId, out var mind))
                _mindSystem.TransferTo(mindId, child, mind: mind);

            if (TryComp(target, out DnaComponent? targetDna))
                EnsureComp<DnaComponent>(child).DNA = targetDna.DNA;

            EnsureComp<DnaModifiedComponent>(child);

            var childDnaModifier = EnsureComp<DnaModifierComponent>(child);
            childDnaModifier.UniqueIdentifiers = CloneUniqueIdentifiersForTarget(component.UniqueIdentifiers, child);
            childDnaModifier.EnzymesPrototypes = component.EnzymesPrototypes?.ToList();
            childDnaModifier.Instability = component.Instability;
            EnsureEvolutionEndpoints(childDnaModifier, component);

            Dirty(child, childDnaModifier);
            ChangeDnaWithStoredSampleContext(target, (child, childDnaModifier));
            ApplyEvolutionCost(child);

            _admin.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(target):user} gene went up a step.");

            // Remove the replaced lower body.
            _entManager.DeleteEntity(target); // Bye
        }
    }

    private bool IsCurrentPrototype(EntityUid target, EntProtoId prototype)
    {
        return TryComp<MetaDataComponent>(target, out var meta)
            && meta.EntityPrototype?.ID == prototype.Id;
    }

    private void EnsureEvolutionEndpoints(DnaModifierComponent target, DnaModifierComponent source)
    {
        target.Upper ??= source.Upper;
        target.Lowest ??= source.Lowest;
    }

    private void ChangeDnaWithStoredSampleContext(EntityUid source, Entity<DnaModifierComponent?> target)
    {
        if (!_entitiesApplyingStoredDna.Contains(source))
        {
            ChangeDna(target);
            return;
        }

        _entitiesApplyingStoredDna.Add(target);
        if (_entitiesApplyingIdentityStabilizer.Contains(source))
            _entitiesApplyingIdentityStabilizer.Add(target);

        if (_entitiesApplyingEvolutionStabilizer.Contains(source))
            _entitiesApplyingEvolutionStabilizer.Add(target);

        try
        {
            ChangeDna(target);
        }
        finally
        {
            _entitiesApplyingStoredDna.Remove(target);
            _entitiesApplyingIdentityStabilizer.Remove(target);
            _entitiesApplyingEvolutionStabilizer.Remove(target);
        }
    }

    private bool TryStartDnaModificationCooldown(EntityUid target, EntityUid? user = null)
    {
        if (TryComp<DnaRecentlyModifiedComponent>(target, out var cooldown)
            && _timing.CurTime < cooldown.ExpiresAt)
        {
            PopupDnaFailure(target, "dna-modifier-fail-cooldown", user);
            return false;
        }

        var newCooldown = EnsureComp<DnaRecentlyModifiedComponent>(target);
        newCooldown.ExpiresAt = _timing.CurTime + DnaModificationCooldown;
        Dirty(target, newCooldown);
        return true;
    }

    private bool CanApplyStoredDnaSample(EntityUid target, UniqueIdentifiersData? identifiers, bool showPopup = false, EntityUid? user = null)
    {
        if (!TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
        {
            if (showPopup)
                PopupDnaFailure(target, "dna-modifier-fail-incompatible", user);

            return false;
        }

        if (!IsRoundStartSpecies(humanoid.Species))
        {
            if (showPopup)
                PopupDnaFailure(target, "dna-modifier-fail-roundstart", user);

            return false;
        }

        if (identifiers == null)
            return true;

        if (identifiers.Race.Length < 3)
        {
            if (showPopup)
                PopupDnaFailure(target, "dna-modifier-fail-bad-sample", user);

            return false;
        }

        var sampleSpecies = GetSpeciesFromBlock(identifiers.Race);
        var valid = sampleSpecies != null && IsRoundStartSpecies(sampleSpecies);
        if (!valid && showPopup)
            PopupDnaFailure(target, "dna-modifier-fail-bad-sample", user);

        return valid;
    }

    private void ApplyDnaInjectionDamage(EntityUid target)
    {
        var damage = new DamageSpecifier { DamageDict = { { DnaInjectionDamage, 50 } } };
        _damage.TryChangeDamage(target, damage, true);
    }

    private bool HasProtectedIdentity(EntityUid target)
    {
        return _mindSystem.TryGetMind(target, out _, out _)
            || TryComp<ActorComponent>(target, out _);
    }

    public bool RequiresIdentityStabilizer(EntityUid target, UniqueIdentifiersData? identifiers)
    {
        if (identifiers == null || !HasProtectedIdentity(target))
            return false;

        if (!TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
            return false;

        if (identifiers.Race.Length >= 3)
        {
            var species = GetSpeciesFromBlock(identifiers.Race);
            if (species != null && humanoid.Species != species)
                return true;
        }

        if (identifiers.Gender.Length >= 3)
        {
            var targetSex = GetSexFromGenderBlock(identifiers.Gender);
            if (targetSex != null && humanoid.Sex != targetSex)
                return true;
        }

        return false;
    }

    private Sex? GetSexFromGenderBlock(string[] gender)
    {
        if (gender.Length < 3)
            return null;

        var values = gender
            .Select(ParseHexDigit)
            .ToArray();

        return (values[0], values[1], values[2]) switch
        {
            ( <= 0x5, <= 0x7, <= 0x3) => Sex.Female,
            ( < 0x8, <= 0x7, >= 0x4 and < 0x9) => Sex.Male,
            ( >= 0x8, >= 0x7, >= 0x9) => Sex.Unsexed,
            _ => Sex.Unsexed
        };
    }

    private bool RequiresEvolutionStabilizer(EntityUid target)
    {
        if (!TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
            return HasProtectedIdentity(target);

        return IsRoundStartSpecies(humanoid.Species);
    }

    private void PreserveProtectedIdentity(Entity<HumanoidAppearanceComponent> humanoid, UniqueIdentifiersData uniqueIdentifiers)
    {
        uniqueIdentifiers.Race = GetSpeciesBlockOrEmpty(humanoid.Comp.Species);
        uniqueIdentifiers.Gender = GenerateGenderBlock(humanoid.Comp.Sex);
    }

    private void ApplyEvolutionCost(EntityUid target)
    {
        var damage = new DamageSpecifier { DamageDict = { { DnaInjectionDamage, 25 } } };
        _damage.TryChangeDamage(target, damage, true);
        _stun.TryKnockdown(target, EvolutionKnockdownTime, true);
    }

    private void PopupDnaFailure(EntityUid target, string locId, EntityUid? user = null)
    {
        _popup.PopupEntity(Loc.GetString(locId), target, user ?? target, PopupType.MediumCaution);
    }

    private void PreserveNpcState(EntityUid source, EntityUid target)
    {
        if (TryComp<HTNComponent>(source, out var sourceHtn))
        {
            var targetHtn = EnsureComp<HTNComponent>(target);
            targetHtn.RootTask = new HTNCompoundTask { Task = sourceHtn.RootTask.Task };
            targetHtn.Blackboard = sourceHtn.Blackboard.ShallowClone();
            targetHtn.Blackboard.SetValue(NPCBlackboard.Owner, target);
            targetHtn.CheckServices = sourceHtn.CheckServices;
            targetHtn.PlanCooldown = sourceHtn.PlanCooldown;
            targetHtn.ConstantlyReplan = sourceHtn.ConstantlyReplan;
            targetHtn.Enabled = sourceHtn.Enabled;
        }

        if (TryComp<NpcFactionMemberComponent>(source, out var sourceFactions))
        {
            _npcFaction.ClearFactions((target, null), false);
            _npcFaction.ClearFriendlyFactions((target, null), false);
            _npcFaction.ClearHostileFactions((target, null), false);
            _npcFaction.AddFactions((target, null), sourceFactions.Factions, false);

            if (sourceFactions.AddFriendlyFactions != null)
            {
                foreach (var faction in sourceFactions.AddFriendlyFactions)
                    _npcFaction.AddFriendlyFaction((target, null), faction, false);
            }

            if (sourceFactions.AddHostileFactions != null)
            {
                foreach (var faction in sourceFactions.AddHostileFactions)
                    _npcFaction.AddHostileFaction((target, null), faction, false);
            }

            _npcFaction.RefreshFactionCache((target, null));
        }

        if (HasComp<ActiveNPCComponent>(source))
            EnsureComp<ActiveNPCComponent>(target);
    }

    private UniqueIdentifiersData? CloneUniqueIdentifiersForTarget(UniqueIdentifiersData? source, EntityUid target)
    {
        var identifiers = CloneUniqueIdentifiers(source);
        if (identifiers == null)
            return null;

        if (TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
            identifiers.Race = GetSpeciesBlockOrEmpty(humanoid.Species);

        return identifiers;
    }

    private string[] GenerateGenderBlock(Sex sex)
    {
        return sex switch
        {
            Sex.Female => GenerateTripleHexValues(0x0, 0x6, 0x0, 0x8, 0x0, 0x4),
            Sex.Male => GenerateTripleHexValues(0x0, 0x8, 0x0, 0x8, 0x4, 0x9),
            Sex.Unsexed => GenerateTripleHexValues(0x8, 0x10, 0x7, 0x10, 0x9, 0x10),
            _ => GenerateRandomHexValues()
        };
    }

    private string[] EncodeRangedBlock(float value, float min, float max)
    {
        if (max <= min)
            return new[] { "0", "0", "0" };

        var normalized = Math.Clamp((value - min) / (max - min), 0f, 1f);
        var encoded = (int)MathF.Round(normalized * 0xFFF);
        var hex = encoded.ToString("X3");

        return new[]
        {
            hex[0].ToString(),
            hex[1].ToString(),
            hex[2].ToString()
        };
    }

    private float DecodeRangedBlock(string[] block, float min, float max, float fallback)
    {
        if (max <= min || block.Length < 3)
            return Math.Clamp(fallback, min, max);

        var normalized = Math.Clamp(ParseHexBlock(block) / (float)0xFFF, 0f, 1f);
        return Math.Clamp(min + (max - min) * normalized, min, max);
    }

    private (float Min, float Max) GetHeightRange(SpeciesPrototype species)
    {
        return (species.MinHeight, species.MaxHeight);
    }

    private (float Min, float Max) GetWidthRange(SpeciesPrototype species)
    {
        return (species.MinWidth, species.MaxWidth);
    }

    private string[] GetSpeciesBlockOrEmpty(ProtoId<SpeciesPrototype> species)
    {
        return IsRoundStartSpecies(species)
            ? GetSpeciesBlock(species)
            : Array.Empty<string>();
    }

    private string[] GetSpeciesBlock(ProtoId<SpeciesPrototype> species)
    {
        var index = GetSpeciesIndex(species.Id);
        var hex = Math.Clamp(index, 0, 0xFFF).ToString("X3");

        return new[]
        {
            hex[0].ToString(),
            hex[1].ToString(),
            hex[2].ToString()
        };
    }

    private string? GetSpeciesFromBlock(string[] race)
    {
        var species = GetOrderedSpecies();
        if (species.Count == 0)
            return null;

        var index = Math.Clamp(ParseHexBlock(race), 0, species.Count - 1);
        return species[index].ID;
    }

    private int GetSpeciesIndex(string species)
    {
        var allSpecies = GetOrderedSpecies();
        for (var i = 0; i < allSpecies.Count; i++)
        {
            if (allSpecies[i].ID == species)
                return i;
        }

        return 0;
    }

    private bool IsRoundStartSpecies(ProtoId<SpeciesPrototype> species)
    {
        return _prototype.TryIndex<SpeciesPrototype>(species, out var prototype)
            && prototype.RoundStart;
    }

    private List<SpeciesPrototype> GetOrderedSpecies()
    {
        return _prototype.EnumeratePrototypes<SpeciesPrototype>()
            .Where(species => species.RoundStart)
            .OrderBy(species => species.ID)
            .ToList();
    }

    private void DropInventoryAndHands(EntityUid target)
    {
        if (_inventory.TryGetContainerSlotEnumerator(target, out var enumerator))
        {
            var slots = new List<string>();
            while (enumerator.MoveNext(out var slot))
            {
                slots.Add(slot.ID);
            }

            foreach (var slot in slots)
            {
                _inventory.TryUnequip(target, slot, true, true);
            }
        }

        foreach (var held in _hands.EnumerateHeld(target).ToList())
        {
            _hands.TryDrop(target, held);
        }
    }

    private async Task ShowMessagesWithDelay(EntityUid target, List<string> messages)
    {
        if (!TryComp<ActorComponent>(target, out var actor))
            return;

        foreach (var message in messages)
        {
            _prayerSystem.SendSubtleMessage(actor.PlayerSession, actor.PlayerSession, string.Empty, Loc.GetString(message));
            await Task.Delay(2000);
        }
    }

    private bool CheckHexCodeCondition(string[] hexCode, EnzymesType type)
    {
        var value = GetHexBlockValue(hexCode);

        switch (type)
        {
            case EnzymesType.Disease:
            case EnzymesType.Minor:
                return value >= 0x802;

            case EnzymesType.Intermediate:
                return value >= 0xBEA;

            case EnzymesType.Base:
                return value >= 0xDAC;

            default: return false;
        }
    }

    private int GetHexBlockValue(string[] hexCode)
    {
        if (hexCode.Length < 3)
            return 0;

        return ParseHexBlock(hexCode);
    }
    #endregion Modify S.E.

    #region Chemistry
    private void OnTryCureDnaDisease(EntityUid uid, DnaModifierComponent component, CureDnaDiseaseAttemptEvent args)
    {
        if (component.EnzymesPrototypes == null)
            return;

        foreach (var enzyme in component.EnzymesPrototypes)
        {
            if (!_prototype.TryIndex<StructuralEnzymesPrototype>(enzyme.EnzymesPrototypeId, out var enzymePrototype))
                continue;

            if (enzymePrototype.TypeDeviation == EnzymesType.Disease)
            {
                if (CheckHexCodeCondition(enzyme.HexCode, EnzymesType.Disease)
                    && _random.Prob(args.CureChance))
                {
                    enzyme.HexCode = GenerateHexCode();
                }
            }
        }

        TryChangeStructuralEnzymes((uid, component));

        Dirty(uid, component);
    }

    private void OnTryMutateDna(EntityUid uid, DnaModifierComponent component, MutateDnaAttemptEvent args)
    {
        if (component.EnzymesPrototypes == null)
            return;

        foreach (var enzyme in component.EnzymesPrototypes)
        {
            if (enzyme.Order == 55)
            {
                enzyme.HexCode = GenerateNeutralEvolutionHexCode(uid, component);
                continue;
            }

            if (!_prototype.TryIndex<StructuralEnzymesPrototype>(enzyme.EnzymesPrototypeId, out var enzymePrototype))
                continue;

            if (enzymePrototype.TypeDeviation == EnzymesType.Disease)
            {
                enzyme.HexCode = GetHexCodeDisease();
            }
        }

        TryChangeStructuralEnzymes((uid, component));

        Dirty(uid, component);
    }
    #endregion

    private void OnDamageChanged(EntityUid uid, DnaModifierComponent component, DamageChangedEvent args)
    {
        if (args.DamageDelta == null || !args.DamageIncreased || !args.DamageDelta.DamageDict.ContainsKey("Radiation"))
            return;

        if (_entitiesReceivingConsoleRadiation.Contains(uid))
            return;

        var radiationDamage = args.DamageDelta.DamageDict["Radiation"];
        if (radiationDamage < 1f)
            return;

        if (component.EnzymesPrototypes == null)
            return;

        if (!_cfg.GetCVar(CCVars.RadiationEnableMutations))
            return;

        var mutationStrength = radiationDamage.Float() * MathF.Max(0f, _cfg.GetCVar(CCVars.RadiationMutationStrengthModifier));
        if (mutationStrength < 1f)
            return;

        var mutationChance = Math.Clamp(0.05f * mutationStrength, 0f, 1f);
        if (_random.Prob(mutationChance))
        {
            // Ambient radiation mutates only disease structural enzymes. Race, height and width
            // blocks stay under explicit console/sample control so passive radiation cannot reshape identity.
            var countToModify = Math.Clamp((int) MathF.Floor(mutationStrength), 1, 10);

            var diseaseEnzymes = component.EnzymesPrototypes
                .Where(enzyme =>
                {
                    if (!_prototype.TryIndex<StructuralEnzymesPrototype>(enzyme.EnzymesPrototypeId, out var enzymePrototype))
                        return false;

                    return enzymePrototype.TypeDeviation == EnzymesType.Disease;
                })
                .ToList();

            var enzymesToModify = diseaseEnzymes
                .OrderBy(_ => _random.Next())
                .Take(countToModify)
                .ToList();

            foreach (var enzyme in enzymesToModify)
            {
                enzyme.HexCode = GetHexCodeDisease();
            }

            TryChangeStructuralEnzymes((uid, component));

            Dirty(uid, component);
        }
    }
}
