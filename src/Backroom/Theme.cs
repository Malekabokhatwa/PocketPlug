using Il2CppTMPro;
using UnityEngine;

namespace PocketPlug.Backroom;

/// <summary>
/// Backroom's design tokens: the only place colours, sizes, fonts and timings are defined.
/// Monochrome graphite with a single ice-white highlight; sizes are at the 1920x1080 reference resolution.
/// </summary>
internal static class Theme
{
    // Surfaces
    public static readonly Color Backdrop = Hex(0x000000, 0.40f);
    public static readonly Color Window = Hex(0x0F1012, 0.97f);
    public static readonly Color Panel = Hex(0x17191C);
    public static readonly Color Raised = Hex(0x202328);
    public static readonly Color RaisedHover = Hex(0x272A30);
    public static readonly Color Divider = Hex(0x2A2E34);

    // Text
    public static readonly Color Text = Hex(0xE9EBEE);
    public static readonly Color Text2 = Hex(0x9AA0A6);
    public static readonly Color Text3 = Hex(0x5F656D);

    // The one highlight: a near-white selection with dark text on it.
    public static readonly Color Select = Hex(0xF2F3F5);
    public static readonly Color OnSelect = Hex(0x0F1012);
    public static readonly Color OnSelect2 = Hex(0x4A4F57);

    // Status: small dots and toasts only.
    public static readonly Color Ok = Hex(0x7DBA94);
    public static readonly Color Warn = Hex(0xD4A64A);
    public static readonly Color Danger = Hex(0xD46A6A);

    // Type scale
    public const float Wordmark = 34, Overline = 12, Title = 26, Body = 20, Small = 16, Caption = 14;

    // Layout
    public const float WindowWidth = 1360, WindowHeight = 800, WindowRadius = 12;
    public const float HeaderHeight = 84, FooterHeight = 48, RailWidth = 232, DetailWidth = 420;
    public const float RowHeight = 52, Gap = 8, Pad = 16, Radius = 8, SmallRadius = 6;

    // Motion (seconds, unscaled)
    public const float OpenTime = 0.14f, SelectTime = 0.09f, PageTime = 0.10f, ToastTime = 2.5f;
    public const float OpenRise = 12f;

    public static TMP_FontAsset Regular, Medium, SemiBold, Bold;

    /// <summary>
    /// The height a single line of text needs at <paramref name="size"/> (OpenSans line height is ~1.36 em).
    /// TMP's ellipsis mode drops all text from a box shorter than this, so every label box uses at least this.
    /// </summary>
    public static float Line(float size) => Mathf.Ceil(size * 1.45f);

    /// <summary>Finds the game's OpenSans font assets. Returns false if they aren't loaded yet.</summary>
    public static bool LoadFonts()
    {
        if (Regular != null && SemiBold != null)
            return true;
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            switch (font.name)
            {
                case "OpenSans-Regular SDF": Regular = font; break;
                case "OpenSans-Medium SDF": Medium = font; break;
                case "OpenSans-SemiBold SDF": SemiBold = font; break;
                case "OpenSans-Bold SDF": Bold = font; break;
            }
        }
        Medium ??= Regular;
        Bold ??= SemiBold;
        return Regular != null && SemiBold != null;
    }

    private static Color Hex(int rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
}
