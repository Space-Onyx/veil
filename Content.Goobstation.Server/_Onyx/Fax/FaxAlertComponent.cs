using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Server._Onyx.Fax
{
    [RegisterComponent]
    public sealed partial class FaxAlertComponent : Component
    {
        /// <summary>
        /// ID радиоканала для отправки факс оповещений.
        /// </summary>
        [DataField]
        public ProtoId<RadioChannelPrototype> AlertChannel = "Command";
    }
}