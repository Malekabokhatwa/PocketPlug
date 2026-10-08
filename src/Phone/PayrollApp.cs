using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Money;
using PocketPlug.Features;
using PocketPlug.Util;
using UnityEngine;
using UnityEngine.UI;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Phone;

/// <summary>Pay employees from the bank: pick a property, set each employee's amount, Pay / Pay all, Auto-pay.</summary>
internal sealed class PayrollApp : CustomApp
{
    public override string Id => "payroll";
    public override string Label => "Payroll";
    public override string IconName => "app_payroll";
    public override bool Enabled => Config.Payroll.On;

    private static readonly Color Accent = new(0.78f, 0.45f, 0.1f, 1f);

    private Text _property, _status, _bank;
    private Image _autoTrack;
    private Text _autoState;
    private RectTransform _list;
    private readonly List<GameObject> _rows = new();
    private readonly List<(Employee emp, Text amount, Text locker)> _rowTexts = new();
    private List<Property> _properties = new();
    private int _index;
    private float _nextRefresh;
    private float _lastAction = -10f;

    protected internal override void Build(RectTransform root)
    {
        Ui.Stretch(Ui.Image("Background", root, Ui.Background).rectTransform);
        Ui.Header(root, "Payroll", Accent);

        // Property picker: ◀ name ▶
        var picker = Ui.Image("Picker", root, Ui.Card).rectTransform;
        Top(picker, 126, 70);
        var prev = Ui.Button("Prev", picker, "<", Ui.Off, () => Step(-1), 30);
        Ui.Place(prev.GetComponent<RectTransform>(), 0.02f, 0.12f, 0.16f, 0.88f);
        var next = Ui.Button("Next", picker, ">", Ui.Off, () => Step(1), 30);
        Ui.Place(next.GetComponent<RectTransform>(), 0.84f, 0.12f, 0.98f, 0.88f);
        _property = Ui.Text("Property", picker, "", 28, Ui.TextMain, TextAnchor.MiddleCenter);
        Ui.Place(_property.rectTransform, 0.17f, 0, 0.83f, 1);
        _property.fontStyle = FontStyle.Bold;

        _list = Ui.ScrollList(root, 206, 236, 8);

        // Footer: auto-pay, pay all, bank balance, status.
        var footer = Ui.Image("Footer", root, Ui.Card).rectTransform;
        footer.anchorMin = new Vector2(0, 0);
        footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(0.5f, 0);
        footer.offsetMin = new Vector2(24, 20);
        footer.offsetMax = new Vector2(-24, 226);

        var autoLabel = Ui.Text("AutoLabel", footer, "Auto-pay every morning", 22, Ui.TextMain);
        Ui.Place(autoLabel.rectTransform, 0.04f, 0.68f, 0.7f, 0.95f);
        var auto = Ui.Button("Auto", footer, "", Ui.Off, ToggleAuto, 22);
        Ui.Place(auto.GetComponent<RectTransform>(), 0.74f, 0.7f, 0.96f, 0.93f);
        _autoTrack = auto.GetComponent<Image>();
        _autoState = auto.transform.Find("Label").GetComponent<Text>();
        _autoState.fontStyle = FontStyle.Bold;

        var payAll = Ui.Button("PayAll", footer, "Pay all", Ui.Green, PayAll, 26);
        Ui.Place(payAll.GetComponent<RectTransform>(), 0.04f, 0.36f, 0.96f, 0.64f);

        _bank = Ui.Text("Bank", footer, "", 20, Ui.TextDim, TextAnchor.MiddleLeft);
        Ui.Place(_bank.rectTransform, 0.04f, 0.04f, 0.5f, 0.32f);
        _status = Ui.Text("Status", footer, "", 20, Ui.TextDim, TextAnchor.MiddleRight);
        Ui.Place(_status.rectTransform, 0.4f, 0.04f, 0.96f, 0.32f);
    }

    protected internal override void OnOpened()
    {
        _properties = Payroll.Properties();
        if (_index >= _properties.Count)
            _index = 0;
        _status.text = "";
        Rebuild();
    }

    protected internal override void Tick()
    {
        if (Time.unscaledTime < _nextRefresh)
            return;
        _nextRefresh = Time.unscaledTime + 0.5f;
        RefreshTexts();
    }

    private Property Current => _properties.Count > 0 ? _properties[Math.Clamp(_index, 0, _properties.Count - 1)] : null;

    private void Step(int dir)
    {
        if (_properties.Count == 0)
            return;
        _index = (_index + dir + _properties.Count) % _properties.Count;
        _status.text = "";
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var row in _rows)
            UnityEngine.Object.Destroy(row);
        _rows.Clear();
        _rowTexts.Clear();

        var prop = Current;
        _property.text = prop != null ? prop.PropertyName : "No employees";
        if (prop == null)
        {
            var empty = Ui.Row("Empty", _list, 90);
            Ui.Stretch(Ui.Text("Text", empty, "Hire employees at a property to pay them here.", 20, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform);
            _rows.Add(empty.gameObject);
        }
        else
        {
            foreach (var emp in Payroll.Employees(prop))
                _rows.Add(EmployeeRow(emp));
        }
        RefreshTexts();
    }

    private GameObject EmployeeRow(Employee emp)
    {
        var row = Ui.Row("Employee", _list, 168, Ui.Card);

        // Portrait in a circle, like the compass.
        var mask = Ui.Image("Mask", row, Color.white, Assets.Sprite("disc"));
        var mr = mask.rectTransform;
        mr.anchorMin = mr.anchorMax = new Vector2(0, 1);
        mr.pivot = new Vector2(0, 1);
        mr.sizeDelta = new Vector2(64, 64);
        mr.anchoredPosition = new Vector2(16, -14);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var photo = Ui.Image("Photo", mask.transform, Color.white, Npcs.Mugshot(emp));
        Ui.Stretch(photo.rectTransform);
        photo.preserveAspect = true;

        var name = Ui.Text("Name", row, $"{emp.FullName}", 24, Ui.TextMain, TextAnchor.UpperLeft);
        Corner(name.rectTransform, 92, 14, 330, 32);
        name.fontStyle = FontStyle.Bold;
        var role = Ui.Text("Role", row, $"{emp.EmployeeType} · wage {MoneyManager.FormatAmount(emp.DailyWage)}/day", 18, Ui.TextDim, TextAnchor.UpperLeft);
        Corner(role.rectTransform, 92, 46, 360, 26);
        var locker = Ui.Text("Locker", row, "", 18, Ui.TextDim, TextAnchor.UpperLeft);
        Corner(locker.rectTransform, 92, 72, 360, 26);

        // Amount: [-100][-10] $X [+10][+100]   [Pay]
        float y = -112;
        StepButton(row, "-100", 16, y, () => Change(emp, -100));
        StepButton(row, "-10", 84, y, () => Change(emp, -10));
        var amount = Ui.Text("Amount", row, "", 24, Ui.TextMain, TextAnchor.MiddleCenter);
        amount.fontStyle = FontStyle.Bold;
        Corner(amount.rectTransform, 148, 92, 120, 44);
        StepButton(row, "+10", 272, y, () => Change(emp, 10));
        StepButton(row, "+100", 340, y, () => Change(emp, 100));
        var pay = Ui.Button("Pay", row, "Pay", Ui.Green, () => PayOne(emp), 22);
        var pr = pay.GetComponent<RectTransform>();
        pr.anchorMin = pr.anchorMax = new Vector2(1, 1);
        pr.pivot = new Vector2(1, 1);
        pr.sizeDelta = new Vector2(150, 46);
        pr.anchoredPosition = new Vector2(-16, -92);

        _rowTexts.Add((emp, amount, locker));
        return row.gameObject;
    }

    private void RefreshTexts()
    {
        var prop = Current;
        bool auto = prop != null && Payroll.AutoPay(prop);
        _autoTrack.color = auto ? Ui.Green : Ui.Off;
        _autoState.text = auto ? "ON" : "OFF";
        _bank.text = $"Bank: {MoneyManager.FormatAmount(Bank.Online)}";
        foreach (var (emp, amount, locker) in _rowTexts)
        {
            if (emp == null)
                continue;
            amount.text = MoneyManager.FormatAmount(Payroll.Amount(emp));
            float cash = Payroll.LockerCash(emp);
            locker.text = cash < 0f
                ? "<color=#E55C54>No locker assigned</color>"
                : $"Locker: {MoneyManager.FormatAmount(cash)}{(emp.PaidForToday ? " · paid today" : "")}";
        }
    }

    private void Change(Employee emp, float delta)
    {
        Payroll.SetAmount(emp, Payroll.Amount(emp) + delta);
        RefreshTexts();
    }

    /// <summary>One money action per click, even if the click reaches the button twice.</summary>
    private bool Guard()
    {
        if (Time.unscaledTime - _lastAction < 0.3f)
            return false;
        _lastAction = Time.unscaledTime;
        return true;
    }

    private void PayOne(Employee emp)
    {
        if (!Guard())
            return;
        float amount = Payroll.Amount(emp);
        bool ok = Payroll.Pay(emp, amount, out string error);
        Status(ok, ok ? $"Paid {emp.FirstName} {MoneyManager.FormatAmount(amount)}" : error);
    }

    private void PayAll()
    {
        var prop = Current;
        if (prop == null || !Guard())
            return;
        var (paid, total, error) = Payroll.PayAll(prop);
        Status(paid > 0 && error == null, paid > 0
            ? $"Paid {paid} · {MoneyManager.FormatAmount(total)}{(error != null ? " (some failed)" : "")}"
            : error ?? "Nobody to pay.");
    }

    private void ToggleAuto()
    {
        var prop = Current;
        if (prop == null)
            return;
        Payroll.SetAutoPay(prop, !Payroll.AutoPay(prop));
        RefreshTexts();
    }

    private void Status(bool ok, string text)
    {
        _status.color = ok ? Ui.Green : Ui.Red;
        _status.text = text;
        RefreshTexts();
    }

    private static void StepButton(RectTransform row, string label, float x, float y, Action onClick)
    {
        var b = Ui.Button(label, row, label, Ui.Off, onClick, 18);
        var r = b.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0, 1);
        r.pivot = new Vector2(0, 1);
        r.sizeDelta = new Vector2(62, 44);
        r.anchoredPosition = new Vector2(x, y + 20);
    }

    private static void Corner(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(w, h);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    private static void Top(RectTransform rect, float y, float height, float margin = 24)
    {
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.offsetMin = new Vector2(margin, -y - height);
        rect.offsetMax = new Vector2(-margin, -y);
    }
}
