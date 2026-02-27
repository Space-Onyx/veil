// SPDX-FileCopyrightText: 2022 Kara <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2022 Moony <moonheart08@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2022 wrexbe <81056464+wrexbe@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: MIT

using JetBrains.Annotations;
using Robust.Shared.Random;

namespace Content.Server.Maps.NameGenerators;

[UsedImplicitly]
public sealed partial class NanotrasenNameGenerator : StationNameGenerator
{
    /// <summary>
    ///     Where the map comes from. Should be a two or three letter code, for example "VG" for Packedstation.
    /// </summary>
    // <Onyx-edited>
    [DataField("prefixCreator")] public string PrefixCreator = "";
    [DataField("prefix")] public string Prefix = "NT";
    [DataField("suffix")] public string[] SuffixCodes = new[] { "LV", "NS", "EV", "PR", "RX" };

    public override string FormatName(string input)
    {
        var random = IoCManager.Resolve<IRobustRandom>();

        var number = random.Next(1, 1000);
        var suffixCode = random.Pick(SuffixCodes);
        var suffixNumber = random.Next(0, 1000);
        var suffix = $"{suffixCode}-{suffixNumber:D3}";
        var fullPrefix = $"{Prefix}{PrefixCreator}";

        return input
            .Replace("{prefix}", Prefix)
            .Replace("{prefixCreator}", PrefixCreator)
            .Replace("{suffix}", suffix)
            .Replace("{number}", number.ToString("D3"))
            .Replace("{0}", fullPrefix)
            .Replace("{1}", suffix);
    // </Onyx-edited>
    }
}
