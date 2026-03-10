using System;
using Content.Shared._Onyx.Speech;
using Content.Shared.Speech;
using Robust.Shared.Random;

namespace Content.Server._Onyx.Speech;

public sealed class BrainDamageAccentSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly string[] RuRandomSpeech =
    {
        "Подождите... я не помню",
        "Где я сейчас",
        "Не трогайте!",
        "Мне кажется тут кто-то",
        "Секунду... мысль...",
        "Это было важно, я забыл..",
        "Голоса снова шепчут",
        "Я что-то не понимаю",
        "Не знаю почему, страшно",
        "Дайте подумать",
        "Что-то не так в башке",
        "Я слышу голоса"
    };

    private static readonly string[] EnRandomSpeech =
    {
        "Wait... I forgot",
        "Where am I right now",
        "Don't touch me",
        "I think someone is here",
        "Hold on... I lost the thought",
        "it was important, I forgot",
        "The voices are back",
        "I am missing something",
        "I don't know why, but I'm scared",
        "Stop, let me think",
        "Something is wrong in my head",
        "I keep hearing noise"
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BrainDamagedAccentComponent, AccentGetEvent>(OnAccentGet);
    }

    private void OnAccentGet(Entity<BrainDamagedAccentComponent> ent, ref AccentGetEvent args)
    {
        var replaceChance = Math.Clamp(ent.Comp.MessageReplaceChance, 0f, 1f);
        var swapChance = Math.Clamp(ent.Comp.LetterSwapChance, 0f, 1f);

        if (_random.Prob(replaceChance))
        {
            args.Message = GetRandomSpeech(args.Message);
            return;
        }

        args.Message = ScrambleMessage(args.Message, swapChance);
    }

    private string GetRandomSpeech(string original)
    {
        var ru = ContainsCyrillic(original);
        var source = ru ? RuRandomSpeech : EnRandomSpeech;
        return source[_random.Next(source.Length)];
    }

    private string ScrambleMessage(string message, float letterSwapChance)
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
            if (length < 3 || !_random.Prob(letterSwapChance))
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
