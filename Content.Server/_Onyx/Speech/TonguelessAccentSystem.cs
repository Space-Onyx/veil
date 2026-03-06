using Content.Shared._Onyx.Speech;
using Content.Shared.Speech;
using Robust.Shared.Random;

namespace Content.Server._Onyx.Speech;

public sealed class TonguelessAccentSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    private const float ReplacementChance = 0.7f;

    private static readonly Dictionary<char, char> RuReplacements = new()
    {
        { 'р', 'в' }, { 'Р', 'В' },
        { 'л', 'у' }, { 'Л', 'У' },
        { 'т', 'ф' }, { 'Т', 'Ф' },
        { 'д', 'з' }, { 'Д', 'З' },
        { 'к', 'х' }, { 'К', 'Х' },
        { 'г', 'х' }, { 'Г', 'Х' },
        { 'б', 'м' }, { 'Б', 'М' },
        { 'п', 'ф' }, { 'П', 'Ф' },
        { 'с', 'ш' }, { 'С', 'Ш' },
        { 'з', 'ж' }, { 'З', 'Ж' },
        { 'ц', 'с' }, { 'Ц', 'С' },
        { 'ч', 'щ' }, { 'Ч', 'Щ' },
    };

    private static readonly Dictionary<char, char> EnReplacements = new()
    {
        { 'r', 'w' }, { 'R', 'W' },
        { 'l', 'w' }, { 'L', 'W' },
        { 't', 'f' }, { 'T', 'F' },
        { 'd', 'z' }, { 'D', 'Z' },
        { 'k', 'h' }, { 'K', 'H' },
        { 'g', 'h' }, { 'G', 'H' },
        { 'b', 'm' }, { 'B', 'M' },
        { 'p', 'f' }, { 'P', 'F' },
        { 's', 'h' }, { 'S', 'H' },
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TonguelessAccentComponent, AccentGetEvent>(OnAccentGet);
    }

    private void OnAccentGet(Entity<TonguelessAccentComponent> ent, ref AccentGetEvent args)
    {
        args.Message = Accentuate(args.Message);
    }

    public string Accentuate(string message)
    {
        var chars = message.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (_random.Prob(ReplacementChance))
            {
                if (RuReplacements.TryGetValue(chars[i], out var ruReplacement))
                    chars[i] = ruReplacement;
                else if (EnReplacements.TryGetValue(chars[i], out var enReplacement))
                    chars[i] = enReplacement;
            }
        }
        return new string(chars);
    }
}
