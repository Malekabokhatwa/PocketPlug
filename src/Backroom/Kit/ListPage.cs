using System;
using System.Collections.Generic;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PocketPlug.Backroom.Kit;

/// <summary>
/// The standard Backroom page: a list of rows on the left and an info box on the right that describes the
/// selected row (the 2019-menu "description box"). Rows are actions, toggles, ‹ value › steppers and choices,
/// and links that drill into a sub-list (Backspace goes back). The white selection bar glides between rows;
/// arrows move it, Left/Right change values, Enter runs. Destructive actions are held to confirm.
/// </summary>
internal abstract class ListPage : Page
{
    private const float ListWidth = Theme.WindowWidth - Theme.RailWidth - Theme.DetailWidth - 24 - 16 - 16;
    private const float SectionHeight = 40, HoldTime = 0.8f;

    protected internal abstract class Row
    {
        public string Title, Description;
        public RectTransform Rect;
        public Button Button;
        public TextMeshProUGUI Label, Value;
        public Image Icon;
        public float Y, Height = Theme.RowHeight;
        public bool Selectable = true;
        public bool IsSelected;
        public Func<bool> Enabled;
        public bool IsEnabled => Enabled == null || Enabled();
        public virtual void Activate() { }
        public virtual void Adjust(int direction) { }
        public virtual void Refresh() { }
    }

    private sealed class SectionRow : Row
    {
        public SectionRow() => Selectable = false;
    }

    private sealed class ActionRow : Row
    {
        public Action Run;
        public Func<string> Read;
        public bool Hold, IsReadout;
        public RectTransform Progress;
        public override void Activate()
        {
            if (Run != null && !Hold && IsEnabled && K.Commit())
            {
                Run();
                Backroom.Recent(Title, Run);
            }
        }
        public override void Refresh()
        {
            if (Read != null)
                Value.text = Read();
        }
    }

    private sealed class ToggleRow : Row
    {
        public Func<bool> Get;
        public Action<bool> Set;
        public Image Track, Knob;
        public override void Activate()
        {
            if (IsEnabled && K.Commit())
            {
                Set(!Get());
                Refresh();
            }
        }
        public override void Adjust(int direction)
        {
            bool want = direction > 0;
            if (IsEnabled && Get() != want)
                Activate();
        }
        public override void Refresh()
        {
            bool on = Get();
            // On the white selection bar the colours invert, so an "on" switch stays visible.
            Track.color = on ? (IsSelected ? Theme.OnSelect : Theme.Select) : (IsSelected ? Theme.OnSelect2 : Theme.Divider);
            Knob.color = on ? (IsSelected ? Theme.Select : Theme.OnSelect) : (IsSelected ? Theme.Select : Theme.Text2);
            K.At(Knob.rectTransform, on ? 1 : 0, 0.5f, 18, 18, on ? -4 : 4, 0);
        }
    }

    private sealed class StepRow : Row
    {
        public Func<float> Get;
        public Action<float> Set;
        public float Min, Max, Step;
        public Func<float, string> Format;
        public override void Adjust(int direction)
        {
            if (!IsEnabled)
                return;
            float step = Keyboard.current != null && Keyboard.current.shiftKey.isPressed ? Step * 10 : Step;
            float v = Mathf.Clamp(Get() + direction * step, Min, Max);
            Set(v);
            Refresh();
        }
        public override void Activate() => Adjust(1);
        public override void Refresh() => Value.text = Format(Get());
    }

    private sealed class ChoiceRow : Row
    {
        public Func<string[]> Options;
        public Func<int> Get;
        public Action<int> Set;
        public override void Adjust(int direction)
        {
            var options = Options();
            if (!IsEnabled || options.Length == 0)
                return;
            int i = Get();
            Set(((i < 0 ? 0 : i) + direction + options.Length) % options.Length);
            Refresh();
        }
        public override void Activate() => Adjust(1);
        public override void Refresh()
        {
            var options = Options();
            int i = Get();
            Value.text = i >= 0 && i < options.Length ? options[i] : "—";
        }
    }

    private sealed class LinkRow : Row
    {
        public ListPage Page;
        public string Path;
        public Action<ListPage> Define;
        public Func<string> Read;
        public override void Activate() => Page.Push(Path ?? Title, Define);
        public override void Refresh()
        {
            if (Read != null)
                Value.text = Read();
        }
    }

    private sealed class View
    {
        public string Title;
        public Action<ListPage> Define;
        public int Selected;
        public float Scroll;
    }

    private readonly List<View> _stack = new();
    private readonly List<Row> _rows = new();
    private readonly List<Row> _visible = new();
    private ScrollArea _scroll;
    private RectTransform _bar;
    private float _barFrom, _barTo, _barT = 1f, _barH = Theme.RowHeight;
    private int _selected = -1;
    private float _nextRefresh;
    private float _hold;
    private string _query = "";

    private TextMeshProUGUI _infoTitle, _infoBody, _infoHint;
    /// <summary>Space at the bottom of the info box for page-specific content (photos, live numbers).</summary>
    protected RectTransform InfoExtra;

    /// <summary>Defines the page's top-level rows (sections, actions...).</summary>
    protected abstract void Define();

    protected Row SelectedRow => _selected >= 0 && _selected < _visible.Count ? _visible[_selected] : null;

    public override string Breadcrumb
    {
        get
        {
            string path = Title.ToUpperInvariant();
            for (int i = 1; i < _stack.Count; i++)
                path += "  ›  " + _stack[i].Title.ToUpperInvariant();
            return _query.Length > 0 ? path + $"  ›  “{_query}”" : path;
        }
    }

    public override string Counter
    {
        get
        {
            int count = 0, position = 0;
            for (int i = 0; i < _visible.Count; i++)
            {
                if (!_visible[i].Selectable)
                    continue;
                count++;
                if (i <= _selected)
                    position++;
            }
            return count == 0 ? "" : $"{position} / {count}";
        }
    }

    protected internal override void Build(RectTransform root)
    {
        var left = K.Rect("List", root);
        K.Fill(left, 24, 0, Theme.DetailWidth + 24 + 16, 0);
        _scroll = new ScrollArea(left, "Rows");
        K.Fill(_scroll.Root);
        var bar = K.Box("Selection", _scroll.Content, Theme.Select, Theme.Radius);
        _bar = bar.rectTransform;
        _bar.anchorMin = new Vector2(0, 1);
        _bar.anchorMax = new Vector2(1, 1);
        _bar.pivot = new Vector2(0.5f, 1);

        var info = K.Box("Info", root, Theme.Panel, 10);
        var r = info.rectTransform;
        r.anchorMin = new Vector2(1, 0);
        r.anchorMax = new Vector2(1, 1);
        r.pivot = new Vector2(1, 0.5f);
        r.offsetMin = new Vector2(-Theme.DetailWidth - 16, 16);
        r.offsetMax = new Vector2(-16, 0);
        float inner = Theme.DetailWidth - 48;
        _infoTitle = K.Label("Title", r, "", Theme.Title - 2, Theme.Text, Theme.SemiBold, TextAlignmentOptions.TopLeft);
        _infoTitle.enableWordWrapping = true;
        K.At(_infoTitle.rectTransform, 0, 1, inner, 2 * Theme.Line(Theme.Title - 2), 24, -22);
        _infoBody = K.Label("Body", r, "", Theme.Small, Theme.Text2, Theme.Regular, TextAlignmentOptions.TopLeft);
        _infoBody.enableWordWrapping = true;
        _infoBody.overflowMode = TextOverflowModes.Overflow;
        K.At(_infoBody.rectTransform, 0, 1, inner, 200, 24, -64);
        _infoHint = K.Label("Hint", r, "", Theme.Caption, Theme.Text3, Theme.Medium, TextAlignmentOptions.BottomLeft);
        K.At(_infoHint.rectTransform, 0, 0, inner, Theme.Line(Theme.Caption), 24, 20);
        InfoExtra = K.Rect("Extra", r);
        K.Fill(InfoExtra, 24, 280, 24, 52);

        _stack.Clear();
        _stack.Add(new View { Title = Title, Define = _ => Define() });
        Rebuild();
    }

    // ---- defining rows (called from Define or a link's define) ----

    protected void Section(string title)
    {
        var row = new SectionRow { Title = title, Height = SectionHeight };
        row.Rect = K.Rect("Section_" + title, _scroll.Content);
        var label = Controls.Section(row.Rect, title);
        K.At(label.rectTransform, 0, 0, ListWidth, Theme.Line(Theme.Caption), 14, 6);
        row.Label = label;
        Add(row);
    }

    protected void Action(string title, string description, Action run, string icon = null, Func<string> value = null,
        bool hold = false, Func<bool> enabled = null)
    {
        var row = new ActionRow { Title = title, Description = description, Run = run, Read = value, Hold = hold, Enabled = enabled };
        Frame(row, icon, hold ? "Hold" : null, value != null);
        if (hold)
        {
            var p = K.Box("Progress", row.Rect, Theme.Danger, Theme.Radius);
            row.Progress = p.rectTransform;
            row.Progress.anchorMin = Vector2.zero;
            row.Progress.anchorMax = new Vector2(0, 1);
            row.Progress.offsetMin = row.Progress.offsetMax = Vector2.zero;
            p.color = new Color(Theme.Danger.r, Theme.Danger.g, Theme.Danger.b, 0.35f);
            row.Progress.SetAsFirstSibling();
        }
        else if (value == null)
        {
            var arrow = K.Glyph("Arrow", row.Rect, "chevron-right", 18, Theme.Text3);
            K.At(arrow.rectTransform, 1, 0.5f, 18, 18, -14, 0);
        }
        Add(row);
    }

    /// <summary>A row that only shows a live value (health, cash...).</summary>
    protected void Readout(string title, string description, Func<string> value, string icon = null)
    {
        var row = new ActionRow { Title = title, Description = description, Run = null, Read = value, IsReadout = true };
        Frame(row, icon, null, true);
        Add(row);
    }

    protected void Toggle(string title, string description, Func<bool> get, Action<bool> set, string icon = null, Func<bool> enabled = null)
    {
        var row = new ToggleRow { Title = title, Description = description, Get = get, Set = set, Enabled = enabled };
        Frame(row, icon, null, false);
        row.Track = K.Box("Track", row.Rect, Theme.Divider, 13);
        K.At(row.Track.rectTransform, 1, 0.5f, 48, 26, -14, 0);
        row.Knob = K.Box("Knob", row.Track.rectTransform, Theme.Text2, 9);
        // Clicking the switch flips it; clicking the rest of the row only selects it.
        row.Track.raycastTarget = true;
        var flip = row.Track.gameObject.AddComponent<Button>();
        flip.transition = UnityEngine.UI.Selectable.Transition.None;
        var nav = flip.navigation;
        nav.mode = Navigation.Mode.None;
        flip.navigation = nav;
        var captured = row;
        flip.onClick.AddListener((Action)(() =>
        {
            SelectRow(captured);
            captured.Activate();
        }));
        Add(row);
    }

    protected void Stepper(string title, string description, Func<float> get, Action<float> set, float min, float max, float step,
        Func<float, string> format, string icon = null, Func<bool> enabled = null)
    {
        var row = new StepRow { Title = title, Description = description, Get = get, Set = set, Min = min, Max = max, Step = step, Format = format, Enabled = enabled };
        Frame(row, icon, null, true, adjustable: true);
        Add(row);
    }

    protected void Choice(string title, string description, Func<string[]> options, Func<int> get, Action<int> set,
        string icon = null, Func<bool> enabled = null)
    {
        var row = new ChoiceRow { Title = title, Description = description, Options = options, Get = get, Set = set, Enabled = enabled };
        Frame(row, icon, null, true, adjustable: true);
        Add(row);
    }

    protected void Link(string title, string description, Action<ListPage> define, string icon = null, Func<string> value = null, string path = null)
    {
        var row = new LinkRow { Title = title, Description = description, Page = this, Define = define, Read = value, Path = path };
        Frame(row, icon, null, value != null);
        var arrow = K.Glyph("Arrow", row.Rect, "chevron-right", 18, Theme.Text3);
        K.At(arrow.rectTransform, 1, 0.5f, 18, 18, -14, 0);
        if (row.Value != null)
            row.Value.rectTransform.anchoredPosition = new Vector2(-40, 0);
        Add(row);
    }

    /// <summary>Common row chrome: clickable background, optional icon, title, optional value text on the right.</summary>
    private void Frame(Row row, string icon, string tag, bool hasValue, bool adjustable = false)
    {
        var bg = K.Box("Row_" + row.Title, _scroll.Content, Color.white, Theme.Radius);
        row.Rect = bg.rectTransform;
        var captured = row;
        row.Button = K.Clickable(bg, () => Click(captured), new Color(1, 1, 1, 0), Theme.Raised, Theme.RaisedHover);
        float left = 16;
        if (icon != null)
        {
            row.Icon = K.Glyph("Icon", row.Rect, icon, 20, Theme.Text2);
            K.At(row.Icon.rectTransform, 0, 0.5f, 20, 20, 16, 0);
            left = 50;
        }
        row.Label = K.Label("Title", row.Rect, row.Title, Theme.Body - 1, Theme.Text, Theme.Medium);
        K.Fill(row.Label.rectTransform, left, 0, hasValue || tag != null ? 220 : 48, 0);
        if (hasValue || tag != null)
        {
            row.Value = K.Label("Value", row.Rect, tag ?? "", Theme.Small, Theme.Text2, Theme.Medium,
                adjustable ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineRight);
            K.At(row.Value.rectTransform, 1, 0.5f, adjustable ? 168 : 200, Theme.Line(Theme.Small), adjustable ? -46 : -16, 0);
        }
        if (adjustable)
        {
            var less = Widgets.IconButton(row.Rect, "chevron-left", 36, () => { SelectRow(captured); captured.Adjust(-1); });
            K.At(less, 1, 0.5f, 36, 36, -250, 0);
            var more = Widgets.IconButton(row.Rect, "chevron-right", 36, () => { SelectRow(captured); captured.Adjust(1); });
            K.At(more, 1, 0.5f, 36, 36, -6, 0);
        }
    }

    private void Add(Row row) => _rows.Add(row);

    // ---- navigation ----

    /// <summary>Drills into a sub-list (a link's rows).</summary>
    public void Push(string title, Action<ListPage> define)
    {
        _stack[^1].Selected = _selected;
        _stack[^1].Scroll = _scroll.Content.anchoredPosition.y;
        _stack.Add(new View { Title = title, Define = define });
        Backroom.Shell.Search.text = "";
        _query = "";
        Rebuild();
    }

    public bool Pop()
    {
        if (_stack.Count <= 1)
            return false;
        _stack.RemoveAt(_stack.Count - 1);
        Rebuild();
        var view = _stack[^1];
        Select(view.Selected, false);
        _scroll.Content.anchoredPosition = new Vector2(0, view.Scroll);
        return true;
    }

    /// <summary>Rebuilds the rows of the current view (after a push, a pop or a data change).</summary>
    protected void Rebuild()
    {
        foreach (var row in _rows)
        {
            row.Rect.SetParent(null, false);
            UnityEngine.Object.Destroy(row.Rect.gameObject);
        }
        _rows.Clear();
        _stack[^1].Define(this);
        _selected = -1;
        Refilter();
        Select(FirstSelectable(0, 1), false);
        _scroll.Content.anchoredPosition = Vector2.zero;
        Backroom.Shell?.RefreshCrumb();
    }

    private void Refilter()
    {
        _visible.Clear();
        // Search shows matching rows, plus the section header above any match.
        SectionRow pendingSection = null;
        foreach (var row in _rows)
        {
            bool show;
            if (row is SectionRow section)
            {
                pendingSection = section;
                section.Rect.gameObject.SetActive(_query.Length == 0);
                if (_query.Length == 0)
                    _visible.Add(section);
                continue;
            }
            show = _query.Length == 0 || row.Title.ToLowerInvariant().Contains(_query)
                   || (row.Description != null && row.Description.ToLowerInvariant().Contains(_query));
            if (show && _query.Length > 0 && pendingSection != null)
            {
                pendingSection.Rect.gameObject.SetActive(true);
                _visible.Add(pendingSection);
                pendingSection = null;
            }
            row.Rect.gameObject.SetActive(show);
            if (show)
                _visible.Add(row);
        }
        float y = 0;
        foreach (var row in _visible)
        {
            row.Y = y;
            row.Rect.anchorMin = new Vector2(0, 1);
            row.Rect.anchorMax = new Vector2(1, 1);
            row.Rect.pivot = new Vector2(0.5f, 1);
            row.Rect.offsetMin = new Vector2(0, -y - row.Height);
            row.Rect.offsetMax = new Vector2(-4, -y);
            y += row.Height + (row is SectionRow ? 0 : 4);
        }
        _scroll.Content.sizeDelta = new Vector2(0, y + 12);
        foreach (var row in _visible)
            SafeRefresh(row);
    }

    /// <summary>
    /// Live values can't always be read (the player's cash doesn't exist while a save loads, an NPC can despawn);
    /// a failing row shows a dash instead of breaking the page.
    /// </summary>
    private static void SafeRefresh(Row row)
    {
        try
        {
            row.Refresh();
        }
        catch (Exception)
        {
            if (row.Value != null)
                row.Value.text = "—";
        }
    }

    private int FirstSelectable(int from, int direction)
    {
        for (int i = from; i >= 0 && i < _visible.Count; i += direction)
        {
            if (_visible[i].Selectable)
                return i;
        }
        return -1;
    }

    private const float DoubleClick = 0.4f;
    private Row _lastClicked;
    private float _lastClickAt = -10f;

    /// <summary>Like a desktop list: one click selects, a double-click runs (Enter also runs the selection).</summary>
    private void Click(Row row)
    {
        float now = Time.unscaledTime;
        bool twice = row == _lastClicked && now - _lastClickAt <= DoubleClick;
        SelectRow(row);
        if (twice)
        {
            _lastClicked = null;
            row.Activate();
            return;
        }
        _lastClicked = row;
        _lastClickAt = now;
    }

    private void SelectRow(Row row)
    {
        int index = _visible.IndexOf(row);
        if (index >= 0 && index != _selected)
            Select(index, true);
    }

    private void Select(int index, bool animate)
    {
        if (index < 0 || index >= _visible.Count || !_visible[index].Selectable)
            index = FirstSelectable(0, 1);
        var previous = SelectedRow;
        if (previous != null)
            Paint(previous, false);
        _selected = index;
        _hold = 0f;
        var row = SelectedRow;
        _bar.gameObject.SetActive(row != null);
        if (row == null)
        {
            ShowInfo(null);
            return;
        }
        Paint(row, true);
        _bar.SetSiblingIndex(0);
        _barH = row.Height;
        _barFrom = animate ? -_bar.offsetMax.y : row.Y;
        _barTo = row.Y;
        _barT = animate ? 0f : 1f;
        PlaceBar(animate ? _barFrom : _barTo);
        _scroll.Reveal(row.Y, row.Y + row.Height, _scroll.Content.sizeDelta.y);
        ShowInfo(row);
        Backroom.Shell?.RefreshCrumb();
    }

    private void PlaceBar(float y)
    {
        _bar.offsetMin = new Vector2(0, -y - _barH);
        _bar.offsetMax = new Vector2(-4, -y);
    }

    /// <summary>Selected rows sit on the white bar, so their text turns dark.</summary>
    private static void Paint(Row row, bool selected)
    {
        row.IsSelected = selected;
        if (row.Button != null)
        {
            if (selected)
                K.OnSelection(row.Button);
            else
                K.OffSelection(row.Button);
        }
        SafeRefresh(row);
        bool enabled = row.IsEnabled;
        row.Label.color = selected ? Theme.OnSelect : enabled ? Theme.Text : Theme.Text3;
        row.Label.font = selected ? Theme.SemiBold : Theme.Medium;
        if (row.Value != null)
            row.Value.color = selected ? Theme.OnSelect2 : Theme.Text2;
        if (row.Icon != null)
            row.Icon.color = selected ? Theme.OnSelect : Theme.Text2;
        foreach (var img in row.Rect.GetComponentsInChildren<Image>())
        {
            if (img.name == "Arrow" || img.name == "Glyph")
                img.color = selected ? Theme.OnSelect2 : Theme.Text3;
        }
    }

    private void ShowInfo(Row row)
    {
        _infoTitle.text = row?.Title ?? "";
        _infoBody.text = row?.Description ?? "";
        _infoHint.text = row switch
        {
            null => "",
            ActionRow a when a.Hold => "Hold Enter or the mouse button to confirm",
            ActionRow { IsReadout: true } => "",
            ActionRow => "Double-click or Enter to run",
            ToggleRow => "Click the switch or press Enter   ·   ← → off / on",
            StepRow => "← → or the arrows to change   ·   Shift for bigger steps",
            ChoiceRow => "← → or the arrows to choose",
            LinkRow => "Double-click or Enter to open   ·   Backspace to go back",
            _ => "",
        };
        OnInfo(row);
    }

    /// <summary>Lets a page add its own info content for the selected row (InfoExtra).</summary>
    protected virtual void OnInfo(Row row) { }

    protected internal override void OnShow()
    {
        string query = Backroom.Query;
        if (query != _query)
        {
            _query = query;
            Refilter();
            Select(FirstSelectable(0, 1), false);
        }
        foreach (var row in _visible)
            SafeRefresh(row);
        if (SelectedRow != null)
            Paint(SelectedRow, true);
    }

    protected internal override void Tick()
    {
        if (_barT < 1f)
        {
            _barT = Mathf.Min(1f, _barT + Time.unscaledDeltaTime / Theme.SelectTime);
            PlaceBar(Mathf.Lerp(_barFrom, _barTo, Shell.Ease(_barT)));
        }

        string query = Backroom.Query;
        if (query != _query)
        {
            _query = query;
            Refilter();
            Select(FirstSelectable(0, 1), false);
            Backroom.Shell?.RefreshCrumb();
        }

        // Hold to confirm: the mouse held on the row, or Enter held while it's selected.
        if (SelectedRow is ActionRow { Hold: true } hold)
        {
            bool held = hold.Button.IsPressed()
                        || (Keyboard.current != null && (Keyboard.current.enterKey.isPressed || Keyboard.current.numpadEnterKey.isPressed));
            _hold = held && hold.IsEnabled ? _hold + Time.unscaledDeltaTime : 0f;
            hold.Progress.anchorMax = new Vector2(Mathf.Clamp01(_hold / HoldTime), 1);
            if (_hold >= HoldTime)
            {
                _hold = -10f;   // once per hold
                hold.Run();
            }
        }

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            foreach (var row in _visible)
                SafeRefresh(row);
            if (SelectedRow != null)
                OnInfo(SelectedRow);
        }
    }

    protected internal override bool OnNavigate(Nav key)
    {
        var row = SelectedRow;
        switch (key)
        {
            case Nav.Up:
                int up = FirstSelectable(_selected - 1, -1);
                if (up < 0)
                    return false;
                Select(up, true);
                return true;
            case Nav.Down:
                int down = FirstSelectable(_selected + 1, 1);
                if (down < 0)
                    return false;
                Select(down, true);
                return true;
            case Nav.Left:
                row?.Adjust(-1);
                return row != null;
            case Nav.Right:
                row?.Adjust(1);
                return row != null;
            case Nav.Submit:
                row?.Activate();
                return row != null;
            case Nav.Back:
                if (_query.Length > 0)
                    return false;   // the menu clears the search
                return Pop();
        }
        return false;
    }

    /// <summary>Dev: runs the row titled <paramref name="title"/> in the current view (links open, holds run).</summary>
    public string DevRun(string title, int adjust = 0)
    {
        var row = _rows.Find(r => r.Selectable && r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (row == null)
            return $"no row '{title}' in {Breadcrumb}";
        SelectRow(row);
        if (adjust != 0)
            row.Adjust(adjust);
        else if (row is ActionRow { Hold: true } hold)
            hold.Run();
        else
            row.Activate();
        SafeRefresh(row);
        return $"ran '{row.Title}' value='{row.Value?.text}' now at {Breadcrumb}";
    }

    public string DevBack() => Pop() ? Breadcrumb : "at top";

    // ---- helpers for pages ----

    protected static void Done(string text) => Backroom.Toast(text);
    protected static void Fail(string text) => Backroom.Toast(text, false);
}
