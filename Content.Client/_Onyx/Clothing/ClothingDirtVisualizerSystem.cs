using Content.Shared._Onyx.Clothing;
using Content.Client.Items.Systems;
using Content.Shared.Clothing;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Hands;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._Onyx.Clothing;

public sealed class ClothingDirtVisualizerSystem : EntitySystem
{
    private const float CleanupInterval = 5f;

    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;

    private readonly Dictionary<EntityUid, Color> _baseSpriteColors = new();
    private readonly Dictionary<EntityUid, Color?> _lastDirtColors = new();
    private readonly HashSet<EntityUid> _dirtyItems = new();
    private readonly List<EntityUid> _restoreBuffer = new();
    private float _cleanupAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingDirtableComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ClothingDirtableComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
        SubscribeLocalEvent<ClothingDirtableComponent, GetEquipmentVisualsEvent>(OnGetEquipmentVisuals,
            after: [typeof(ClothingSystem)]);
        SubscribeLocalEvent<ClothingDirtableComponent, GetInhandVisualsEvent>(OnGetInhandVisuals,
            after: [typeof(ItemSystem)]);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var uid in _dirtyItems)
        {
            if (!TryComp(uid, out ClothingDirtableComponent? component)
                || !TryComp(uid, out SpriteComponent? sprite))
                continue;

            UpdateItemVisuals((uid, component), sprite);
        }

        _dirtyItems.Clear();
        _cleanupAccumulator += frameTime;
        if (_cleanupAccumulator < CleanupInterval)
            return;

        _cleanupAccumulator = 0f;
        _restoreBuffer.Clear();
        foreach (var uid in _baseSpriteColors.Keys)
        {
            if (!TryComp<ClothingDirtableComponent>(uid, out var dirtable)
                || dirtable.DirtColor == null
                || !HasComp<SpriteComponent>(uid))
            {
                _restoreBuffer.Add(uid);
            }
        }

        foreach (var uid in _restoreBuffer)
        {
            RestoreItemVisuals(uid);
        }
    }

    private void OnStartup(Entity<ClothingDirtableComponent> ent, ref ComponentStartup args)
    {
        UpdateItemVisuals(ent);
    }

    private void OnAfterHandleState(Entity<ClothingDirtableComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _dirtyItems.Add(ent.Owner);
        _item.VisualsChanged(ent.Owner);
    }

    private void OnGetEquipmentVisuals(Entity<ClothingDirtableComponent> ent, ref GetEquipmentVisualsEvent args)
    {
        TintLayers(ent.Comp, args.Layers);
    }

    private void OnGetInhandVisuals(Entity<ClothingDirtableComponent> ent, ref GetInhandVisualsEvent args)
    {
        TintLayers(ent.Comp, args.Layers);
    }

    private void TintLayers(ClothingDirtableComponent component, List<(string, PrototypeLayerData)> layers)
    {
        if (component.DirtColor is not { } dirtColor)
            return;

        for (var i = 0; i < layers.Count; i++)
        {
            var (key, layerData) = layers[i];
            var tinted = CopyLayerData(layerData);
            tinted.Color = BlendDirtColor(layerData.Color ?? Color.White, dirtColor);
            layers[i] = (key, tinted);
        }
    }

    private void UpdateItemVisuals(Entity<ClothingDirtableComponent> ent)
    {
        if (!TryComp(ent.Owner, out SpriteComponent? sprite))
            return;

        UpdateItemVisuals(ent, sprite);
    }

    private void UpdateItemVisuals(Entity<ClothingDirtableComponent> ent, SpriteComponent sprite)
    {
        if (_lastDirtColors.TryGetValue(ent.Owner, out var lastDirtColor)
            && lastDirtColor == ent.Comp.DirtColor)
        {
            return;
        }

        if (ent.Comp.DirtColor is not { } dirtColor)
        {
            RestoreItemVisuals(ent.Owner, sprite);
            return;
        }

        if (!_baseSpriteColors.TryGetValue(ent.Owner, out var baseColor))
        {
            baseColor = sprite.Color;
            _baseSpriteColors[ent.Owner] = baseColor;
        }

        _sprite.SetColor((ent.Owner, sprite), BlendDirtColor(baseColor, dirtColor));
        _lastDirtColors[ent.Owner] = dirtColor;
    }

    private void RestoreItemVisuals(EntityUid uid, SpriteComponent? sprite = null)
    {
        _lastDirtColors.Remove(uid);

        if (!_baseSpriteColors.Remove(uid, out var baseColor)
            || !Resolve(uid, ref sprite, false))
            return;

        _sprite.SetColor((uid, sprite), baseColor);
    }

    private static Color BlendDirtColor(Color baseColor, Color dirtColor)
    {
        var strength = dirtColor.A;
        return new Color(
            baseColor.R * (1f - strength) + dirtColor.R * strength,
            baseColor.G * (1f - strength) + dirtColor.G * strength,
            baseColor.B * (1f - strength) + dirtColor.B * strength,
            baseColor.A);
    }

    private static PrototypeLayerData CopyLayerData(PrototypeLayerData layerData)
    {
        return new PrototypeLayerData
        {
            Shader = layerData.Shader,
            TexturePath = layerData.TexturePath,
            RsiPath = layerData.RsiPath,
            State = layerData.State,
            Scale = layerData.Scale,
            Rotation = layerData.Rotation,
            Offset = layerData.Offset,
            Visible = layerData.Visible,
            Color = layerData.Color,
            MapKeys = layerData.MapKeys == null ? null : new HashSet<string>(layerData.MapKeys),
            RenderingStrategy = layerData.RenderingStrategy,
            CopyToShaderParameters = layerData.CopyToShaderParameters,
            Cycle = layerData.Cycle,
        };
    }
}
