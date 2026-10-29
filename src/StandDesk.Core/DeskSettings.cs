using System.Text.Json.Serialization;

namespace StandDesk.Core;

public sealed record DeskSettings
{
    public int Version { get; init; } = 1;
    public string Address { get; init; } = "D8:81:2F:0B:F2:7F";
    public int BaseRaw { get; init; } = 6250;
    public int SitRaw { get; init; } = 1162;
    public int StandRaw { get; init; } = 5437;
    public int MinRaw { get; init; } = 1162;
    public int MaxRaw { get; init; } = 5437;
    public bool MotionValidated { get; init; }
    public bool HotkeysEnabled { get; init; }
    public bool StartAtLogin { get; init; }
    public string SitKey { get; init; } = "Ctrl+Alt+Shift+Down";
    public string StandKey { get; init; } = "Ctrl+Alt+Shift+Up";
    public string StopKey { get; init; } = "Ctrl+Alt+Shift+Space";
    [JsonIgnore] public decimal SitCm => DeskProtocol.ToCm(SitRaw, BaseRaw);
    [JsonIgnore] public decimal StandCm => DeskProtocol.ToCm(StandRaw, BaseRaw);
    public void Validate()
    {
        if (Version != 1) throw new ArgumentException("Unsupported settings version.");
        if (Address != "D8:81:2F:0B:F2:7F") throw new ArgumentException("This release is configured for DESK 2079 only.");
        if (BaseRaw is < 0 or > 20000 || MinRaw < 0 || MaxRaw > ushort.MaxValue || MinRaw >= MaxRaw)
            throw new ArgumentException("Invalid calibration or operating range.");
        if (SitRaw < MinRaw || StandRaw > MaxRaw || SitRaw >= StandRaw)
            throw new ArgumentException("Sitting must be below standing, within the verified operating range.");
        if (new[] { SitKey, StandKey, StopKey }.Any(string.IsNullOrWhiteSpace) || new[] { SitKey, StandKey, StopKey }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new ArgumentException("Choose three different keyboard shortcuts.");
    }
}
