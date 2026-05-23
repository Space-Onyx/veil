using Content.Shared._Onyx.ProxyControl;
using Robust.Shared.Player;

namespace Content.Client._Onyx.ProxyControl;

public sealed class ClientProxyControlSystem : EntitySystem
{
    [Dependency] private readonly ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProxyControlComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ProxyControlComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<ProxyControlComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ProxyControlComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ProxyControlComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnPlayerAttached(EntityUid uid, ProxyControlComponent component, LocalPlayerAttachedEvent args)
    {
        RaiseLocalEvent(new ProxyControlLocalRefreshEvent(uid, ProxyControlLocalRefreshKind.Attached));
    }

    private void OnPlayerDetached(EntityUid uid, ProxyControlComponent component, LocalPlayerDetachedEvent args)
    {
        RaiseLocalEvent(new ProxyControlLocalRefreshEvent(uid, ProxyControlLocalRefreshKind.Detached));
    }

    private void OnStartup(EntityUid uid, ProxyControlComponent component, ComponentStartup args)
    {
        if (_player.LocalEntity == uid)
            RaiseLocalEvent(new ProxyControlLocalRefreshEvent(uid, ProxyControlLocalRefreshKind.Startup));
    }

    private void OnShutdown(EntityUid uid, ProxyControlComponent component, ComponentShutdown args)
    {
        if (_player.LocalEntity == uid)
            RaiseLocalEvent(new ProxyControlLocalRefreshEvent(uid, ProxyControlLocalRefreshKind.Shutdown));
    }

    private void OnState(Entity<ProxyControlComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_player.LocalEntity == ent.Owner)
            RaiseLocalEvent(new ProxyControlLocalRefreshEvent(ent.Owner, ProxyControlLocalRefreshKind.State));
    }
}

public sealed class ProxyControlLocalRefreshEvent(
    EntityUid controller,
    ProxyControlLocalRefreshKind kind)
    : EntityEventArgs
{
    public readonly EntityUid Controller = controller;
    public readonly ProxyControlLocalRefreshKind Kind = kind;
}

public enum ProxyControlLocalRefreshKind : byte
{
    Attached,
    Detached,
    Startup,
    Shutdown,
    State,
}
