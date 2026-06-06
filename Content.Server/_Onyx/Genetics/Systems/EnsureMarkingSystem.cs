using System.Linq;
using Content.Server.Humanoid;
using Content.Shared.Genetics;
using Content.Shared.Genetics.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;

namespace Content.Server.Genetics.System;

public sealed class EnsureMarkingSystem : EntitySystem
{
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;

    public static readonly ProtoId<MarkingPrototype> DefaultHorns = "LizardHornsDemonic";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EnsureHornsGenComponent, ComponentInit>(OnHornsInit);
        SubscribeLocalEvent<EnsureHornsGenComponent, ComponentShutdown>(OnHornsShutdown);
    }

    private void OnHornsInit(Entity<EnsureHornsGenComponent> ent, ref ComponentInit args)
    {
        if (TryComp<HumanoidAppearanceComponent>(ent, out _))
            _humanoid.AddMarking(ent, DefaultHorns, Color.Black);
    }

    private void OnHornsShutdown(Entity<EnsureHornsGenComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<HumanoidAppearanceComponent>(ent, out _))
            _humanoid.RemoveMarking(ent, DefaultHorns);
    }

    public void UpdateMarkingCategory(
        Entity<HumanoidAppearanceComponent> humanoid,
        MarkingSet markingSet,
        MarkingCategories category,
        string[] colorR, string[] colorG, string[] colorB,
        string[] style, string species,
        List<MarkingPrototypeInfo> markingPrototypes,
        string[]? secondaryColorR = null,
        string[]? secondaryColorG = null,
        string[]? secondaryColorB = null)
    {
        markingSet.RemoveCategory(category);
        if (style.All(c => c == "0"))
            return;

        if (category == MarkingCategories.HeadTop && HasComp<EnsureHornsGenComponent>(humanoid))
        {
            _humanoid.AddMarking(humanoid, DefaultHorns, Color.Black);
            return;
        }

        var bestMatch = FindBestMatchingMarking(style, species, markingPrototypes);
        if (bestMatch == null)
            return;

        int red = ParseHexByte(colorR[0], colorR[1]);
        int green = ParseHexByte(colorG[0], colorG[1]);
        int blue = ParseHexByte(colorB[0], colorB[1]);

        var mainColor = new Color(red / 255f, green / 255f, blue / 255f);

        var colors = new List<Color> { mainColor };

        if (category == MarkingCategories.Hair &&
            secondaryColorR != null &&
            secondaryColorG != null &&
            secondaryColorB != null)
        {
            int secondaryRed = ParseHexByte(secondaryColorR[0], secondaryColorR[1]);
            int secondaryGreen = ParseHexByte(secondaryColorG[0], secondaryColorG[1]);
            int secondaryBlue = ParseHexByte(secondaryColorB[0], secondaryColorB[1]);

            var secondaryColor = new Color(secondaryRed / 255f, secondaryGreen / 255f, secondaryBlue / 255f);
            colors.Add(secondaryColor);
        }

        _humanoid.AddMarkingWithColors(humanoid, bestMatch.MarkingPrototypeId, colors);
    }

    private MarkingPrototypeInfo? FindBestMatchingMarking(string[] style, string species, List<MarkingPrototypeInfo> markingPrototypes)
    {
        MarkingPrototypeInfo? bestMatch = null;
        int bestScore = int.MaxValue;

        foreach (var marking in markingPrototypes)
        {
            if (!string.IsNullOrEmpty(marking.Species) && !marking.Species.Contains(species))
                continue;

            int score = CalculateStyleMatchScore(marking.HexValue, style);
            if (score < bestScore)
            {
                bestScore = score;
                bestMatch = marking;
            }
        }

        return bestMatch;
    }

    private int CalculateStyleMatchScore(string[] markingStyle, string[] targetStyle)
    {
        int score = 0;
        for (int i = 0; i < markingStyle.Length; i++)
        {
            if (i >= targetStyle.Length)
                break;

            int markingValue = ParseHexDigit(markingStyle[i]);
            int targetValue = ParseHexDigit(targetStyle[i]);
            score += Math.Abs(markingValue - targetValue);
        }

        return score;
    }

    private static int ParseHexByte(string high, string low)
    {
        return (ParseHexDigit(high) << 4) | ParseHexDigit(low);
    }

    private static int ParseHexDigit(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        var digit = value[0];
        if (digit >= '0' && digit <= '9')
            return digit - '0';

        if (digit >= 'A' && digit <= 'F')
            return digit - 'A' + 10;

        if (digit >= 'a' && digit <= 'f')
            return digit - 'a' + 10;

        return 0;
    }
}
