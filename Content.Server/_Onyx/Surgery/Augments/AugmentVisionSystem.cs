using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Flash.Components;
using Content.Shared.Overlays;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentVisionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentVisionComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentVisionComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
    }

    private void OnOrganAddedToBody(EntityUid uid, AugmentVisionComponent component, ref OrganAddedToBodyEvent args)
    {
        ApplyVision(args.Body, component, true);
    }

    private void OnOrganRemovedFromBody(EntityUid uid, AugmentVisionComponent component, ref OrganRemovedFromBodyEvent args)
    {
        ApplyVision(args.OldBody, component, false);
    }

    private void ApplyVision(EntityUid body, AugmentVisionComponent component, bool enable)
    {
        foreach (var visionType in component.GetAllVisionTypes())
        {
            ApplyVisionType(body, visionType, enable);
        }
    }

    private void ApplyVisionType(EntityUid body, AugmentVisionType visionType, bool enable)
    {
        switch (visionType)
        {
            case AugmentVisionType.NightVision:
                ToggleNightVision(body, enable);
                break;

            case AugmentVisionType.ThermalVision:
                ToggleThermalVision(body, enable);
                break;

            case AugmentVisionType.FlashProtection:
                if (enable)
                    EnsureComp<FlashImmunityComponent>(body);
                else
                    RemComp<FlashImmunityComponent>(body);
                break;

            case AugmentVisionType.MedicalHUD:
                if (enable)
                    EnsureComp<ShowHealthIconsComponent>(body);
                else
                    RemComp<ShowHealthIconsComponent>(body);
                break;

            case AugmentVisionType.SecurityHUD:
                if (enable)
                {
                    EnsureComp<ShowJobIconsComponent>(body);
                    EnsureComp<ShowCriminalRecordIconsComponent>(body);
                }
                else
                {
                    RemComp<ShowJobIconsComponent>(body);
                    RemComp<ShowCriminalRecordIconsComponent>(body);
                }
                break;

            case AugmentVisionType.DiagnosticHUD:
                if (enable)
                    EnsureComp<ShowHealthIconsComponent>(body);
                else
                    RemComp<ShowHealthIconsComponent>(body);
                break;

            case AugmentVisionType.SyndicateHUD:
                if (enable)
                    EnsureComp<ShowSyndicateIconsComponent>(body);
                else
                    RemComp<ShowSyndicateIconsComponent>(body);
                break;

            case AugmentVisionType.MindShieldHUD:
                if (enable)
                    EnsureComp<ShowMindShieldIconsComponent>(body);
                else
                    RemComp<ShowMindShieldIconsComponent>(body);
                break;
        }
    }
    private void ToggleNightVision(EntityUid uid, bool enable)
    {

        if (enable)
        {
 
        }
        else
        {

        }
    }

    private void ToggleThermalVision(EntityUid uid, bool enable)
    {
        if (enable)
        {

        }
        else
        {

        }
    }
}
