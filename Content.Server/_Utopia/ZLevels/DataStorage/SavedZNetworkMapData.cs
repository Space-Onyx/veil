using System.Text.Json.Serialization;

namespace Content.Server._Utopia.ZLevels;

public sealed class SavedZNetworkMapData
{
    public Dictionary<int, string> LevelPaths { get; set; } = new();
    public List<SavedDeviceLink> DeviceLinks { get; set; } = new(); // <Onyx-ZLevels>
}

// <Onyx-ZLevels>
public sealed class SavedDeviceLink
{
    public int SourceDepth { get; set; }
    public float SourceX { get; set; }
    public float SourceY { get; set; }
    public string SourcePrototype { get; set; } = "";

    public int SinkDepth { get; set; }
    public float SinkX { get; set; }
    public float SinkY { get; set; }
    public string SinkPrototype { get; set; } = "";

    public List<string[]> PortLinks { get; set; } = new();
}
// </Onyx-ZLevels>