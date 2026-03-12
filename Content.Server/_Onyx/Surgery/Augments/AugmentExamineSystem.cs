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
        var augments = CollectVisibleAugments(uid);

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

    private List<(string PartName, string Text, string Color)> CollectVisibleAugments(EntityUid body)
    {
        var result = new List<(string PartName, string Text, string Color)>();

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentExamineComponent>(organUid, out var examine)
                    || !examine.Visible
                    || string.IsNullOrEmpty(examine.ExamineText))
                {
                    continue;
                }

                var partName = !string.IsNullOrEmpty(examine.ExaminePartText)
                    ? Loc.GetString(examine.ExaminePartText)
                    : GetPartName(partComp);
                result.Add((partName, Loc.GetString(examine.ExamineText), examine.Color));
            }
        }

        return result;
    }

    private string GetPartName(BodyPartComponent part)
    {
        var partId = "body-part-" + (part.ParentSlot?.Id ?? part.PartType.ToString().ToLower()).Replace(" ", "-");
        return Loc.GetString(partId);
    }
}
