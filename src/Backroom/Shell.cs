using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Money;
using Il2CppTMPro;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Backroom;

/// <summary>
/// The Backroom window: header (wordmark, search, live readouts, close), the rail of pages with a gliding
/// selection, the page area with its breadcrumb, and the footer (key hints and toasts).
/// </summary>
internal sealed class Shell
{
    public RectTransform Canvas;
    public CanvasGroup Group;
    public RectTransform Window;
    public RectTransform PageArea;
    public TMP_InputField Search;

    private readonly List<Page> _pages;
    private readonly List<RailItem> _rail = new();
    private RectTransform _railSelect;
    private float _railSelectY, _railSelectFrom, _railSelectT = 1f;
    private TextMeshProUGUI _breadcrumb, _counter, _readouts;
    private float _nextReadout;

    private RectTransform _toast;
    private CanvasGroup _toastGroup;
    private TextMeshProUGUI _toastText;
    private Image _toastIcon;
    private float _toastUntil;

    public int Current { get; private set; } = -1;

    private sealed class RailItem
    {
        public RectTransform Rect;
        public Image Icon;
        public TextMeshProUGUI Label;
    }

    public Shell(List<Page> pages) => _pages = pages;

    public void Build(Transform parent)
    {
        // Full-screen canvas: always on top of the game's UI, scaled from 1920x1080.
        var go = new GameObject("PocketPlug Backroom");
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        Group = go.AddComponent<CanvasGroup>();
        Canvas = go.GetComponent<RectTransform>();

        // The live world stays visible behind a light dim; the backdrop also catches stray clicks.
        var backdrop = K.Box("Backdrop", Canvas, Theme.Backdrop);
        K.Fill(backdrop.rectTransform);
        backdrop.raycastTarget = true;

        var shadow = K.Rect("Shadow", Canvas).gameObject.AddComponent<Image>();
        shadow.sprite = Util.Assets.Sprite("br_shadow", new Vector4(64, 64, 64, 64));
        shadow.type = Image.Type.Sliced;
        shadow.color = new Color(0, 0, 0, 0.55f);
        shadow.raycastTarget = false;
        K.At(shadow.rectTransform, 0.5f, 0.5f, Theme.WindowWidth + 96, Theme.WindowHeight + 96);
        shadow.rectTransform.pivot = new Vector2(0.5f, 0.5f);

        var window = K.Box("Window", Canvas, Theme.Window, Theme.WindowRadius);
        window.raycastTarget = true;
        Window = window.rectTransform;
        K.At(Window, 0.5f, 0.5f, Theme.WindowWidth, Theme.WindowHeight);
        Window.pivot = new Vector2(0.5f, 0.5f);
        shadow.rectTransform.SetParent(Window, true);

        BuildHeader();
        BuildRail();
        BuildFooter();

        PageArea = K.Rect("Pages", Window);
        K.Fill(PageArea, Theme.RailWidth, Theme.HeaderHeight + 52, 0, Theme.FooterHeight);

        // Breadcrumb bar above the page.
        var crumbBar = K.Rect("Breadcrumb", Window);
        K.TopStrip(crumbBar, 52, Theme.HeaderHeight, Theme.RailWidth + 24, 24);
        _breadcrumb = K.Label("Path", crumbBar, "", Theme.Small, Theme.Text2, Theme.SemiBold);
        _breadcrumb.characterSpacing = 6;
        K.Fill(_breadcrumb.rectTransform);
        _counter = K.Label("Counter", crumbBar, "", Theme.Small, Theme.Text3, Theme.Medium, TextAlignmentOptions.MidlineRight);
        K.Fill(_counter.rectTransform);
    }

    private void BuildHeader()
    {
        var header = K.Rect("Header", Window);
        K.TopStrip(header, Theme.HeaderHeight);

        var overline = K.Label("Overline", header, "POCKETPLUG", Theme.Overline, Theme.Text2, Theme.SemiBold);
        overline.characterSpacing = 30;
        K.At(overline.rectTransform, 0, 1, 260, Theme.Line(Theme.Overline), 28, -14);
        var mark = K.Label("Wordmark", header, "BACKROOM", Theme.Wordmark, Theme.Text, Theme.Bold);
        mark.characterSpacing = 12;
        K.At(mark.rectTransform, 0, 1, 320, Theme.Line(Theme.Wordmark), 26, -28);

        Search = Widgets.SearchField(header, "Search everything");
        Search.onFocusSelectAll = false;
        K.At(Search.GetComponent<RectTransform>(), 0.5f, 0.5f, 460, 44, -40, 0);
        Search.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);

        var close = Widgets.IconButton(header, "x", 40, () => Backroom.Close());
        K.At(close, 1, 0.5f, 40, 40, -22, 0);
        _readouts = K.Label("Readouts", header, "", Theme.Small, Theme.Text2, Theme.Medium, TextAlignmentOptions.MidlineRight);
        K.At(_readouts.rectTransform, 1, 0.5f, 360, 30, -76, 0);

        var rule = K.Box("Rule", Window, Theme.Divider);
        K.TopStrip(rule.rectTransform, 1, Theme.HeaderHeight);
    }

    private void BuildRail()
    {
        var rail = K.Box("Rail", Window, Theme.Panel);
        var r = rail.rectTransform;
        r.anchorMin = new Vector2(0, 0);
        r.anchorMax = new Vector2(0, 1);
        r.pivot = new Vector2(0, 0.5f);
        r.offsetMin = new Vector2(0, Theme.FooterHeight);
        r.offsetMax = new Vector2(Theme.RailWidth, -Theme.HeaderHeight - 1);

        // The white selection sits behind the items and glides between them.
        var select = K.Box("Selection", r, Theme.Select, Theme.Radius);
        _railSelect = select.rectTransform;
        K.TopStrip(_railSelect, 48, 12, 12, 12);

        for (int i = 0; i < _pages.Count; i++)
        {
            int index = i;
            var page = _pages[i];
            var bg = K.Box(page.Id, r, Color.white, Theme.Radius);
            K.TopStrip(bg.rectTransform, 48, 12 + i * 52, 12, 12);
            K.Clickable(bg, () => Backroom.Show(index), new Color(1, 1, 1, 0), Theme.Raised, Theme.RaisedHover);
            var icon = K.Glyph("Icon", bg.rectTransform, page.Icon, 22, Theme.Text2);
            K.At(icon.rectTransform, 0, 0.5f, 22, 22, 16, 0);
            var label = K.Label("Label", bg.rectTransform, page.Title, Theme.Body - 2, Theme.Text, Theme.Medium);
            K.Fill(label.rectTransform, 52, 0, 8, 0);
            _rail.Add(new RailItem { Rect = bg.rectTransform, Icon = icon, Label = label });
        }
        _railSelect.SetAsFirstSibling();
        // The rail's own background must stay behind the selection.
        _railSelect.SetSiblingIndex(0);
    }

    private void BuildFooter()
    {
        var footer = K.Rect("Footer", Window);
        K.BottomStrip(footer, Theme.FooterHeight);
        var rule = K.Box("Rule", footer, Theme.Divider);
        K.TopStrip(rule.rectTransform, 1);

        var hints = K.Rect("Hints", footer);
        K.Fill(hints, 0, 0, 520, 0);
        float x = 24;
        x = Widgets.KeyHint(hints, x, "↑↓", "Navigate");
        x = Widgets.KeyHint(hints, x, "Enter", "Select");
        x = Widgets.KeyHint(hints, x, "Backspace", "Back");
        x = Widgets.KeyHint(hints, x, "Type", "Search");
        Widgets.KeyHint(hints, x, "Esc", "Close");

        _toast = K.Rect("Toast", footer);
        K.At(_toast, 1, 0.5f, 500, 34, -20, 0);
        _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
        _toastGroup.alpha = 0;
        _toastIcon = K.Glyph("Icon", _toast, "check", 18, Theme.Ok);
        K.At(_toastIcon.rectTransform, 1, 0.5f, 18, 18, 0, 0);
        _toastText = K.Label("Text", _toast, "", Theme.Small, Theme.Text, Theme.Medium, TextAlignmentOptions.MidlineRight);
        K.Fill(_toastText.rectTransform, 0, 0, 28, 0);
    }

    /// <summary>Builds every page now (behind the loading screen), hidden.</summary>
    public void BuildAll()
    {
        foreach (var page in _pages)
        {
            if (page.Built)
                continue;
            page.Root = K.Fill(K.Rect(page.Id, PageArea));
            try
            {
                page.Build(page.Root);
            }
            catch (System.Exception e)
            {
                Core.Log.Error($"Backroom: building the {page.Title} page failed: {e}");
            }
            page.Built = true;
            page.Root.gameObject.SetActive(false);
        }
    }

    /// <summary>Switches the visible page (builds it the first time).</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= _pages.Count)
            return;
        if (Current >= 0 && Current != index)
        {
            var old = _pages[Current];
            old.Root.gameObject.SetActive(false);
            old.OnHide();
        }

        var page = _pages[index];
        if (!page.Built)
        {
            page.Root = K.Fill(K.Rect(page.Id, PageArea));
            page.Build(page.Root);
            page.Built = true;
        }
        page.Root.gameObject.SetActive(true);
        bool changed = Current != index;
        Current = index;
        page.OnShow();

        for (int i = 0; i < _rail.Count; i++)
        {
            bool on = i == index;
            _rail[i].Label.color = on ? Theme.OnSelect : Theme.Text;
            _rail[i].Label.font = on ? Theme.SemiBold : Theme.Medium;
            _rail[i].Icon.color = on ? Theme.OnSelect : Theme.Text2;
        }
        float y = -(12 + index * 52);
        if (changed)
        {
            _railSelectFrom = _railSelect.anchoredPosition.y;
            _railSelectY = y;
            _railSelectT = 0f;
        }
        RefreshCrumb();
    }

    /// <summary>Jumps the rail selection to its target without animating (used when the menu opens).</summary>
    public void SnapRail()
    {
        _railSelectT = 1f;
        _railSelect.anchoredPosition = new Vector2(_railSelect.anchoredPosition.x, _railSelectY);
    }

    public void RefreshCrumb()
    {
        if (Current < 0)
            return;
        var page = _pages[Current];
        _breadcrumb.text = page.Breadcrumb;
        _counter.text = page.Counter;
    }

    public void Toast(string text, bool ok = true)
    {
        _toastText.text = text;
        _toastIcon.sprite = Util.Assets.Sprite(ok ? "br_check" : "br_x");
        _toastIcon.color = ok ? Theme.Ok : Theme.Danger;
        _toastUntil = Time.unscaledTime + Theme.ToastTime;
        _toastGroup.alpha = 1f;
    }

    public void Tick()
    {
        float now = Time.unscaledTime;
        if (_railSelectT < 1f)
        {
            _railSelectT = Mathf.Min(1f, _railSelectT + Time.unscaledDeltaTime / Theme.SelectTime);
            float y = Mathf.Lerp(_railSelectFrom, _railSelectY, Ease(_railSelectT));
            _railSelect.anchoredPosition = new Vector2(_railSelect.anchoredPosition.x, y);
        }

        if (_toastGroup.alpha > 0f && now > _toastUntil)
            _toastGroup.alpha = Mathf.Max(0f, _toastGroup.alpha - Time.unscaledDeltaTime / 0.25f);

        if (now >= _nextReadout)
        {
            _nextReadout = now + 0.5f;
            _readouts.text = Readouts();
        }
    }

    public void RefreshReadouts() => _nextReadout = 0f;

    private static string Readouts()
    {
        string money = "";
        if (NetworkSingleton<MoneyManager>.InstanceExists)
        {
            var mm = NetworkSingleton<MoneyManager>.Instance;
            money = $"{K.Money(mm.cashBalance)}   <color=#5F656D>Bank</color> {K.Money(mm.sync___get_value_onlineBalance())}";
        }
        string time = "";
        if (NetworkSingleton<TimeManager>.InstanceExists)
        {
            var tm = NetworkSingleton<TimeManager>.Instance;
            int hhmm = tm.CurrentTime;
            int h = hhmm / 100, m = hhmm % 100;
            string ampm = h >= 12 ? "PM" : "AM";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            time = $"   <color=#5F656D>·</color>   {tm.CurrentDay} {h12}:{m:00} {ampm}";
        }
        return money + time;
    }

    /// <summary>Ease-out cubic.</summary>
    public static float Ease(float t) => 1f - Mathf.Pow(1f - t, 3f);
}
