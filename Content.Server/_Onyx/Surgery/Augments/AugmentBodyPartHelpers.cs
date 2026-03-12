using System.Collections.Generic;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;

namespace Content.Server._Onyx.Surgery.Augments;

internal static class AugmentBodyPartHelpers
{
    public static List<(EntityUid Id, BodyPartComponent Component)> CollectBodyParts(EntityUid bodyUid, SharedBodySystem bodySystem)
    {
        var bodyParts = new List<(EntityUid Id, BodyPartComponent Component)>();
        foreach (var bodyPart in bodySystem.GetBodyChildren(bodyUid))
        {
            bodyParts.Add(bodyPart);
        }

        return bodyParts;
    }
}
