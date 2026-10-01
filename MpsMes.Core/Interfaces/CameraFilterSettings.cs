namespace MpsMes.Core.Interfaces;

/// <summary>Immutable filter settings shared by preview and inspection workers.</summary>
public sealed record CameraFilterSettings
{
    public bool HsvEnabled { get; init; } = true;
    public bool ClaheEnabled { get; init; } = true;
    public bool GammaEnabled { get; init; } = true;
    public bool CannyEnabled { get; init; }
    public bool ApplyToInspection { get; init; }
}
