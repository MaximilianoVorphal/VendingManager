namespace VendingManager.Tests.Viewport;

/// <summary>
/// Viewport device profiles matching the user device matrix.
/// Each profile defines a target viewport with orientation support.
/// </summary>
public static class ViewportConfig
{
    /// <summary>PC 24" at 1080p — 1920×1080, no orientation.</summary>
    public static readonly ViewportProfile Desktop1080p = new(
        Name: "Desktop 1080p",
        Width: 1920,
        Height: 1080,
        DeviceScaleFactor: 1.0,
        IsMobile: false
    );

    /// <summary>Thinkpad 14" — 1200px CSS width, ~800px height.</summary>
    public static readonly ViewportProfile Laptop14in = new(
        Name: "Laptop 14\"",
        Width: 1200,
        Height: 800,
        DeviceScaleFactor: 1.0,
        IsMobile: false
    );

    /// <summary>iPad Pro 11" portrait — 834×1194.</summary>
    public static readonly ViewportProfile IPadPro11Portrait = new(
        Name: "iPad Pro 11\" Portrait",
        Width: 834,
        Height: 1194,
        DeviceScaleFactor: 2.0,
        IsMobile: true
    );

    /// <summary>iPad Pro 11" landscape — 1194×834.</summary>
    public static readonly ViewportProfile IPadPro11Landscape = new(
        Name: "iPad Pro 11\" Landscape",
        Width: 1194,
        Height: 834,
        DeviceScaleFactor: 2.0,
        IsMobile: true
    );

    /// <summary>iPhone portrait — 390×844.</summary>
    public static readonly ViewportProfile IPhonePortrait = new(
        Name: "iPhone Portrait",
        Width: 390,
        Height: 844,
        DeviceScaleFactor: 3.0,
        IsMobile: true
    );

    /// <summary>iPhone landscape — 844×390.</summary>
    public static readonly ViewportProfile IPhoneLandscape = new(
        Name: "iPhone Landscape",
        Width: 844,
        Height: 390,
        DeviceScaleFactor: 3.0,
        IsMobile: true
    );

    /// <summary>All profiles for parametrized tests.</summary>
    public static IReadOnlyList<ViewportProfile> AllProfiles =>
    [
        Desktop1080p,
        Laptop14in,
        IPadPro11Portrait,
        IPadPro11Landscape,
        IPhonePortrait,
        IPhoneLandscape,
    ];
}

/// <summary>
/// A single viewport profile for responsive testing.
/// </summary>
/// <param name="Name">Human-readable device name.</param>
/// <param name="Width">Viewport width in CSS pixels.</param>
/// <param name="Height">Viewport height in CSS pixels.</param>
/// <param name="DeviceScaleFactor">Device pixel ratio (2.0 for most tablets, 3.0 for phones).</param>
/// <param name="IsMobile">Whether this is a mobile/form-factor device.</param>
public record ViewportProfile(
    string Name,
    int Width,
    int Height,
    double DeviceScaleFactor,
    bool IsMobile
)
{
    /// <summary>Returns "(width × height)" for test naming.</summary>
    public string Dimensions => $"{Width}×{Height}";
}