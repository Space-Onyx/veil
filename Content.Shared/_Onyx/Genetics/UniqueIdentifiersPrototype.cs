using Content.Shared.Genetics.Systems;
using Robust.Shared.Serialization;

namespace Content.Shared.Genetics;

[Serializable, NetSerializable]
[Access(typeof(SharedDnaModifierSystem), typeof(EnzymeInfo))]
public sealed class UniqueIdentifiersData
{
    public string ID { get; set; } = string.Empty;
    public string[] HairColorR { get; set; } = Array.Empty<string>();
    public string[] HairColorG { get; set; } = Array.Empty<string>();
    public string[] HairColorB { get; set; } = Array.Empty<string>();
    public string[] SecondaryHairColorR { get; set; } = Array.Empty<string>();
    public string[] SecondaryHairColorG { get; set; } = Array.Empty<string>();
    public string[] SecondaryHairColorB { get; set; } = Array.Empty<string>();
    public string[] BeardColorR { get; set; } = Array.Empty<string>();
    public string[] BeardColorG { get; set; } = Array.Empty<string>();
    public string[] BeardColorB { get; set; } = Array.Empty<string>();
    public string[] SkinColorR { get; set; } = Array.Empty<string>();
    public string[] SkinColorG { get; set; } = Array.Empty<string>();
    public string[] SkinColorB { get; set; } = Array.Empty<string>();
    public string[] Race { get; set; } = Array.Empty<string>();
    public string[] HeadAccessoryColorR { get; set; } = Array.Empty<string>();
    public string[] HeadAccessoryColorG { get; set; } = Array.Empty<string>();
    public string[] HeadAccessoryColorB { get; set; } = Array.Empty<string>();
    public string[] HeadMarkingColorR { get; set; } = Array.Empty<string>();
    public string[] HeadMarkingColorG { get; set; } = Array.Empty<string>();
    public string[] HeadMarkingColorB { get; set; } = Array.Empty<string>();
    public string[] BodyMarkingColorR { get; set; } = Array.Empty<string>();
    public string[] BodyMarkingColorG { get; set; } = Array.Empty<string>();
    public string[] BodyMarkingColorB { get; set; } = Array.Empty<string>();
    public string[] TailMarkingColorR { get; set; } = Array.Empty<string>();
    public string[] TailMarkingColorG { get; set; } = Array.Empty<string>();
    public string[] TailMarkingColorB { get; set; } = Array.Empty<string>();
    public string[] EyeColorR { get; set; } = Array.Empty<string>();
    public string[] EyeColorG { get; set; } = Array.Empty<string>();
    public string[] EyeColorB { get; set; } = Array.Empty<string>();
    public string[] Gender { get; set; } = Array.Empty<string>();
    public string[] HairStyle { get; set; } = Array.Empty<string>();
    public string[] BeardStyle { get; set; } = Array.Empty<string>();
    public string[] HeadAccessoryStyle { get; set; } = Array.Empty<string>();
    public string[] HeadMarkingStyle { get; set; } = Array.Empty<string>();
    public string[] BodyMarkingStyle { get; set; } = Array.Empty<string>();
    public string[] TailMarkingStyle { get; set; } = Array.Empty<string>();
    public string[] Height { get; set; } = Array.Empty<string>();
    public string[] Width { get; set; } = Array.Empty<string>();

    public UniqueIdentifiersData Clone(UniqueIdentifiersData data)
    {
        var newData = new UniqueIdentifiersData()
        {
            ID = data.ID,
            HairColorR = (string[])data.HairColorR.Clone(),
            HairColorG = (string[])data.HairColorG.Clone(),
            HairColorB = (string[])data.HairColorB.Clone(),
            SecondaryHairColorR = (string[])data.SecondaryHairColorR.Clone(),
            SecondaryHairColorG = (string[])data.SecondaryHairColorG.Clone(),
            SecondaryHairColorB = (string[])data.SecondaryHairColorB.Clone(),
            BeardColorR = (string[])data.BeardColorR.Clone(),
            BeardColorG = (string[])data.BeardColorG.Clone(),
            BeardColorB = (string[])data.BeardColorB.Clone(),
            SkinColorR = (string[])data.SkinColorR.Clone(),
            SkinColorG = (string[])data.SkinColorG.Clone(),
            SkinColorB = (string[])data.SkinColorB.Clone(),
            Race = (string[])data.Race.Clone(),
            HeadAccessoryColorR = (string[])data.HeadAccessoryColorR.Clone(),
            HeadAccessoryColorG = (string[])data.HeadAccessoryColorG.Clone(),
            HeadAccessoryColorB = (string[])data.HeadAccessoryColorB.Clone(),
            HeadMarkingColorR = (string[])data.HeadMarkingColorR.Clone(),
            HeadMarkingColorG = (string[])data.HeadMarkingColorG.Clone(),
            HeadMarkingColorB = (string[])data.HeadMarkingColorB.Clone(),
            BodyMarkingColorR = (string[])data.BodyMarkingColorR.Clone(),
            BodyMarkingColorG = (string[])data.BodyMarkingColorG.Clone(),
            BodyMarkingColorB = (string[])data.BodyMarkingColorB.Clone(),
            TailMarkingColorR = (string[])data.TailMarkingColorR.Clone(),
            TailMarkingColorG = (string[])data.TailMarkingColorG.Clone(),
            TailMarkingColorB = (string[])data.TailMarkingColorB.Clone(),
            EyeColorR = (string[])data.EyeColorR.Clone(),
            EyeColorG = (string[])data.EyeColorG.Clone(),
            EyeColorB = (string[])data.EyeColorB.Clone(),
            Gender = (string[])data.Gender.Clone(),
            BeardStyle = (string[])data.BeardStyle.Clone(),
            HairStyle = (string[])data.HairStyle.Clone(),
            HeadAccessoryStyle = (string[])data.HeadAccessoryStyle.Clone(),
            HeadMarkingStyle = (string[])data.HeadMarkingStyle.Clone(),
            BodyMarkingStyle = (string[])data.BodyMarkingStyle.Clone(),
            TailMarkingStyle = (string[])data.TailMarkingStyle.Clone(),
            Height = (string[])data.Height.Clone(),
            Width = (string[])data.Width.Clone()
        };

        return newData;
    }
}

public readonly record struct UniqueIdentifierBlock(int Block, Func<UniqueIdentifiersData, string[]> GetValues);

public static class UniqueIdentifierBlocks
{
    public static readonly UniqueIdentifierBlock[] All =
    {
        new(1, data => data.HairColorR),
        new(2, data => data.HairColorG),
        new(3, data => data.HairColorB),
        new(4, data => data.SecondaryHairColorR),
        new(5, data => data.SecondaryHairColorG),
        new(6, data => data.SecondaryHairColorB),
        new(7, data => data.BeardColorR),
        new(8, data => data.BeardColorG),
        new(9, data => data.BeardColorB),
        new(10, data => data.SkinColorR),
        new(11, data => data.SkinColorG),
        new(12, data => data.SkinColorB),
        new(13, data => data.Race),
        new(17, data => data.HeadAccessoryColorR),
        new(18, data => data.HeadAccessoryColorG),
        new(19, data => data.HeadAccessoryColorB),
        new(20, data => data.HeadMarkingColorR),
        new(21, data => data.HeadMarkingColorG),
        new(22, data => data.HeadMarkingColorB),
        new(23, data => data.BodyMarkingColorR),
        new(24, data => data.BodyMarkingColorG),
        new(25, data => data.BodyMarkingColorB),
        new(26, data => data.TailMarkingColorR),
        new(27, data => data.TailMarkingColorG),
        new(28, data => data.TailMarkingColorB),
        new(29, data => data.EyeColorR),
        new(30, data => data.EyeColorG),
        new(31, data => data.EyeColorB),
        new(32, data => data.Gender),
        new(33, data => data.BeardStyle),
        new(34, data => data.HairStyle),
        new(35, data => data.HeadAccessoryStyle),
        new(36, data => data.HeadMarkingStyle),
        new(37, data => data.BodyMarkingStyle),
        new(38, data => data.TailMarkingStyle),
        new(39, data => data.Height),
        new(40, data => data.Width)
    };

    public static bool TryGet(UniqueIdentifiersData data, int block, out string[] values)
    {
        foreach (var definition in All)
        {
            if (definition.Block != block)
                continue;

            values = definition.GetValues(data);
            return true;
        }

        values = Array.Empty<string>();
        return false;
    }
}
