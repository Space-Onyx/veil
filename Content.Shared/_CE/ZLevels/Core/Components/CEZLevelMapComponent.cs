/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System;
using Robust.Shared.GameStates;

namespace Content.Shared._CE.ZLevels.Core.Components;

/// <summary>
/// Automatically added to the map when it appears in zLevelNetwork.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class CEZLevelMapComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Depth = 0;

    // <Onyx-Tweak>
    [DataField]
    public TimeSpan SuppressFallsUntil = TimeSpan.Zero;
    // </Onyx-Tweak>
}
