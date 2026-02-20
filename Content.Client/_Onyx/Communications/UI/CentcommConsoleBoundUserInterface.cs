using Content.Shared._Onyx.Communications;
using Content.Shared.CrewManifest;
using Robust.Client.UserInterface;

namespace Content.Client._Onyx.Communications.UI
{
    public sealed class CentcommConsoleBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private CentcommConsoleMenu? _menu;

        private Dictionary<NetEntity, string> _stationNames = new();
        private NetEntity? _selectedStation;
        private string _selectedStationName = "";
        private CrewManifestEntries? _manifestEntries;

        private CentcommManifestWindow? _manifestWindow;

        // Track the currently selected tab to avoid resetting it on UI updates
        private int _currentTabIndex = 0;

        public CentcommConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _menu = this.CreateWindow<CentcommConsoleMenu>();
            _menu.OnCallShuttle += CallShuttle;
            _menu.OnRecallShuttle += RecallShuttle;
            _menu.OnViewManifest += ViewManifest;
            _menu.OnCreateFTLDisk += CreateFTLDisk;
            _menu.OnToggleFTLCorridor += ToggleFTLCorridor;
            _menu.OnApplyThreatCode += ApplyThreatCode;
            _menu.OnTabChanged += tabIndex => _currentTabIndex = tabIndex;

            // Request initial FTL state
            SendMessage(new CentcommConsoleRequestFTLStateMessage());
            // Server will send UI state immediately when BoundUIOpenedEvent is raised
        }

        private void CallShuttle(float arrivalTimeMinutes)
        {
            SendMessage(new CentcommConsoleCallShuttleMessage(arrivalTimeMinutes));
        }

        private void RecallShuttle()
        {
            SendMessage(new CentcommConsoleRecallShuttleMessage());
        }

        private void ViewManifest()
        {
            // Close existing manifest window if it's open
            if (_manifestWindow != null)
            {
                _manifestWindow.Close();
                _manifestWindow = null;
            }

            // Create and open new manifest window
            _manifestWindow = new CentcommManifestWindow();
            _manifestWindow.OnStationSelected += station => SendMessage(new CentcommConsoleSelectStationMessage(station));
            _manifestWindow.OnClose += () => _manifestWindow = null; // Clear reference when window is closed
            _manifestWindow.Populate(_stationNames, _selectedStation, _selectedStationName, _manifestEntries);
            _manifestWindow.OpenCentered();
        }

        private void CreateFTLDisk()
        {
            SendMessage(new CentcommConsoleCreateFTLDiskMessage());
        }

        private void ToggleFTLCorridor()
        {
            SendMessage(new CentcommConsoleToggleFTLCorridorMessage());
        }

        private void ApplyThreatCode(NetEntity station, string threatCode)
        {
            SendMessage(new CentcommConsoleApplyThreatCodeMessage(station, threatCode));
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not CentcommConsoleInterfaceState centcommState)
                return;

            _stationNames = centcommState.StationNames;
            _selectedStation = centcommState.SelectedStation;
            _selectedStationName = centcommState.SelectedStationName;
            _manifestEntries = centcommState.ManifestEntries;

            if (_menu != null)
            {
                _menu.CountdownStarted = centcommState.CountdownStarted;
                _menu.CountdownEnd = centcommState.ExpectedCountdownEnd;
                _menu.CanCallShuttle = centcommState.CanCallShuttle;
                _menu.CanRecallShuttle = centcommState.CanRecallShuttle;

                _menu.UpdateCountdown();
                _menu.ViewManifestButton.Disabled = !centcommState.CanViewManifest;
                _menu.CreateFTLDiskButton.Disabled = !centcommState.CanCreateFTLDisk;
                _menu.ToggleFTLCorridorButton.Disabled = !centcommState.CanToggleFTLCorridor;
                _menu.ApplyThreatCodeButton.Disabled = !centcommState.CanApplyThreatCode;

                // Update FTL button with current state
                _menu.UpdateFTLButton(centcommState.FTLCorridorOpen);

                // Handle tab visibility
                UpdateTabVisibility(centcommState);

                // Populate communication tab dropdowns
                _menu.PopulateStationSelector(centcommState.StationNames);
                _menu.PopulateThreatCodeSelector(centcommState.ThreatCodes);
            }

            if (_manifestWindow != null)
            {
                _manifestWindow.Populate(_stationNames, _selectedStation, _selectedStationName, _manifestEntries);
            }
        }

        private void UpdateTabVisibility(CentcommConsoleInterfaceState state)
        {
            if (_menu == null)
                return;

            // Hide/show tabs based on configuration
            _menu.SetTabVisible(0, state.CommunicationTabEnabled);
            _menu.SetTabVisible(1, state.EvacuationTabEnabled);
            _menu.SetTabVisible(2, state.FTLTabEnabled);

            // Check if current tab is still available
            bool currentTabAvailable = false;
            switch (_currentTabIndex)
            {
                case 0:
                    currentTabAvailable = state.CommunicationTabEnabled;
                    break;
                case 1:
                    currentTabAvailable = state.EvacuationTabEnabled;
                    break;
                case 2:
                    currentTabAvailable = state.FTLTabEnabled;
                    break;
            }

            // If current tab is not available, switch to the first available tab
            if (!currentTabAvailable)
            {
                int newTabIndex = -1;
                if (state.CommunicationTabEnabled)
                    newTabIndex = 0;
                else if (state.EvacuationTabEnabled)
                    newTabIndex = 1;
                else if (state.FTLTabEnabled)
                    newTabIndex = 2;

                if (newTabIndex >= 0)
                {
                    _currentTabIndex = newTabIndex;
                    _menu.SetCurrentTab(newTabIndex);
                }
            }
            else
            {
                // Current tab is still available, ensure it's selected
                _menu.SetCurrentTab(_currentTabIndex);
            }
        }

        protected override void ReceiveMessage(BoundUserInterfaceMessage message)
        {
            base.ReceiveMessage(message);

            if (message is CentcommConsoleUpdateFTLButtonMessage updateMsg)
            {
                _menu?.UpdateFTLButton(updateMsg.IsOpen);
            }
            else if (message is CentcommConsoleApplyThreatCodeMessage applyMsg)
            {
                _menu?.OnThreatCodeApplied(applyMsg.ThreatCode == "УСПЕШНО!");
            }
        }
    }
}