using System;
using System.Collections.Generic;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Backroom.Kit;

/// <summary>A vertically scrolling area with a slim scrollbar; children go into <see cref="Content"/>.</summary>
internal sealed class ScrollArea
{
    public RectTransform Root, Viewport, Content;
    public ScrollRect Scroll;

    public ScrollArea(Transform parent, string name = "Scroll")
    {
        Root = K.Rect(name, parent);
        Viewport = K.Rect("Viewport", Root);
        K.Fill(Viewport, 0, 0, 10, 0);
        Viewport.gameObject.AddComponent<RectMask2D>();
        // An invisible graphic so the wheel and drags register anywhere in the area, not just over children.
        var hit = Viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);

        Content = K.Rect("Content", Viewport);
        Content.anchorMin = new Vector2(0, 1);
        Content.anchorMax = new Vector2(1, 1);
        Content.pivot = new Vector2(0.5f, 1);
        Content.offsetMin = Content.offsetMax = Vector2.zero;

        var bar = K.Rect("Scrollbar", Root);
        bar.anchorMin = new Vector2(1, 0);
        bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(1, 0.5f);
        bar.offsetMin = new Vector2(-6, 4);
        bar.offsetMax = new Vector2(0, -4);
        var sliding = K.Fill(K.Rect("Sliding", bar));
        var handle = K.Box("Handle", sliding, Theme.Divider, 3);
        K.Fill(handle.rectTransform);
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var nav = scrollbar.navigation;
        nav.mode = Navigation.Mode.None;
        scrollbar.navigation = nav;
        handle.raycastTarget = true;

        Scroll = Root.gameObject.AddComponent<ScrollRect>();
        Scroll.viewport = Viewport;
        Scroll.content = Content;
        Scroll.horizontal = false;
        Scroll.vertical = true;
        Scroll.movementType = ScrollRect.MovementType.Clamped;
        Scroll.scrollSensitivity = 40f;
        Scroll.inertia = true;
        Scroll.decelerationRate = 0.12f;
        Scroll.verticalScrollbar = scrollbar;
        Scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    /// <summary>
    /// Scrolls just enough to show the span [<paramref name="top"/>, <paramref name="bottom"/>] (distances from the
    /// top of the content). Callers compute the span themselves, so this never depends on layout having run.
    /// </summary>
    public void Reveal(float top, float bottom, float contentHeight, float margin = 12f)
    {
        float viewHeight = Viewport.rect.height;
        if (viewHeight <= 0 || contentHeight <= viewHeight)
        {
            Content.anchoredPosition = new Vector2(Content.anchoredPosition.x, 0);
            return;
        }
        float scroll = Content.anchoredPosition.y;
        if (top - margin < scroll)
            scroll = top - margin;
        else if (bottom + margin > scroll + viewHeight)
            scroll = bottom + margin - viewHeight;
        scroll = Mathf.Clamp(scroll, 0, contentHeight - viewHeight);
        Content.anchoredPosition = new Vector2(Content.anchoredPosition.x, scroll);
        Scroll.velocity = Vector2.zero;
    }
}

/// <summary>A row of selectable chips laid out left to right (wrapping), sized from their text.</summary>
internal sealed class ChipRow
{
    private readonly List<(Image bg, TextMeshProUGUI label)> _chips = new();
    private readonly Action<int> _onSelect;
    public int Selected { get; private set; }
    public float Height { get; }

    public ChipRow(RectTransform parent, IList<string> labels, Action<int> onSelect, float width)
    {
        _onSelect = onSelect;
        float x = 0, y = 0;
        const float h = 34, gap = 6;
        for (int i = 0; i < labels.Count; i++)
        {
            int index = i;
            var label = K.Label("Label", parent, labels[i], Theme.Small, Theme.Text2, Theme.Medium, TextAlignmentOptions.Center);
            float w = label.GetPreferredValues(labels[i]).x + 24;
            if (x > 0 && x + w > width)
            {
                x = 0;
                y += h + gap;
            }
            var bg = K.Box("Chip_" + labels[i], parent, Color.white, h / 2);
            K.At(bg.rectTransform, 0, 1, w, h, x, -y);
            label.rectTransform.SetParent(bg.rectTransform, false);
            K.Fill(label.rectTransform);
            K.Clickable(bg, () => Select(index, true), Theme.Raised, Theme.RaisedHover, Theme.Divider);
            _chips.Add((bg, label));
            x += w + gap;
        }
        Height = y + h;
        Select(0, false);
    }

    public void Select(int index, bool notify)
    {
        Selected = index;
        for (int i = 0; i < _chips.Count; i++)
        {
            bool on = i == index;
            var button = _chips[i].bg.GetComponent<Button>();
            if (on)
                K.SetColors(button, Theme.Select, Theme.Select, Theme.Select);
            else
                K.SetColors(button, Theme.Raised, Theme.RaisedHover, Theme.Divider);
            _chips[i].label.color = on ? Theme.OnSelect : Theme.Text2;
            _chips[i].label.font = on ? Theme.SemiBold : Theme.Medium;
        }
        if (notify)
            _onSelect?.Invoke(index);
    }
}

/// <summary>Equal-width options in a rounded track; the chosen one is a white pill. Options can carry a colour dot.</summary>
internal sealed class Segmented
{
    private readonly List<(Image bg, TextMeshProUGUI label, Image dot)> _items = new();
    private readonly Action<int> _onSelect;
    public RectTransform Root;
    public int Selected { get; private set; } = -1;

    public Segmented(RectTransform parent, IList<string> labels, Action<int> onSelect, IList<Color?> dots = null, float height = 40)
    {
        _onSelect = onSelect;
        var track = K.Box("Segmented", parent, Theme.Raised, Theme.Radius);
        Root = track.rectTransform;
        Root.sizeDelta = new Vector2(Root.sizeDelta.x, height);
        for (int i = 0; i < labels.Count; i++)
        {
            int index = i;
            var bg = K.Box("Option_" + labels[i], Root, Color.white, Theme.SmallRadius);
            bg.rectTransform.anchorMin = new Vector2((float)i / labels.Count, 0);
            bg.rectTransform.anchorMax = new Vector2((float)(i + 1) / labels.Count, 1);
            bg.rectTransform.offsetMin = new Vector2(3, 3);
            bg.rectTransform.offsetMax = new Vector2(-3, -3);
            K.Clickable(bg, () => Select(index, true), new Color(1, 1, 1, 0), Theme.RaisedHover, Theme.Divider);
            Color? dotColor = dots != null && i < dots.Count ? dots[i] : null;
            Image dot = null;
            float left = 6;
            if (dotColor.HasValue)
            {
                dot = K.Box("Dot", bg.rectTransform, dotColor.Value, 4);
                K.At(dot.rectTransform, 0, 0.5f, 8, 8, 10, 0);
                left = 22;
            }
            var label = K.Label("Label", bg.rectTransform, labels[i], Theme.Caption + 1, Theme.Text2, Theme.Medium, TextAlignmentOptions.Center);
            K.Fill(label.rectTransform, left, 0, 6, 0);
            label.enableAutoSizing = true;
            label.fontSizeMin = Theme.Caption;
            label.fontSizeMax = Theme.Caption + 1;
            _items.Add((bg, label, dot));
        }
    }

    public void Select(int index, bool notify)
    {
        if (index < 0 || index >= _items.Count)
            return;
        Selected = index;
        for (int i = 0; i < _items.Count; i++)
        {
            bool on = i == index;
            var button = _items[i].bg.GetComponent<Button>();
            if (on)
                K.SetColors(button, Theme.Select, Theme.Select, Theme.Select);
            else
                K.SetColors(button, new Color(1, 1, 1, 0), Theme.RaisedHover, Theme.Divider);
            _items[i].label.color = on ? Theme.OnSelect : Theme.Text2;
            _items[i].label.font = on ? Theme.SemiBold : Theme.Medium;
        }
        if (notify)
            _onSelect?.Invoke(index);
    }
}

/// <summary>
/// A "‹ value ›" picker in the 2019-menu style: arrows step through the options, clicking the value steps forward.
/// Options can carry a colour dot.
/// </summary>
internal sealed class Cycle
{
    private readonly IList<string> _labels;
    private readonly IList<Color?> _dots;
    private readonly Action<int> _onChange;
    private readonly TextMeshProUGUI _label;
    private readonly Image _dot;
    public RectTransform Root;
    public int Selected { get; private set; }

    public Cycle(RectTransform parent, IList<string> labels, Action<int> onChange, IList<Color?> dots = null, float height = 40)
    {
        _labels = labels;
        _dots = dots;
        _onChange = onChange;
        var track = K.Box("Cycle", parent, Color.white, Theme.Radius);
        Root = track.rectTransform;
        Root.sizeDelta = new Vector2(Root.sizeDelta.x, height);
        K.Clickable(track, () => Step(1), Theme.Raised, Theme.RaisedHover, Theme.Divider);
        var left = Widgets.IconButton(Root, "chevron-left", height, () => Step(-1));
        K.At(left, 0, 0.5f, height, height, 0, 0);
        var right = Widgets.IconButton(Root, "chevron-right", height, () => Step(1));
        K.At(right, 1, 0.5f, height, height, 0, 0);
        _dot = K.Box("Dot", Root, Color.white, 5);
        _label = K.Label("Value", Root, "", Theme.Small, Theme.Text, Theme.SemiBold);
        _label.overflowMode = TextOverflowModes.Overflow;
        Select(0, false);
    }

    public void Step(int delta) => Select((Selected + delta + _labels.Count) % _labels.Count, true);

    public void Select(int index, bool notify)
    {
        Selected = Mathf.Clamp(index, 0, _labels.Count - 1);
        string text = _labels[Selected];
        _label.text = text;
        Color? dot = _dots != null && Selected < _dots.Count ? _dots[Selected] : null;
        _dot.gameObject.SetActive(dot.HasValue);
        // Centre the dot and the text together.
        float textWidth = _label.GetPreferredValues(text).x;
        float total = textWidth + (dot.HasValue ? 18 : 0);
        float x = -total / 2;
        if (dot.HasValue)
        {
            _dot.color = dot.Value;
            K.At(_dot.rectTransform, 0.5f, 0.5f, 10, 10, x, 0);
            _dot.rectTransform.pivot = new Vector2(0, 0.5f);
            x += 18;
        }
        K.At(_label.rectTransform, 0.5f, 0.5f, textWidth + 4, Theme.Line(Theme.Small), x, 0);
        _label.rectTransform.pivot = new Vector2(0, 0.5f);
        if (notify)
            _onChange?.Invoke(Selected);
    }
}

internal static class Controls
{
    /// <summary>The primary action button: near-white with dark text. Use SetEnabled to grey it out with a reason.</summary>
    public static (Button button, TextMeshProUGUI label) Primary(RectTransform parent, string text, Action onClick, float height = 52)
    {
        var bg = K.Box("Primary", parent, Color.white, Theme.Radius);
        bg.rectTransform.sizeDelta = new Vector2(bg.rectTransform.sizeDelta.x, height);
        var button = K.Clickable(bg, onClick, Theme.Select, Color.white, Hex(0xD9DCE0));
        var label = K.Label("Label", bg.rectTransform, text, Theme.Body - 2, Theme.OnSelect, Theme.SemiBold, TextAlignmentOptions.Center);
        K.Fill(label.rectTransform, 12, 0, 12, 0);
        return (button, label);
    }

    public static void SetEnabled(Button button, TextMeshProUGUI label, bool enabled)
    {
        button.interactable = enabled;
        if (enabled)
            K.SetColors(button, Theme.Select, Color.white, Hex(0xD9DCE0));
        else
        {
            var colors = button.colors;
            colors.disabledColor = Theme.Raised;
            button.colors = colors;
        }
        label.color = enabled ? Theme.OnSelect : Theme.Text3;
    }

    /// <summary>A secondary button: raised grey, light text.</summary>
    public static (Button button, TextMeshProUGUI label) Secondary(RectTransform parent, string text, Action onClick, float height = 40)
    {
        var bg = K.Box("Secondary", parent, Color.white, Theme.Radius);
        bg.rectTransform.sizeDelta = new Vector2(bg.rectTransform.sizeDelta.x, height);
        var button = K.Clickable(bg, onClick, Theme.Raised, Theme.RaisedHover, Theme.Divider);
        var label = K.Label("Label", bg.rectTransform, text, Theme.Small, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
        K.Fill(label.rectTransform, 8, 0, 8, 0);
        return (button, label);
    }

    /// <summary>An uppercase section label ("QUALITY").</summary>
    public static TextMeshProUGUI Section(RectTransform parent, string text)
    {
        var label = K.Label("Section_" + text, parent, text.ToUpperInvariant(), Theme.Caption, Theme.Text3, Theme.SemiBold);
        label.characterSpacing = 8;
        return label;
    }

    /// <summary>
    /// Lays out pills (an optional colour dot and a label) left to right, wrapping at <paramref name="width"/>.
    /// Returns the total height. Old pills under <paramref name="parent"/> are removed first.
    /// </summary>
    public static float Pills(RectTransform parent, IList<(string text, Color? dot)> pills, float width)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        float x = 0, y = 0;
        const float h = 30, gap = 6;
        foreach (var (text, dot) in pills)
        {
            var label = K.Label("Text", parent, text, Theme.Caption, Theme.Text, Theme.Medium);
            float w = label.GetPreferredValues(text).x + (dot.HasValue ? 34 : 22);
            if (x > 0 && x + w > width)
            {
                x = 0;
                y += h + gap;
            }
            var bg = K.Box("Pill", parent, Theme.Raised, h / 2);
            K.At(bg.rectTransform, 0, 1, w, h, x, -y);
            label.rectTransform.SetParent(bg.rectTransform, false);
            K.Fill(label.rectTransform, dot.HasValue ? 24 : 11, 0, 8, 0);
            if (dot.HasValue)
            {
                var d = K.Box("Dot", bg.rectTransform, dot.Value, 4);
                K.At(d.rectTransform, 0, 0.5f, 8, 8, 11, 0);
            }
            x += w + gap;
        }
        return pills.Count == 0 ? 0 : y + h;
    }

    private static Color Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
