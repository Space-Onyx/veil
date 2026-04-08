using Content.Server._Onyx.Chat;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    private void TryTriggerInlineActionEmotes(EntityUid source, string message, bool forced, bool ignoreActionBlocker)
    {
        if (!_actionBlocker.CanEmote(source) && !ignoreActionBlocker)
            return;

        foreach (var action in InlineActionFormatter.ExtractActions(message))
        {
            TryEmoteChatInput(source, action, forced);
        }
    }
}