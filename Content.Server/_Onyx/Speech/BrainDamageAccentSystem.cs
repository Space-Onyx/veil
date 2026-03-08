using Content.Shared._Onyx.Speech;
using Content.Shared.Speech;
using Robust.Shared.Random;

namespace Content.Server._Onyx.Speech;

public sealed class BrainDamageAccentSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    private const float LetterScrambleChance = 0.6f;
    private const float RandomSpeechChance = 0.3f;

    private static readonly string[] RuRandomSpeech =
    {
        "подождите... я не помню",
        "где я сейчас",
        "не трогайте!",
        "мне кажется тут кто-то",
        "секунду... мысль...",
        "это было важно, я забыл..",
        "голоса снова шепчут",
        "я что-то не понимаю",
        "не знаю почему, страшно",
        "дайте подумать",
        "что-то не так в башке",
        "я слышу голоса"
    };

    private static readonly string[] EnRandomSpeech =
    {
        "wait... I forgot",
        "where am I right now",
        "don't touch me",
        "I think someone is here",
        "hold on... I lost the thought",
        "it was important, I forgot",
        "the voices are back",
        "I am missing something",
        "I don't know why, but I'm scared",
        "stop, let me think",
        "something is wrong in my head",
        "I keep hearing noise"
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BrainDamagedAccentComponent, AccentGetEvent>(OnAccentGet);
    }

    private void OnAccentGet(Entity<BrainDamagedAccentComponent> ent, ref AccentGetEvent args)
    {
        if (_random.Prob(RandomSpeechChance))
        {
            args.Message = GetRandomSpeech(args.Message);
            return;
        }

        args.Message = ScrambleMessage(args.Message);
    }

    private string GetRandomSpeech(string original)
    {
        var ru = ContainsCyrillic(original);
        var source = ru ? RuRandomSpeech : EnRandomSpeech;
        return source[_random.Next(source.Length)];
    }

    private string ScrambleMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        var chars = message.ToCharArray();
        var i = 0;

        while (i < chars.Length)
        {
            if (!char.IsLetter(chars[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < chars.Length && char.IsLetter(chars[i]))
            {
                i++;
            }

            var length = i - start;
            if (length < 3 || !_random.Prob(LetterScrambleChance))
                continue;

            var innerStart = start + 1;
            var innerLength = length - 2;
            if (innerLength <= 1)
                continue;

            for (var j = innerStart + innerLength - 1; j > innerStart; j--)
            {
                var k = _random.Next(innerStart, j + 1);
                (chars[j], chars[k]) = (chars[k], chars[j]);
            }
        }

        return new string(chars);
    }

    private static bool ContainsCyrillic(string message)
    {
        foreach (var chr in message)
        {
            if (chr is >= '\u0400' and <= '\u052F')
                return true;
        }

        return false;
    }
}
