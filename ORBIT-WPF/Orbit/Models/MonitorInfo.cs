namespace Orbit.Models;

/// <summary>
/// Screen bounds structure representing coordinates and dimensions in WPF device-independent units.
/// </summary>
public readonly record struct MonitorBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public bool Contains(double x, double y)
    {
        return x >= Left && x < Right && y >= Top && y < Bottom;
    }
}

/// <summary>
/// Detailed monitor metadata including DPI scaling and visible work area in DIP units.
/// </summary>
public sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required MonitorBounds Bounds { get; init; }
    public required MonitorBounds WorkArea { get; init; }
    public required bool IsPrimary { get; init; }
    public double DpiScaleX { get; init; } = 1.0;
    public double DpiScaleY { get; init; } = 1.0;

    public override string ToString() =>
        $"{DeviceName} (Primary: {IsPrimary}, WorkArea: [{WorkArea.Left},{WorkArea.Top},{WorkArea.Width}x{WorkArea.Height}], Scale: {DpiScaleX:0.##})";
}
