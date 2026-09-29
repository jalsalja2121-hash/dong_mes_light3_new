namespace MpsMes.Core.Interfaces;

/// <summary>Immutable ON/OFF settings shared by preview and inspection workers.</summary>
public sealed record CameraFilterSettings
{
    public bool HsvEnabled { get; init; } = true;
    public bool CannyEnabled { get; init; }
    public bool ApplyToInspection { get; init; }
}
