using System.Text.Json.Serialization;
using Content.Shared._Shitmed.Medical.Surgery.Consciousness.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Pain;
using Content.Shared._Shitmed.Medical.Surgery.Pain.Systems;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;
using Content.Goobstation.Maths.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class SuppressPain : EntityEffect
{
    private const string WoundModifierSuffix = "_wound"; // <Onyx-PainFix>
    private const string TraumaModifierSuffix = "_trauma"; // <Onyx-PainFix>

    [DataField(required: true)]
    [JsonPropertyName("amount")]
    public FixedPoint2 Amount = default!;

    [DataField(required: true)]
    [JsonPropertyName("time")]
    public TimeSpan Time = default!;

    [DataField]
    [JsonPropertyName("identifier")]
    public string ModifierIdentifier = "PainSuppressant";

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-suppress-pain");

    public override void Effect(EntityEffectBaseArgs args)
    {
        var scale = FixedPoint2.New(1);

        if (args is EntityEffectReagentArgs reagentArgs)
        {
            scale = reagentArgs.Quantity * reagentArgs.Scale;
        }

        if (!args.EntityManager.System<ConsciousnessSystem>().TryGetNerveSystem(args.TargetEntity, out var nerveSys))
            return;

        var bodyPart = args.EntityManager.System<SharedBodySystem>()
            .GetBodyChildrenOfType(args.TargetEntity, BodyPartType.Head)
            .FirstOrNull();

        if (bodyPart == null)
            return;
        // <Onyx-PainFix Edited>
        var painSystem = args.EntityManager.System<PainSystem>();
        var painChange = -Amount * scale;
        var woundModifierIdentifier = $"{ModifierIdentifier}{WoundModifierSuffix}";
        var traumaModifierIdentifier = $"{ModifierIdentifier}{TraumaModifierSuffix}";

        var hasWoundModifier = painSystem.TryGetPainModifier(
            nerveSys.Value,
            bodyPart.Value.Id,
            woundModifierIdentifier,
            out var woundModifier);
        var hasTraumaModifier = painSystem.TryGetPainModifier(
            nerveSys.Value,
            bodyPart.Value.Id,
            traumaModifierIdentifier,
            out var traumaModifier);

        if (!hasWoundModifier && !hasTraumaModifier)
        {
            painSystem.TryAddPainModifier(
                nerveSys.Value,
                bodyPart.Value.Id,
                ModifierIdentifier,
                painChange,
                time: Time);

            return;
        }

        if (hasWoundModifier && woundModifier != null)
        {
            painSystem.TryChangePainModifier(
                nerveSys.Value,
                bodyPart.Value.Id,
                woundModifierIdentifier,
                woundModifier.Value.Change + painChange,
                time: Time,
                painType: PainDamageTypes.WoundPain);
        }

        if (hasTraumaModifier && traumaModifier != null)
        {
            painSystem.TryChangePainModifier(
                nerveSys.Value,
                bodyPart.Value.Id,
                traumaModifierIdentifier,
                traumaModifier.Value.Change + painChange,
                time: Time,
                painType: PainDamageTypes.TraumaticPain);
        // </Onyx-PainFix Edited>
        }
    }
}
