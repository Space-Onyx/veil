using System.Diagnostics.CodeAnalysis;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Random;

namespace Content.Shared.Genetics.Systems;

public abstract partial class SharedDnaModifierSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public void TrySaveInDisk(EntityUid disk, EnzymeInfo enzyme)
    {
        if (!TryComp(disk, out DnaModifierDiskComponent? comp))
            return;

        if (comp.Data != null)
            return;

        comp.Data = (EnzymeInfo)enzyme.Clone();
        if (TryComp(disk, out MetaDataComponent? meta))
            _metaData.SetEntityName(disk, Loc.GetString("dna-disk-name") + " " + $"({enzyme.SampleName})");

        Dirty(disk, comp);
        return;
    }

    public bool TryGetDataFromDisk(EntityUid disk, [NotNullWhen(true)] out EnzymeInfo? data)
    {
        data = null;
        if (!TryComp(disk, out DnaModifierDiskComponent? comp))
            return false;

        if (comp.Data == null)
            return false;

        data = comp.Data;
        return true;
    }

    public bool TryClearDiskData(EntityUid disk)
    {
        if (!TryComp(disk, out DnaModifierDiskComponent? comp))
            return false;

        if (comp.Data == null)
            return false;

        comp.Data = null;
        return true;
    }

    public Color GetFirstMarkingColor(IReadOnlyList<Marking> markings)
    {
        if (markings.Count > 0 && markings[0].MarkingColors.Count > 0)
        {
            return markings[0].MarkingColors[0];
        }
        return Color.White;
    }

    public string[] ConvertColorToHexArray(Color color)
    {
        int r = (int)(color.R * 255);
        int g = (int)(color.G * 255);
        int b = (int)(color.B * 255);

        string rHex = r.ToString("X2");
        string gHex = g.ToString("X2");
        string bHex = b.ToString("X2");

        return new[]
        {
            rHex[0].ToString(),
            rHex[1].ToString(),
            "0",
            gHex[0].ToString(),
            gHex[1].ToString(),
            "0",
            bHex[0].ToString(),
            bHex[1].ToString(),
            "0"
        };
    }

    public (string[] R, string[] G, string[] B) ConvertColorToRgbBlocks(Color color)
    {
        // Each RGB channel is stored as one three-subblock genetics block.
        var colorArray = ConvertColorToHexArray(color);
        return (
            new[] { colorArray[0], colorArray[1], colorArray[2] },
            new[] { colorArray[3], colorArray[4], colorArray[5] },
            new[] { colorArray[6], colorArray[7], colorArray[8] }
        );
    }

    public Color ConvertRgbBlocksToColor(string[] redBlock, string[] greenBlock, string[] blueBlock)
    {
        if (redBlock.Length < 2 || greenBlock.Length < 2 || blueBlock.Length < 2)
            return Color.White;

        var red = ParseHexByte(redBlock[0], redBlock[1]);
        var green = ParseHexByte(greenBlock[0], greenBlock[1]);
        var blue = ParseHexByte(blueBlock[0], blueBlock[1]);

        return new Color(red / 255f, green / 255f, blue / 255f);
    }

    public void EnsureSkinColorBlocks(UniqueIdentifiersData uniqueIdentifiers)
    {
        if (HasRgbBlocks(uniqueIdentifiers.SkinColorR, uniqueIdentifiers.SkinColorG, uniqueIdentifiers.SkinColorB))
            return;

        var (r, g, b) = ConvertColorToRgbBlocks(Color.White);
        uniqueIdentifiers.SkinColorR = r;
        uniqueIdentifiers.SkinColorG = g;
        uniqueIdentifiers.SkinColorB = b;
    }

    private bool HasRgbBlocks(string[] redBlock, string[] greenBlock, string[] blueBlock)
    {
        return redBlock.Length >= 2
            && greenBlock.Length >= 2
            && blueBlock.Length >= 2;
    }

    protected static int ParseHexDigit(string value)
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

    protected static int ParseHexByte(string high, string low)
    {
        return (ParseHexDigit(high) << 4) | ParseHexDigit(low);
    }

    protected static int ParseHexBlock(string[] hexCode)
    {
        if (hexCode.Length < 3)
            return 0;

        return (ParseHexDigit(hexCode[0]) << 8)
            | (ParseHexDigit(hexCode[1]) << 4)
            | ParseHexDigit(hexCode[2]);
    }

    public string[] GenerateRandomGenderHexValue(int minHex, int maxHex)
    {
        var value = _random.Next(minHex, maxHex + 1);
        var hexString = value.ToString("X3");
        return new[]
        {
            hexString[0].ToString(),
            hexString[1].ToString(),
            hexString[2].ToString()
        };
    }

    public string[] GenerateTripleHexValues(byte min0, byte max0, byte min1, byte max1, byte min2, byte max2)
    {
        return new[]
        {
            _random.Next(min0, max0).ToString("X1"),
            _random.Next(min1, max1).ToString("X1"),
            _random.Next(min2, max2).ToString("X1")
        };
    }

    public string[] GenerateRandomHexValues()
    {
        return new[]
        {
            _random.Next(0, 16).ToString("X1"),
            _random.Next(0, 16).ToString("X1"),
            _random.Next(0, 16).ToString("X1")
        };
    }

}
