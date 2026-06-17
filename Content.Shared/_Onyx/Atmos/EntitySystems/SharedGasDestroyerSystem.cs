using Content.Shared._Onyx.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Examine;

namespace Content.Shared._Onyx.Atmos.EntitySystems;

public abstract class SharedGasDestroyerSystem : EntitySystem
{
    [Dependency] private readonly SharedAtmosphereSystem _sharedAtmosphereSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GasDestroyerComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(Entity<GasDestroyerComponent> ent, ref ExaminedEvent args)
    {
        var component = ent.Comp;

        using (args.PushGroup(nameof(GasDestroyerComponent)))
        {
            if (component.DestroyAnyGas)
            {
                args.PushMarkup(Loc.GetString("gas-destroyer-destroys-any-text"));
            }
            else if (component.ListDestroyGas is not null)
            {
                var gasesFormat = "";
                var len = component.ListDestroyGas.Count;
                var i = 0;

                foreach (var gas in component.ListDestroyGas)
                {
                    gasesFormat += Loc.GetString(_sharedAtmosphereSystem.GetGas(gas.Key).Name)
                        + $" ({Math.Round(gas.Value * 100, 2)}%)"
                        + (i < len - 1 ? ", " : "");
                    i++;
                }

                args.PushMarkup(Loc.GetString("gas-destroyer-destroys-text", ("gas", gasesFormat)));
            }
            else if (component.DestroyGas is not null)
            {
                args.PushMarkup(Loc.GetString("gas-destroyer-destroys-text",
                    ("gas", Loc.GetString(_sharedAtmosphereSystem.GetGas(component.DestroyGas.Value).Name))));
            }

            args.PushText(Loc.GetString("gas-destroyer-amount-text",
                ("moles", $"{component.DestroyAmount:0.#}")));

            if (component.MinExternalAmount > 0f)
            {
                args.PushText(Loc.GetString("gas-destroyer-moles-cutoff-text",
                    ("moles", $"{component.MinExternalAmount:0.#}")));
            }

            if (component.MinExternalPressure > 0f)
            {
                args.PushText(Loc.GetString("gas-destroyer-pressure-cutoff-text",
                    ("pressure", $"{component.MinExternalPressure:0.#}")));
            }

            args.AddMarkup(component.DestroyerState switch
            {
                GasDestroyerState.Disabled => Loc.GetString("gas-destroyer-state-disabled-text"),
                GasDestroyerState.Idle => Loc.GetString("gas-destroyer-state-idle-text"),
                GasDestroyerState.Working => Loc.GetString("gas-destroyer-state-working-text"),
                _ => throw new IndexOutOfRangeException(nameof(component.DestroyerState)),
            });
        }
    }
}
