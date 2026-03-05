using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Part;
using Content.Shared.HealthExaminable;
using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentExamineSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstalledAugmentsComponent, HealthBeingExaminedEvent>(OnHealthExamined);
    }
    private void OnHealthExamined(EntityUid uid, InstalledAugmentsComponent component, HealthBeingExaminedEvent args)
    {
        var augments = new List<(string PartName, string Text, string Color)>();

        foreach (var (partUid, partComp) in _body.GetBodyChildren(uid))
        {
            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentExamineComponent>(organUid, out var examine))
                    continue;

                if (!examine.Visible || string.IsNullOrEmpty(examine.ExamineText))
                    continue;

                var partName = !string.IsNullOrEmpty(examine.ExaminePartText)
                    ? Loc.GetString(examine.ExaminePartText)
                    : GetPartName(partComp);
                var text = Loc.GetString(examine.ExamineText);
                augments.Add((partName, text, examine.Color));
            }
        }

        if (augments.Count == 0)
            return;

        args.Message.PushNewline();
        args.Message.AddMarkupOrThrow(Loc.GetString("augment-examine-header"));

        foreach (var (partName, text, color) in augments)
        {
            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString("augment-examine-entry",
                ("color", color),
                ("part", partName),
                ("text", text)));
        }
    }
    private string GetPartName(BodyPartComponent part)
    {
        var partId = "body-part-" + (part.ParentSlot?.Id ?? part.PartType.ToString().ToLower()).Replace(" ", "-");
        return Loc.GetString(partId);
    }
}
