using System;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Util;

/// <summary>Small helpers for building Unity UI at runtime with the game's font.</summary>
internal static class Ui
{
    public static readonly Color Background = new(0.09f, 0.1f, 0.12f, 1f);
    public static readonly Color Card = new(0.15f, 0.16f, 0.19f, 1f);
    public static readonly Color TextMain = new(0.95f, 0.95f, 0.95f, 1f);
    public static readonly Color TextDim = new(0.62f, 0.64f, 0.68f, 1f);
    public static readonly Color Green = new(0.28f, 0.8f, 0.45f, 1f);
    public static readonly Color Red = new(0.9f, 0.36f, 0.33f, 1f);
    public static readonly Color Off = new(0.32f, 0.34f, 0.38f, 1f);

    /// <summary>The font used by the phone's home screen labels; set when the apps are created.</summary>
    public static Font Font;

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        return rect;
    }

    public static void Stretch(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    public static Image Image(string name, Transform parent, Color color, Sprite sprite = null)
    {
        var rect = Rect(name, parent);
        var img = rect.gameObject.AddComponent<Image>();
        img.color = color;
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    public static Text Text(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
    {
        var rect = Rect(name, parent);
        var t = rect.gameObject.AddComponent<Text>();
        t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    /// <summary>A flat rounded-looking button with a centered label.</summary>
    public static Button Button(string name, Transform parent, string label, Color color, Action onClick, int fontSize = 22)
    {
        var img = Image(name, parent, color);
        img.raycastTarget = true;
        var button = img.gameObject.AddComponent<Button>();
        // Never let the button become the selected UI object: a selected button also fires onClick on the game's
        // Submit input, so one click could run the action twice (this duplicated money in the Bank app).
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
        var text = Text("Label", img.transform, label, fontSize, TextMain, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        return button;
    }

    /// <summary>A vertical list that grows with its children, inside a scroll view filling <paramref name="parent"/>.</summary>
    public static RectTransform ScrollList(Transform parent, float top, float bottom, float spacing = 10f)
    {
        var viewport = Rect("Scroll", parent);
        Stretch(viewport, 0, top, 0, bottom);
        // A stencil Mask, not RectMask2D: phone apps are rotated 90 degrees, which RectMask2D can't clip correctly.
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = Color.white;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(24, 24, 8, 24);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return content;
    }

    /// <summary>A row of fixed height for use inside a layout group.</summary>
    public static RectTransform Row(string name, Transform parent, float height, Color? background = null)
    {
        RectTransform rect;
        if (background.HasValue)
            rect = Image(name, parent, background.Value).rectTransform;
        else
            rect = Rect(name, parent);
        var le = rect.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        return rect;
    }

    /// <summary>The standard app header: a title bar at the top of the page.</summary>
    public static Text Header(Transform root, string title, Color accent)
    {
        var bar = Image("Header", root, accent);
        var rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.offsetMin = new Vector2(0, -110);
        rect.offsetMax = Vector2.zero;
        var text = Text("Title", rect, title, 40, TextMain);
        Stretch(text.rectTransform, 32, 0, 32, 0);
        text.fontStyle = FontStyle.Bold;
        return text;
    }

    public static void Place(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
    {
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
