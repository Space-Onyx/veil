using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client._Onyx.Wiki.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.Input;
using Robust.Client.State;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._Onyx.UserInterface.Systems.Wiki;

public sealed class WikiUIController : UIController, IOnStateEntered<GameplayState>, IOnStateEntered<LobbyState>, IOnStateExited<GameplayState>, IOnStateExited<LobbyState>
{
    private WikiWindow? _window;
    private string? _lastArticleId;
    private MenuButton? WikiButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.WikiButton;

    public void OnStateEntered(GameplayState state)
    {
        HandleStateEntered();
    }

    public void OnStateEntered(LobbyState state)
    {
        HandleStateEntered();
    }

    public void OnStateExited(GameplayState state)
    {
        HandleStateExited();
    }

    public void OnStateExited(LobbyState state)
    {
        HandleStateExited();
    }

    public void LoadButton()
    {
        if (WikiButton != null)
            WikiButton.OnPressed += WikiButtonPressed;
    }

    public void UnloadButton()
    {
        if (WikiButton != null)
            WikiButton.OnPressed -= WikiButtonPressed;
    }

    public void ToggleWiki()
    {
        if (_window != null && _window.IsOpen)
        {
            UIManager.ClickSound();
            _window.Close();
        }
        else
        {
            OpenWiki();
        }
    }

    public void OpenWiki(string? articleId = null)
    {
        if (articleId == null)
            OpenDefaultWiki();
        else
            OpenArticle(articleId);
    }

    public void OpenDefaultWiki()
    {
        if (_window == null)
            CreateWindow();

        var window = _window!;

        if (WikiButton != null)
            WikiButton.SetClickPressed(!window.IsOpen);

        if (_lastArticleId != null)
        {
            window.OpenArticle(_lastArticleId);
        }
        else
        {
            window.OpenDefault();
            _lastArticleId = window.SelectedArticleId;
        }

        window.OpenCenteredRight();
    }

    public void OpenArticle(string articleId)
    {
        if (_window == null)
            CreateWindow();

        var window = _window!;

        if (WikiButton != null)
            WikiButton.SetClickPressed(!window.IsOpen);

        window.OpenArticle(articleId);
        _lastArticleId = window.SelectedArticleId;
        window.OpenCenteredRight();
    }

    private void HandleStateEntered()
    {
        DebugTools.Assert(_window == null);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenWiki, InputCmdHandler.FromDelegate(_ => ToggleWiki()))
            .Register<WikiUIController>();
    }

    private void HandleStateExited()
    {
        DestroyWindow();

        CommandBinds.Unregister<WikiUIController>();
    }

    private void CreateWindow()
    {
        DebugTools.Assert(_window == null);

        _window = UIManager.CreateWindow<WikiWindow>();
        _window.OnClose += OnWindowClosed;
        _window.OnOpen += OnWindowOpen;
    }

    private void DestroyWindow()
    {
        if (_window == null)
            return;

        _lastArticleId = _window.SelectedArticleId;
        _window.OnClose -= OnWindowClosed;
        _window.OnOpen -= OnWindowOpen;
        _window.Dispose();
        _window = null;
    }

    private void WikiButtonPressed(ButtonEventArgs args)
    {
        ToggleWiki();
    }

    private void OnWindowClosed()
    {
        if (WikiButton != null)
            WikiButton.Pressed = false;

        DestroyWindow();
    }

    private void OnWindowOpen()
    {
        if (WikiButton != null)
            WikiButton.Pressed = true;
    }
}
