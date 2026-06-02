using Content.Client._Onyx.Wiki;
using Content.Shared._Onyx.Wiki;
using Robust.Shared.Prototypes;

namespace Content.Client._Onyx.Wiki.Components;

/// <summary>
/// Stores wiki article ids relevant to this entity.
/// </summary>
[RegisterComponent]
[Access(typeof(WikiHelpSystem))]
public sealed partial class WikiHelpComponent : Component
{
    /// <summary>
    /// Article ids to open. The first existing article is selected.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<WikiArticlePrototype>> Articles = new();

    /// <summary>
    /// Whether interacting with the entity should open the wiki.
    /// Mostly intended for books.
    /// </summary>
    [DataField("openOnActivation")]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool OpenOnActivation;
}
