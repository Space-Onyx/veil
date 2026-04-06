using Content.Shared._Onyx.ZLevels.Core.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared._Onyx.ZLevels.Core.EntitySystems;

public static class ZLevelsExtensions
{
    public static bool IsGrounded(this PhysicsComponent phys, IEntityManager entMan)
    {
        if (!entMan.TryGetComponent<CEZPhysicsComponent>(phys.Owner, out var zPhys))
            return phys.BodyStatus == BodyStatus.OnGround;

        return phys.BodyStatus == BodyStatus.OnGround && zPhys.IsGrounded;
    }

    public static bool IsGrounded(this CEZPhysicsComponent zPhys, IEntityManager entMan)
    {
        if (!entMan.TryGetComponent<PhysicsComponent>(zPhys.Owner, out var phys))
            return true;

        return phys.BodyStatus == BodyStatus.OnGround && zPhys.IsGrounded;
    }
}
