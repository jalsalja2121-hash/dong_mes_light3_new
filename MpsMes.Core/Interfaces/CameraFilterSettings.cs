namespace MpsMes.Core.Interfaces;

/// <summary>Immutable filter settings shared by preview and inspection workers.</summary>
public sealed record CameraFilterSettings
{
    public bool HsvEnabled { get; init; } = true;
    public bool ClaheEnabled { get; init; } = true;
    public bool GammaEnabled { get; init; } = true;
    public int HueMin { get; init; } = 0;
    public int HueMax { get; init; } = 179;
    public int SaturationMin { get; init; } = 0;
    public int SaturationMax { get; init; } = 40;
    public int ValueMin { get; init; } = 245;
    public int ValueMax { get; init; } = 255;
    public bool CannyEnabled { get; init; }
    public bool ApplyToInspection { get; init; }
}
