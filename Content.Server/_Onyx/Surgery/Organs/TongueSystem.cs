using Content.Shared._Onyx.Speech;
using Content.Shared._Onyx.Surgery.Organs;
using Content.Shared.Body.Events;

namespace Content.Server._Onyx.Surgery.Organs;

public sealed class TongueSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TongueComponent, OrganAddedToBodyEvent>(OnTongueAdded);
        SubscribeLocalEvent<TongueComponent, OrganRemovedFromBodyEvent>(OnTongueRemoved);
    }

    private void OnTongueAdded(Entity<TongueComponent> ent, ref OrganAddedToBodyEvent args)
    {
        RemComp<TonguelessAccentComponent>(args.Body);
    }

    private void OnTongueRemoved(Entity<TongueComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        EnsureComp<TonguelessAccentComponent>(args.OldBody);
    }
}
