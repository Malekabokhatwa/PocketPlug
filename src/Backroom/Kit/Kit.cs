using System;
using Il2CppTMPro;
using PocketPlug.Util;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Backroom.Kit;

/// <summary>
/// Low-level building blocks for Backroom's UI: rects, rounded boxes, TMP labels, glyphs and clickable areas.
/// Every widget is built from these, so look and behaviour stay consistent.
/// </summary>
internal static class K
{
    private static Sprite _round;

    /// <summary>The rounded-rect sprite (32 px corners, 9-sliced); any radius is drawn with pixelsPerUnitMultiplier.</summary>
    public static Sprite Round => _round != null ? _round : _round = Assets.Sprite("br_round", new Vector4(32, 32, 32, 32));

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    public static RectTransform Fill(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    /// <summary>Anchors to one point of the parent (0..1 each axis) with a fixed size and offset.</summary>
    public static RectTransform At(RectTransform rect, float ax, float ay, float width, float height, float x = 0, float y = 0)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(ax, ay);
        rect.pivot = new Vector2(ax, ay);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
        return rect;
    }

    /// <summary>A strip along the top of the parent: full width, fixed height, <paramref name="top"/> from the top edge.</summary>
    public static RectTransform TopStrip(RectTransform rect, float height, float top = 0, float left = 0, float right = 0)
    {
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    public static RectTransform BottomStrip(RectTransform rect, float height, float bottom = 0, float left = 0, float right = 0)
    {
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, bottom + height);
        return rect;
    }

    /// <summary>A flat or rounded filled box. Radius 0 draws a plain rectangle.</summary>
    public static Image Box(string name, Transform parent, Color color, float radius = 0)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        SetRadius(img, radius);
        return img;
    }

    public static void SetRadius(Image img, float radius)
    {
        if (radius <= 0)
        {
            img.sprite = null;
            img.type = Image.Type.Simple;
            return;
        }
        img.sprite = Round;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 32f / radius;
    }

    public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color,
        TMP_FontAsset font = null, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var t = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font ?? Theme.Regular;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.text = text;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.richText = true;
        return t;
    }

    /// <summary>A white Lucide glyph (assets/icons/br_*.png) tinted to <paramref name="color"/>.</summary>
    public static Image Glyph(string name, Transform parent, string icon, float size, Color color)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = Assets.Sprite("br_" + icon);
        img.color = color;
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        return img;
    }

    /// <summary>
    /// Makes <paramref name="target"/> clickable with hover and press colours. The graphic's own colour is set to
    /// white and the states carry the real colours, so Unity's tinting gives exact values.
    /// Navigation is always None: a selected button would also fire on the game's Submit input (gotcha 6).
    /// </summary>
    public static Button Clickable(Image target, Action onClick, Color normal, Color hover, Color pressed)
    {
        target.raycastTarget = true;
        target.color = Color.white;
        var button = target.gameObject.AddComponent<Button>();
        button.targetGraphic = target;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        SetColors(button, normal, hover, pressed);
        if (onClick != null)
            button.onClick.AddListener(onClick);
        return button;
    }

    public static void SetColors(Button button, Color normal, Color hover, Color pressed)
    {
        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = hover;
        colors.pressedColor = pressed;
        colors.selectedColor = normal;
        colors.disabledColor = new Color(normal.r, normal.g, normal.b, normal.a * 0.4f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    /// <summary>
    /// Colours for a clickable that sits on the white selection: its own hover/press fills would cover the
    /// selection (dark text on a dark box), so hover stays clear and press only darkens faintly.
    /// </summary>
    public static void OnSelection(Button button) =>
        SetColors(button, new Color(1, 1, 1, 0), new Color(1, 1, 1, 0), new Color(0, 0, 0, 0.06f));

    /// <summary>Colours for an ordinary (unselected) row or rail item.</summary>
    public static void OffSelection(Button button) =>
        SetColors(button, new Color(1, 1, 1, 0), Theme.Raised, Theme.RaisedHover);

    private static float _lastCommit = -10f;

    /// <summary>
    /// True at most once per 0.25 s. Wrap anything that changes the game (gives, money, spawns) so a double click,
    /// or a key and a click in the same moment, can't run it twice.
    /// </summary>
    public static bool Commit()
    {
        float now = Time.unscaledTime;
        if (now - _lastCommit < 0.25f)
            return false;
        _lastCommit = now;
        return true;
    }

    /// <summary>"GranddaddyPurple" / "Street_Rat" → "Granddaddy Purple" / "Street Rat".</summary>
    public static string Pretty(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_')
            {
                sb.Append(' ');
                continue;
            }
            if (char.IsUpper(c) && i > 0 && name[i - 1] != '_' && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The game's HHMM time as "3:40 PM".</summary>
    public static string Clock(int hhmm)
    {
        int h = hhmm / 100, m = hhmm % 100;
        return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
    }

    /// <summary>Shortens an amount for display: 1,250 / 12.5K / 3.2M.</summary>
    public static string Money(float amount)
    {
        float a = Mathf.Abs(amount);
        string s = a >= 1_000_000f ? $"{a / 1_000_000f:0.#}M" : a >= 100_000f ? $"{a / 1000f:0}K" : $"{a:N0}";
        return (amount < 0 ? "-$" : "$") + s;
    }
}
