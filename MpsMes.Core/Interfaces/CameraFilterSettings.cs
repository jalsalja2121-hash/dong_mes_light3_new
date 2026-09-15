namespace MpsMes.Core.Interfaces;

/// <summary>Immutable snapshot shared by camera preview and inspection workers.</summary>
public sealed record CameraFilterSettings
{
    public bool HsvEnabled { get; init; } = true;
    public bool CannyEnabled { get; init; } = true;
    public bool ApplyToInspection { get; init; }
    public int HueMin { get; init; }
    public int HueMax { get; init; } = 179;
    public int SaturationMin { get; init; }
    public int SaturationMax { get; init; } = 255;
    public int ValueMin { get; init; }
    public int ValueMax { get; init; } = 255;
    public int CannyLow { get; init; } = 50;
    public int CannyHigh { get; init; } = 150;
}
