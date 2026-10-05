using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Money;
using PocketPlug.Features;
using PocketPlug.Util;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Phone;

/// <summary>Deposit and withdraw from the phone, with transaction history.</summary>
internal sealed class BankApp : CustomApp
{
    public override string Id => "bank";
    public override string Label => "Bank";
    public override string IconName => "app_bank";
    public override bool Enabled => Config.BankApp.On;

    private static readonly Color Accent = new(0.12f, 0.5f, 0.3f, 1f);

    private Text _online, _cash, _amount, _status, _weekly;
    private RectTransform _history;
    private readonly List<GameObject> _historyRows = new();
    private int _shownHistoryVersion = -1;
    private float _selected;
    private float _nextRefresh;

    protected internal override void Build(RectTransform root)
    {
        Ui.Stretch(Ui.Image("Background", root, Ui.Background).rectTransform);
        Ui.Header(root, "Bank", Accent);

        // Balances
        var balance = Ui.Image("Balance", root, Ui.Card).rectTransform;
        Top(balance, 130, 170);
        Caption(balance, "BANK BALANCE", 18, Ui.TextDim, 24, 18, 40);
        _online = Caption(balance, "", 52, Ui.TextMain, 24, 52, 70);
        _online.fontStyle = FontStyle.Bold;
        _cash = Caption(balance, "", 22, Ui.TextDim, 24, 126, 32);

        // Amount picker
        var picker = Ui.Image("Amount", root, Ui.Card).rectTransform;
        Top(picker, 320, 250);
        Caption(picker, "AMOUNT", 18, Ui.TextDim, 24, 16, 30);
        _amount = Caption(picker, "", 44, Ui.TextMain, 24, 46, 60);
        _amount.fontStyle = FontStyle.Bold;
        Buttons(picker, 116, ("-1K", () => Add(-1000)), ("-100", () => Add(-100)), ("+100", () => Add(100)), ("+1K", () => Add(1000)));
        Buttons(picker, 180, ("All cash", () => Set(Bank.Cash)), ("All bank", () => Set(Bank.Online)), ("Clear", () => Set(0)));

        // Actions
        var actions = Ui.Rect("Actions", root);
        Top(actions, 590, 76);
        var deposit = Ui.Button("Deposit", actions, "Deposit", Ui.Green, () => Do(true), 28);
        Ui.Place(deposit.GetComponent<RectTransform>(), 0, 0, 0.49f, 1);
        var withdraw = Ui.Button("Withdraw", actions, "Withdraw", Ui.Off, () => Do(false), 28);
        Ui.Place(withdraw.GetComponent<RectTransform>(), 0.51f, 0, 1, 1);

        _status = Ui.Text("Status", root, "", 20, Ui.TextDim, TextAnchor.MiddleCenter);
        Top(_status.rectTransform, 672, 34);
        _weekly = Ui.Text("Weekly", root, "", 18, Ui.TextDim, TextAnchor.MiddleCenter);
        Top(_weekly.rectTransform, 704, 30);

        var historyTitle = Ui.Text("HistoryTitle", root, "HISTORY", 18, Ui.TextDim, TextAnchor.LowerLeft);
        historyTitle.fontStyle = FontStyle.Bold;
        Top(historyTitle.rectTransform, 740, 34);
        _history = Ui.ScrollList(root, 780, 0, 6);
    }

    protected internal override void OnOpened()
    {
        _status.text = "";
        _shownHistoryVersion = -1;
        Refresh();
    }

    protected internal override void Tick()
    {
        if (Time.unscaledTime < _nextRefresh)
            return;
        _nextRefresh = Time.unscaledTime + 0.25f;
        Refresh();
    }

    private void Refresh()
    {
        _online.text = MoneyManager.FormatAmount(Bank.Online, true);
        _cash.text = $"Cash on hand: {MoneyManager.FormatAmount(Bank.Cash, true)}";
        _amount.text = MoneyManager.FormatAmount(_selected);
        _weekly.text = Config.NoDepositLimit.On
            ? $"Deposited this week: {MoneyManager.FormatAmount(Bank.DepositedThisWeek)} (no limit)"
            : $"Deposited this week: {MoneyManager.FormatAmount(Bank.DepositedThisWeek)} / {MoneyManager.FormatAmount(Bank.WeeklyLimit)}";

        var entries = BankHistory.All;
        if (BankHistory.Version == _shownHistoryVersion)
            return;
        _shownHistoryVersion = BankHistory.Version;
        foreach (var row in _historyRows)
            UnityEngine.Object.Destroy(row);
        _historyRows.Clear();
        if (entries.Count == 0)
        {
            var empty = Ui.Row("Empty", _history, 60);
            Ui.Stretch(Ui.Text("Text", empty, "No transactions yet.", 20, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform);
            _historyRows.Add(empty.gameObject);
            return;
        }
        foreach (var e in entries)
            _historyRows.Add(HistoryRow(e));
    }

    private GameObject HistoryRow(BankHistory.Entry e)
    {
        var row = Ui.Row("Entry", _history, 74, Ui.Card);
        var name = Ui.Text("Name", row, string.IsNullOrEmpty(e.Note) ? e.Name : $"{e.Name} · {e.Note}", 21, Ui.TextMain, TextAnchor.UpperLeft);
        name.rectTransform.anchorMin = new Vector2(0, 0);
        name.rectTransform.anchorMax = new Vector2(0.68f, 1);
        name.rectTransform.offsetMin = new Vector2(18, 0);
        name.rectTransform.offsetMax = new Vector2(0, -10);
        name.horizontalOverflow = HorizontalWrapMode.Overflow;
        var when = Ui.Text("When", row, e.When, 16, Ui.TextDim, TextAnchor.LowerLeft);
        when.rectTransform.anchorMin = Vector2.zero;
        when.rectTransform.anchorMax = new Vector2(0.68f, 1);
        when.rectTransform.offsetMin = new Vector2(18, 10);
        when.rectTransform.offsetMax = Vector2.zero;
        var amount = Ui.Text("Amount", row, (e.Amount >= 0 ? "+" : "-") + MoneyManager.FormatAmount(Math.Abs(e.Amount)), 24,
            e.Amount >= 0 ? Ui.Green : Ui.Red, TextAnchor.MiddleRight);
        amount.fontStyle = FontStyle.Bold;
        amount.rectTransform.anchorMin = new Vector2(0.62f, 0);
        amount.rectTransform.anchorMax = Vector2.one;
        amount.rectTransform.offsetMin = Vector2.zero;
        amount.rectTransform.offsetMax = new Vector2(-18, 0);
        return row.gameObject;
    }

    private void Add(float delta) => Set(_selected + delta);

    private void Set(float value)
    {
        _selected = Mathf.Max(0f, Mathf.Floor(value));
        _status.text = "";
        Refresh();
    }

    private void Do(bool deposit)
    {
        float amount = _selected;
        bool ok = deposit ? Bank.Deposit(amount, out string error) : Bank.Withdraw(amount, out error);
        _status.color = ok ? Ui.Green : Ui.Red;
        _status.text = ok
            ? $"{(deposit ? "Deposited" : "Withdrew")} {MoneyManager.FormatAmount(amount)}"
            : error;
        if (ok)
            _selected = 0;
        Refresh();
    }

    private static void Top(RectTransform rect, float y, float height, float margin = 24)
    {
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.offsetMin = new Vector2(margin, -y - height);
        rect.offsetMax = new Vector2(-margin, -y);
    }

    private static Text Caption(RectTransform parent, string text, int size, Color color, float x, float y, float height)
    {
        var t = Ui.Text("Label", parent, text, size, color);
        Top(t.rectTransform, y, height, x);
        return t;
    }

    private static void Buttons(RectTransform parent, float y, params (string label, Action action)[] buttons)
    {
        var row = Ui.Rect("Buttons", parent);
        Top(row, y, 52, 20);
        float w = 1f / buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = Ui.Button(buttons[i].label, row, buttons[i].label, Ui.Off, buttons[i].action, 20);
            Ui.Place(b.GetComponent<RectTransform>(), i * w + 0.01f, 0, (i + 1) * w - 0.01f, 1);
        }
    }
}
