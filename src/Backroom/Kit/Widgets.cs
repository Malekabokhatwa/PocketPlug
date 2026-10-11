using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Backroom.Kit;

/// <summary>Composite widgets built from <see cref="K"/> primitives.</summary>
internal static class Widgets
{
    /// <summary>A rounded search box with a magnifier glyph. Built inactive and wired up before it's enabled.</summary>
    public static TMP_InputField SearchField(Transform parent, string placeholder) => TextField(parent, placeholder, "search");

    /// <summary>A rounded single-line text box with a leading glyph. Registered with Backroom so typing in it
    /// never reaches the game (GameInput.IsTyping) and the menu key types instead of closing.</summary>
    public static TMP_InputField TextField(Transform parent, string placeholder, string icon)
    {
        var box = K.Box("Search", parent, Color.white, Theme.Radius);
        box.gameObject.SetActive(false);
        var rect = box.rectTransform;

        var glyph = K.Glyph("Icon", rect, icon, 20, Theme.Text2);
        K.At(glyph.rectTransform, 0, 0.5f, 20, 20, 14, 0);

        var viewport = K.Rect("Viewport", rect);
        K.Fill(viewport, 44, 0, 14, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = K.Label("Text", viewport, "", Theme.Body - 2, Theme.Text);
        K.Fill(text.rectTransform);
        var hint = K.Label("Placeholder", viewport, placeholder, Theme.Body - 2, Theme.Text3);
        K.Fill(hint.rectTransform);

        var field = box.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = viewport;
        field.textComponent = text;
        field.placeholder = hint;
        field.fontAsset = Theme.Regular;
        field.pointSize = Theme.Body - 2;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.richText = false;
        field.customCaretColor = true;
        field.caretColor = Theme.Select;
        field.caretWidth = 2;
        field.selectionColor = new Color(1f, 1f, 1f, 0.22f);
        field.targetGraphic = box;
        box.raycastTarget = true;
        var nav = field.navigation;
        nav.mode = Navigation.Mode.None;
        field.navigation = nav;
        var colors = field.colors;
        colors.normalColor = Theme.Raised;
        colors.highlightedColor = Theme.RaisedHover;
        colors.pressedColor = Theme.RaisedHover;
        colors.selectedColor = Theme.RaisedHover;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        field.colors = colors;

        box.gameObject.SetActive(true);
        Backroom.RegisterField(field);
        return field;
    }

    /// <summary>A square, borderless button holding one glyph.</summary>
    public static RectTransform IconButton(Transform parent, string icon, float size, Action onClick)
    {
        var bg = K.Box("Button_" + icon, parent, Color.white, Theme.Radius);
        bg.rectTransform.sizeDelta = new Vector2(size, size);
        K.Clickable(bg, onClick, new Color(1, 1, 1, 0), Theme.Raised, Theme.RaisedHover);
        var glyph = K.Glyph("Glyph", bg.rectTransform, icon, size * 0.5f, Theme.Text2);
        K.At(glyph.rectTransform, 0.5f, 0.5f, size * 0.5f, size * 0.5f);
        glyph.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        return bg.rectTransform;
    }

    /// <summary>
    /// A footer hint, a keycap and what it does ("Enter  Select"), placed at <paramref name="x"/> from the left.
    /// Sizes come from the measured text (layout groups don't size runtime TMP text reliably on the first frame).
    /// Returns the x where the next hint goes.
    /// </summary>
    public static float KeyHint(RectTransform parent, float x, string key, string action)
    {
        var capText = K.Label("Key", parent, key, Theme.Caption, Theme.Text, Theme.SemiBold, TextAlignmentOptions.Center);
        float keyWidth = Mathf.Max(24f, capText.GetPreferredValues(key).x + 16f);
        var cap = K.Box("Cap_" + action, parent, Theme.Raised, Theme.SmallRadius);
        K.At(cap.rectTransform, 0, 0.5f, keyWidth, 24, x, 0);
        capText.rectTransform.SetParent(cap.rectTransform, false);
        K.Fill(capText.rectTransform);
        capText.overflowMode = TextOverflowModes.Overflow;

        var label = K.Label("Action_" + action, parent, action, Theme.Caption, Theme.Text2, Theme.Medium);
        float labelWidth = label.GetPreferredValues(action).x + 2f;
        K.At(label.rectTransform, 0, 0.5f, labelWidth, 24, x + keyWidth + 8, 0);
        label.overflowMode = TextOverflowModes.Overflow;
        return x + keyWidth + 8 + labelWidth + 22;
    }
}
