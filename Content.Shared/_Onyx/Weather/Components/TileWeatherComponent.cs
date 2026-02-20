using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Weather.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TileWeatherComponent : Component
{
    /// <summary>
    /// Chunk size for storing weather data (same as roof chunk size).
    /// </summary>
    public const int ChunkSize = 8;

    /// <summary>
    /// Chunk origin and bitmask of disabled weather tiles in chunk.
    /// Each bit represents one tile in the 8x8 chunk.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<Vector2i, ulong> Data = new();

    /// <summary>
    /// Chunk origin and bitmask of enabled weather tiles in chunk.
    /// Each bit represents one tile in the 8x8 chunk.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<Vector2i, ulong> EnableData = new();
}
