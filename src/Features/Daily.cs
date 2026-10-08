using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone.Messages;
using UnityEngine;

namespace PocketPlug.Features;

/// <summary>
/// The morning routine: when a new in-game day starts, run auto-pay, sweep dealers' cash to the bank, and have
/// "PocketPlug AAB" text a report of the day that just ended. Day stats (dealer earnings, deals) are collected
/// while playing: dealer earnings are rises in each dealer's cash, sampled every couple of seconds (our own
/// transfers lower it, so they never count as earnings); deals come from Contract.SubmitPayment.
/// </summary>
internal static class Daily
{
    private sealed class DealerStat
    {
        public string Name;
        public float LastCash = -1f;
        public float Earned;
        public int Deals;
        public float Swept;
    }

    private static readonly Dictionary<string, DealerStat> Dealers = new();
    private static int _lastDay = -1;
    private static float _next;
    private static float _settledAt = -1f;
    private static int _dealsDone;
    private static float _dealsPaid;
    private static float _payroll;
    private static float _morningPayroll;
    private static bool _inMorning;

    public static void Reset()
    {
        _lastDay = -1;
        _settledAt = -1f;
        Dealers.Clear();
        ClearDay();
        Aab.Reset();
    }

    private static void ClearDay()
    {
        _dealsDone = 0;
        _dealsPaid = 0f;
        _payroll = 0f;
        foreach (var d in Dealers.Values)
        {
            d.Earned = 0f;
            d.Deals = 0;
            d.Swept = 0f;
        }
    }

    public static void RecordPayroll(float amount)
    {
        if (_inMorning)
            _morningPayroll += amount;
        else
            _payroll += amount;
    }

    public static void RecordDeal(Contract c, float bonus)
    {
        if (c.Dealer != null)
        {
            Stat(c.Dealer).Deals++;
            return;
        }
        _dealsDone++;
        _dealsPaid += c.Payment + bonus;
    }

    private static DealerStat Stat(Dealer d)
    {
        string id = d.GUID.ToString();
        if (!Dealers.TryGetValue(id, out var s))
        {
            s = new DealerStat { Name = d.FullName };
            Dealers[id] = s;
        }
        return s;
    }

    public static void Update()
    {
        if (Time.unscaledTime < _next || !NetworkSingleton<TimeManager>.InstanceExists)
            return;
        _next = Time.unscaledTime + 2f;

        // The save's time is applied partway through loading (ElapsedDays reads 0 first), so only start tracking
        // once the game has finished loading and had a few seconds to settle.
        if (!Singleton<Il2CppScheduleOne.Persistence.LoadManager>.InstanceExists)
            return;
        // Restore saved reports as early as Messages exists (during loading), so it never costs a frame in play.
        Aab.EnsureRestored();

        var load = Singleton<Il2CppScheduleOne.Persistence.LoadManager>.Instance;
        if (!load.IsGameLoaded || load.IsLoading)
        {
            _settledAt = -1f;
            return;
        }
        if (_settledAt < 0f)
            _settledAt = Time.unscaledTime + 5f;
        if (Time.unscaledTime < _settledAt)
            return;

        SampleDealers();

        int day = NetworkSingleton<TimeManager>.Instance.ElapsedDays;
        if (_lastDay >= 0 && day == _lastDay + 1)
            Morning(_lastDay);
        else if (_lastDay >= 0 && day != _lastDay)
            Core.Log.Msg($"Day changed {_lastDay} -> {day} without a normal day pass; not running the morning routine.");
        _lastDay = day;
    }

    private static void SampleDealers()
    {
        var dealers = Dealer.AllPlayerDealers;
        for (int i = 0; i < dealers.Count; i++)
        {
            var d = dealers[i];
            if (d == null || !d.IsRecruited)
                continue;
            var s = Stat(d);
            float cash = d.Cash;
            if (s.LastCash >= 0f && cash > s.LastCash)
                s.Earned += cash - s.LastCash;
            s.LastCash = cash;
        }
    }

    /// <summary>Runs the morning routine now (also used by the dev command <c>newday</c>).</summary>
    public static void Morning(int finishedDay)
    {
        SampleDealers();
        _inMorning = true;
        _morningPayroll = 0f;
        float swept = 0f;
        try
        {
            if (Config.DealerSweep.On)
                swept = Sweep();
            Payroll.AutoPayMorning();
            EmployeeAlerts.NewDay();
        }
        finally
        {
            _inMorning = false;
        }

        if (Config.DailyReport.On)
            Aab.Send(Report(finishedDay, swept), notify: true);
        ClearDay();
    }

    /// <summary>Dev: sends a report for today so far, without sweeping or paying anyone.</summary>
    public static void SendReportNow()
    {
        SampleDealers();
        Aab.Send(Report(NetworkSingleton<TimeManager>.Instance.ElapsedDays, 0f), notify: true);
    }

    private static float Sweep()
    {
        float total = 0f;
        int count = 0;
        var dealers = Dealer.AllPlayerDealers;
        for (int i = 0; i < dealers.Count; i++)
        {
            var d = dealers[i];
            if (d == null || !d.IsRecruited)
                continue;
            float amount = Mathf.Floor(d.Cash);
            if (amount < 1f)
                continue;
            d.SetCash(d.Cash - amount);
            Bank.Receive(amount, d.FullName);
            var s = Stat(d);
            s.Swept += amount;
            s.LastCash = d.Cash - amount;
            total += amount;
            count++;
        }
        if (count > 0 && Singleton<NotificationsManager>.InstanceExists)
        {
            Singleton<NotificationsManager>.Instance.SendNotification(
                $"Dealers sent you {MoneyManager.FormatAmount(total)}",
                $"{count} dealer{(count == 1 ? "" : "s")} swept to your bank", Util.Assets.Sprite("app_bank"), 6f, true);
        }
        return total;
    }

    private static string Report(int finishedDay, float swept)
    {
        var sb = new StringBuilder();
        sb.Append($"Daily report, day {finishedDay + 1}\n");

        // Bank in/out for that day, from the bank history (entries are stamped "Day N, time").
        float bankIn = 0f, bankOut = 0f;
        string prefix = $"Day {finishedDay + 1},";
        foreach (var e in BankHistory.All)
        {
            if (e.When == null || !e.When.StartsWith(prefix))
                continue;
            if (e.Amount >= 0f)
                bankIn += e.Amount;
            else
                bankOut -= e.Amount;
        }
        sb.Append($"\nBank: +{Money(bankIn)} in, -{Money(bankOut)} out (net {Signed(bankIn - bankOut)})");
        sb.Append($"\nYour deals: {_dealsDone} done, {Money(_dealsPaid)} paid");
        if (_payroll > 0f)
            sb.Append($"\nPayroll paid: {Money(_payroll)}");

        var dealers = Dealer.AllPlayerDealers;
        bool any = false;
        for (int i = 0; i < dealers.Count; i++)
        {
            var d = dealers[i];
            if (d == null || !d.IsRecruited)
                continue;
            if (!any)
            {
                sb.Append("\n\nDealers:");
                any = true;
            }
            var s = Stat(d);
            sb.Append($"\n- {d.FullName}: made {Money(s.Earned)}, {s.Deals} deal{(s.Deals == 1 ? "" : "s")}");
            sb.Append($"\n  Left: {ProductLeft(d)}");
        }

        if (swept > 0f || _morningPayroll > 0f)
        {
            sb.Append("\n\nThis morning:");
            if (swept > 0f)
                sb.Append($" {Money(swept)} swept from dealers to your bank.");
            if (_morningPayroll > 0f)
                sb.Append($" Payroll topped up {Money(_morningPayroll)}.");
        }
        return sb.ToString();
    }

    /// <summary>What a dealer still holds, grouped by product, counting units inside packaging.</summary>
    private static string ProductLeft(Dealer d)
    {
        var totals = new Dictionary<string, int>();
        var slots = d.Inventory?.ItemSlots;
        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var product = slots[i]?.ItemInstance?.TryCast<ProductItemInstance>();
                if (product == null)
                    continue;
                string name = product.Name;
                totals[name] = (totals.TryGetValue(name, out int n) ? n : 0) + product.GetTotalAmount();
            }
        }
        if (totals.Count == 0)
            return "nothing";
        return string.Join(", ", totals.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}"));
    }

    private static string Money(float v) => MoneyManager.FormatAmount(v);
    private static string Signed(float v) => (v >= 0 ? "+" : "-") + MoneyManager.FormatAmount(Math.Abs(v));
}

[HarmonyPatch(typeof(Contract), nameof(Contract.SubmitPayment))]
internal static class ContractPaymentPatch
{
    private static void Postfix(Contract __instance, float bonusTotal)
    {
        try
        {
            Daily.RecordDeal(__instance, bonusTotal);
        }
        catch (Exception e)
        {
            Core.Log.Warning($"Deal stats: {e.Message}");
        }
    }
}

/// <summary>
/// The "PocketPlug AAB" contact in Messages. 0.4.7 conversations can have a contact with no NPC behind it
/// (MessageContactInfo with a name and icon), but the game only saves NPC conversations, so the last few reports
/// are kept in UserData/PocketPlug/reports/ and put back quietly after each load.
/// </summary>
internal static class Aab
{
    private const string Id = "pocketplug_aab";
    private const string Separator = "\n<<<pocketplug-report>>>\n";
    private const int Keep = 7;

    private static MSGConversation _convo;
    private static bool _restored;

    public static void Reset()
    {
        _convo = null;
        _restored = false;
    }

    /// <summary>Dev: opens the AAB conversation in the Messages app.</summary>
    public static void Show()
    {
        var convo = Conversation();
        if (convo == null)
            return;
        var app = PlayerSingleton<MessagesApp>.Instance;
        app.SetOpen(true);
        convo.EntryClicked();   // what tapping the row does
    }

    private static MSGConversation Conversation()
    {
        if (_convo != null)
            return _convo;
        if (!PlayerSingleton<MessagesApp>.InstanceExists || !NetworkSingleton<MessagingManager>.InstanceExists)
            return null;
        var contact = new MessageContactInfo("PocketPlug AAB", Id, Util.Assets.Sprite("contact_aab"), false, false);
        _convo = new MSGConversation(contact, Id);
        _convo.SetIsKnown(true);
        return _convo;
    }

    /// <summary>Puts saved reports back into the conversation once Messages is ready.</summary>
    public static void EnsureRestored()
    {
        if (_restored || !Config.DailyReport.On || !PlayerSingleton<MessagesApp>.InstanceExists)
            return;
        _restored = true;
        // No saved reports: don't create the conversation until the first report is sent.
        var saved = Load();
        if (saved.Count == 0)
            return;
        var convo = Conversation();
        if (convo == null)
        {
            _restored = false;
            return;
        }
        foreach (var text in saved)
            convo.SendMessage(new Message(text, Message.ESenderType.Other, true, -1), false, false);
    }

    public static void Send(string text, bool notify)
    {
        var convo = Conversation();
        if (convo == null)
        {
            Core.Log.Warning("Daily report: Messages isn't ready yet.");
            return;
        }
        EnsureRestored();
        convo.SendMessage(new Message(text, Message.ESenderType.Other, true, -1), notify, false);
        var all = Load();
        all.Add(text);
        Store(all.Skip(Math.Max(0, all.Count - Keep)).ToList());
    }

    private static List<string> Load()
    {
        var file = Util.SaveKey.File("reports", ".txt");
        if (file == null || !File.Exists(file))
            return new List<string>();
        return File.ReadAllText(file).Split(Separator, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static void Store(List<string> reports)
    {
        var file = Util.SaveKey.File("reports", ".txt");
        if (file != null)
            File.WriteAllText(file, string.Join(Separator, reports));
    }
}
