// SPDX-FileCopyrightText: 2019 DamianX <DamianX@users.noreply.github.com>
// SPDX-FileCopyrightText: 2019 ZelteHonor <gabrieldionbouchard@gmail.com>
// SPDX-FileCopyrightText: 2020 Exp <theexp111@gmail.com>
// SPDX-FileCopyrightText: 2021 Acruid <shatter66@gmail.com>
// SPDX-FileCopyrightText: 2021 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2021 Swept <sweptwastaken@protonmail.com>
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto <gradientvera@outlook.com>
// SPDX-FileCopyrightText: 2022 Andreas Kämper <andreas@kaemper.tech>
// SPDX-FileCopyrightText: 2022 Flipp Syder <76629141+vulppine@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 ShadowCommander <10494922+ShadowCommander@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2022 wrexbe <81056464+wrexbe@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 TemporalOroboros <TemporalOroboros@gmail.com>
// SPDX-FileCopyrightText: 2023 ike709 <ike709@github.com>
// SPDX-FileCopyrightText: 2023 ike709 <ike709@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Kara <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Pieter-Jan Briers <pieterjan.briers+git@gmail.com>
// SPDX-FileCopyrightText: 2024 Winkarst <74284083+Winkarst-cpu@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Kevin Zheng <kevinz5000@gmail.com>
// SPDX-FileCopyrightText: 2025 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client._Onyx.VendingMachines.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using System.Linq;

namespace Content.Client.VendingMachines
{
    public sealed class VendingMachineBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private FancyVendingMachineMenu? _menu; // <Onyx Tweak> - Новая панель

        [ViewVariables]
        private List<VendingMachineInventoryEntry> _cachedInventory = new();

        public VendingMachineBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _menu = new();  // <Onyx Tweak> - Новая панель
            var component = EntMan.GetComponent<VendingMachineComponent>(Owner); // <Onyx Economy>
            var system = EntMan.System<VendingMachineSystem>(); // <Onyx Economy>
            _cachedInventory = system.GetAllInventory(Owner, component); // <Onyx Economy>
            _menu.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;

            // <Onyx Tweak start>
            _menu.OnClose += Close;
            _menu.OnItemSelected += OnItemSelected;
            _menu.OnWithdraw += () => SendMessage(new VendingMachineWithdrawMessage());
            _menu.Populate(Owner, _cachedInventory, component.PriceMultiplier, component.Credits);
            // <Onyx Tweak end>

            _menu.OpenCentered();
        }

        public void Refresh()
        {
            var system = EntMan.System<VendingMachineSystem>();
            var component = EntMan.GetComponent<VendingMachineComponent>(Owner); //<Onyx Economy>
            _cachedInventory = system.GetAllInventory(Owner);

            _menu?.Populate(Owner, _cachedInventory, component.PriceMultiplier, component.Credits); //<Onyx Economy>-Tweak
        }


        // START-TWEAK
        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            var system = EntMan.System<VendingMachineSystem>();

            if (state is not VendingMachineInterfaceState newState)
                return;

            _cachedInventory = system.GetAllInventory(Owner);

            _menu?.Populate(Owner, _cachedInventory, newState.PriceMultiplier, newState.Credits); //<Onyx Economy>-Tweak
        }

        private void OnItemSelected(VendingMachineInventoryEntry entry)
        {
            SendPredictedMessage(new VendingMachineEjectCountMessage(entry, 1));
        }

        // END-TWEAK

        public void UpdateAmounts()
        {
            var system = EntMan.System<VendingMachineSystem>();
            var component = EntMan.GetComponent<VendingMachineComponent>(Owner);
            _cachedInventory = system.GetAllInventory(Owner);
            _menu?.Populate(Owner, _cachedInventory, component.PriceMultiplier, component.Credits);
        }

        private void OnItemSelected(GUIBoundKeyEventArgs args, ListData data)
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            if (data is not VendorItemsListData { ItemIndex: var itemIndex })
                return;

            if (_cachedInventory.Count == 0)
                return;

            var selectedItem = _cachedInventory.ElementAtOrDefault(itemIndex);

            if (selectedItem == null)
                return;

            SendPredictedMessage(new VendingMachineEjectMessage(selectedItem.Type, selectedItem.ID));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            if (_menu == null)
                return;

            _menu.OnItemSelected -= OnItemSelected;    // <Onyx eject count>
            _menu.OnClose -= Close;
            _menu.Dispose();
        }
    }
}