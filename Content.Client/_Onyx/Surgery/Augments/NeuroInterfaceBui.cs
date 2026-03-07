using System;
using Content.Shared._Onyx.Surgery.Augments;
using JetBrains.Annotations;

namespace Content.Client._Onyx.Surgery.Augments;

[UsedImplicitly]
public sealed class NeuroInterfaceBui : BoundUserInterface
{
    private NeuroInterfaceWindow? _window;

    public NeuroInterfaceBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new NeuroInterfaceWindow();
        _window.OnClose += Close;
        _window.OnToggleRequested += (augment, enable) =>
            SendMessage(new NeuroInterfaceToggleAugmentMessage(augment, enable));

        if (State is NeuroInterfaceBuiState state)
            _window.UpdateState(state);

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not NeuroInterfaceBuiState cast)
            return;

        _window.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        _window?.Close();
        _window = null;
        base.Dispose(disposing);
    }
}


