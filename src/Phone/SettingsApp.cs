using System.Collections.Generic;
using PocketPlug.Util;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Phone;

/// <summary>Turns every PocketPlug feature on or off and edits the per-type stack limits.</summary>
internal sealed class SettingsApp : CustomApp
{
    public override string Id => "settings";
    public override string Label => "Settings";
    public override string IconName => "app_settings";

    private readonly List<(Toggle toggle, Image track, Text state)> _toggles = new();
    private readonly List<(StackSetting setting, Text value)> _stacks = new();
    private Text _stationNote;

    protected internal override void Build(RectTransform root)
    {
        Ui.Image("Background", root, Ui.Background).rectTransform.SetAsFirstSibling();
        Ui.Stretch(root.Find("Background").GetComponent<RectTransform>());
        Ui.Header(root, "Settings", new Color(0.24f, 0.26f, 0.3f, 1f));

        var list = Ui.ScrollList(root, 120, 0);

        Section(list, "FEATURES");
        foreach (var toggle in Config.Toggles)
            ToggleRow(list, toggle);

        Section(list, "STACK LIMITS");
        foreach (var setting in Config.Stacks)
            StackRow(list, setting);

        Section(list, "STATIONS");
        foreach (var setting in Config.Stations)
            StackRow(list, setting);
        _stationNote = Ui.Text("Note", Ui.Row("StationNoteRow", list, 60).transform, "", 18, Ui.TextDim);
        Ui.Stretch(_stationNote.rectTransform, 4, 0, 4, 0);

        var note = Ui.Text("Note", Ui.Row("NoteRow", list, 70).transform,
            "0 keeps the game's limit. Guns, melee weapons, ammo and items that don't stack are never changed.",
            18, Ui.TextDim);
        Ui.Stretch(note.rectTransform, 4, 0, 4, 0);
    }

    protected internal override void OnOpened() => Sync();

    private static void Section(Transform list, string title)
    {
        var text = Ui.Text("Section", Ui.Row(title, list, 56).transform, title, 20, Ui.TextDim, TextAnchor.LowerLeft);
        text.fontStyle = FontStyle.Bold;
        Ui.Stretch(text.rectTransform, 6, 0, 0, 8);
    }

    private void ToggleRow(Transform list, Toggle toggle)
    {
        var row = Ui.Row(toggle.Key, list, 104, Ui.Card);
        var title = Ui.Text("Title", row, toggle.Title, 26, Ui.TextMain, TextAnchor.UpperLeft);
        Ui.Place(title.rectTransform, 0, 0.5f, 0.74f, 1);
        Ui.Stretch(title.rectTransform, 20, 16, 0, 0);
        title.rectTransform.anchorMin = new Vector2(0, 0.5f);
        title.rectTransform.anchorMax = new Vector2(0.74f, 1);

        var desc = Ui.Text("Desc", row, toggle.Description, 18, Ui.TextDim, TextAnchor.UpperLeft);
        desc.rectTransform.anchorMin = new Vector2(0, 0);
        desc.rectTransform.anchorMax = new Vector2(0.74f, 0.55f);
        desc.rectTransform.offsetMin = new Vector2(20, 8);
        desc.rectTransform.offsetMax = new Vector2(0, 0);

        var button = Ui.Button("Switch", row, "", Ui.Off, () => Config.Set(toggle, !toggle.On), 22);
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
        rect.pivot = new Vector2(1, 0.5f);
        rect.sizeDelta = new Vector2(110, 52);
        rect.anchoredPosition = new Vector2(-20, 0);
        var state = button.transform.Find("Label").GetComponent<Text>();
        state.fontStyle = FontStyle.Bold;
        _toggles.Add((toggle, button.GetComponent<Image>(), state));
    }

    private void StackRow(Transform list, StackSetting setting)
    {
        var row = Ui.Row(setting.Key, list, 96, Ui.Card);
        var title = Ui.Text("Title", row, setting.Title, 24, Ui.TextMain, TextAnchor.UpperLeft);
        title.rectTransform.anchorMin = new Vector2(0, 0.5f);
        title.rectTransform.anchorMax = new Vector2(0.5f, 1);
        title.rectTransform.offsetMin = new Vector2(20, 0);
        title.rectTransform.offsetMax = new Vector2(0, -14);
        var desc = Ui.Text("Desc", row, setting.Description, 17, Ui.TextDim, TextAnchor.UpperLeft);
        desc.rectTransform.anchorMin = new Vector2(0, 0);
        desc.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        desc.rectTransform.offsetMin = new Vector2(20, 6);
        desc.rectTransform.offsetMax = Vector2.zero;

        // [-10] value [+10], with long steps of 50 on the outer buttons.
        float x = -16;
        x = StepButton(row, "+50", x, () => Config.Set(setting, setting.Entry.Value + 50));
        x = StepButton(row, "+10", x, () => Config.Set(setting, setting.Entry.Value + 10));
        var value = Ui.Text("Value", row, "", 26, Ui.TextMain, TextAnchor.MiddleCenter);
        value.fontStyle = FontStyle.Bold;
        var vr = value.rectTransform;
        vr.anchorMin = vr.anchorMax = new Vector2(1, 0.5f);
        vr.pivot = new Vector2(1, 0.5f);
        vr.sizeDelta = new Vector2(76, 50);
        vr.anchoredPosition = new Vector2(x, 0);
        x -= 80;
        x = StepButton(row, "-10", x, () => Config.Set(setting, setting.Entry.Value - 10));
        StepButton(row, "-50", x, () => Config.Set(setting, setting.Entry.Value - 50));
        _stacks.Add((setting, value));
    }

    private static float StepButton(Transform row, string label, float x, System.Action onClick)
    {
        var button = Ui.Button(label, row, label, Ui.Off, onClick, 18);
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
        rect.pivot = new Vector2(1, 0.5f);
        rect.sizeDelta = new Vector2(52, 46);
        rect.anchoredPosition = new Vector2(x, 0);
        return x - 58;
    }

    private void Sync()
    {
        foreach (var (toggle, track, state) in _toggles)
        {
            track.color = toggle.On ? Ui.Green : Ui.Off;
            state.text = toggle.On ? "ON" : "OFF";
        }
        foreach (var (setting, value) in _stacks)
            value.text = setting.Entry.Value == 0 ? "Game" : setting.Entry.Value.ToString();

        // Batches are added to the output slot in one go, so they're capped at the product stack limit.
        int cap = Features.StationLimits.OutputCap;
        bool capped = false;
        foreach (var s in Config.Stations)
            capped |= s.Entry.Value > cap;
        _stationNote.text = capped
            ? $"<color=#FFB43C>Capped at {cap}</color>: a finished batch must fit one product stack. Raise the Products stack limit to go higher."
            : "Mix time scales with the batch size. 0 keeps the game's value.";
    }
}
