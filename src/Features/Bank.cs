using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI;
using MelonLoader.Utils;

namespace PocketPlug.Features;

/// <summary>Moving money between cash and the bank account, the way the ATM does it.</summary>
internal static class Bank
{
    public const float WeeklyLimit = 10000f;

    public static float Cash => NetworkSingleton<MoneyManager>.Instance.cashBalance;
    public static float Online => NetworkSingleton<MoneyManager>.Instance.sync___get_value_onlineBalance();
    public static float DepositedThisWeek => ATM.WeeklyDepositSum;

    public static float DepositAllowance =>
        Config.NoDepositLimit.On ? float.MaxValue : Math.Max(0f, WeeklyLimit - ATM.WeeklyDepositSum);

    private static float _lastAction = -10f;

    /// <summary>
    /// True if a money action already ran in the last 0.3 s. Backstop for a click reaching a button more than once
    /// (pointer click plus submit), so money only ever moves once per click.
    /// </summary>
    private static bool Debounced()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now - _lastAction < 0.3f)
        {
            Core.Log.Warning($"Ignored a repeated bank action ({now - _lastAction:F3}s after the last one).");
            return true;
        }
        _lastAction = now;
        return false;
    }

    public static bool Deposit(float amount, out string error)
    {
        error = null;
        if (Debounced())
            return false;
        if (amount <= 0f)
            error = "Pick an amount first.";
        else if (Cash < amount)
            error = "You don't have that much cash.";
        else if (amount > DepositAllowance)
            error = $"Weekly limit: {MoneyManager.FormatAmount(DepositAllowance)} left.";
        if (error != null)
            return false;

        var money = NetworkSingleton<MoneyManager>.Instance;
        Core.Log.Msg($"Deposit {amount} (cash {Cash}, bank {Online}, frame {UnityEngine.Time.frameCount})");
        money.ChangeCashBalance(-amount, true, false);
        money.CreateOnlineTransaction("Cash Deposit", amount, 1f, "Bank app");
        // Still counted, so the game's own progression (e.g. the Clean Cash quest) keeps working.
        ATM.WeeklyDepositSum += amount;
        return true;
    }

    public static bool Withdraw(float amount, out string error)
    {
        error = null;
        if (Debounced())
            return false;
        if (amount <= 0f)
            error = "Pick an amount first.";
        else if (Online < amount)
            error = "Not enough money in the bank.";
        if (error != null)
            return false;

        var money = NetworkSingleton<MoneyManager>.Instance;
        Core.Log.Msg($"Withdraw {amount} (cash {Cash}, bank {Online}, frame {UnityEngine.Time.frameCount})");
        money.ChangeCashBalance(amount, true, false);
        money.CreateOnlineTransaction("Cash Withdrawal", -amount, 1f, "Bank app");
        return true;
    }

    /// <summary>Puts money straight into the bank account (used for dealer transfers).</summary>
    public static void Receive(float amount, string from)
    {
        NetworkSingleton<MoneyManager>.Instance.CreateOnlineTransaction("Transfer", amount, 1f, $"From {from}");
    }
}

/// <summary>
/// The game only keeps the transaction ledger for the current session, so new entries are also written to
/// <c>UserData/PocketPlug/history/&lt;save&gt;.tsv</c> with the in-game day and time.
/// </summary>
internal static class BankHistory
{
    public sealed class Entry
    {
        public string When;
        public string Name;
        public string Note;
        public float Amount;
    }

    private const int MaxShown = 60;
    private static readonly List<Entry> Entries = new();
    private static string _file;
    private static int _seenLedgerCount;

    public static IReadOnlyList<Entry> All => Entries;

    /// <summary>Goes up every time the list changes, so views know when to redraw.</summary>
    public static int Version { get; private set; }

    public static void OnSceneChanged()
    {
        Entries.Clear();
        Version++;
        _file = null;
        _seenLedgerCount = 0;
    }

    /// <summary>Opens the save's history file during loading, so the first read never costs a frame in play.</summary>
    public static void Warmup()
    {
        if (_file != null || !Singleton<LoadManager>.InstanceExists)
            return;
        var path = Singleton<LoadManager>.Instance.LoadedGameFolderPath;
        if (!string.IsNullOrEmpty(path))
            Open(path);
    }

    public static void Update()
    {
        if (!NetworkSingleton<MoneyManager>.InstanceExists || !Singleton<LoadManager>.InstanceExists)
            return;
        var load = Singleton<LoadManager>.Instance;
        if (!load.IsGameLoaded || string.IsNullOrEmpty(load.LoadedGameFolderPath))
            return;

        if (_file == null)
            Open(load.LoadedGameFolderPath);

        var ledger = NetworkSingleton<MoneyManager>.Instance.ledger;
        if (ledger.Count < _seenLedgerCount)
            _seenLedgerCount = 0;
        for (int i = _seenLedgerCount; i < ledger.Count; i++)
        {
            var t = ledger[i];
            Add(new Entry { When = Now(), Name = t.transaction_Name, Note = t.transaction_Note, Amount = t.total_Amount }, true);
        }
        _seenLedgerCount = ledger.Count;
    }

    private static void Open(string saveFolder)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(saveFolder.TrimEnd('/', '\\')));
        var name = $"{parent}_{Path.GetFileName(saveFolder.TrimEnd('/', '\\'))}";
        var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug", "history");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, name + ".tsv");
        if (!File.Exists(_file))
            return;

        foreach (var line in File.ReadAllLines(_file))
        {
            var p = line.Split('\t');
            if (p.Length == 4 && float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
                Add(new Entry { When = p[0], Name = p[1], Note = p[2], Amount = amount }, false);
        }
    }

    private static void Add(Entry e, bool persist)
    {
        Version++;
        Entries.Insert(0, e);
        if (Entries.Count > MaxShown)
            Entries.RemoveAt(Entries.Count - 1);
        if (persist && _file != null)
        {
            string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ');
            File.AppendAllText(_file,
                $"{Clean(e.When)}\t{Clean(e.Name)}\t{Clean(e.Note)}\t{e.Amount.ToString(CultureInfo.InvariantCulture)}\n");
        }
    }

    private static string Now()
    {
        if (!NetworkSingleton<TimeManager>.InstanceExists)
            return "";
        var time = NetworkSingleton<TimeManager>.Instance;
        return $"Day {time.ElapsedDays + 1}, {TimeManager.Get12HourTime(time.CurrentTime, true)}";
    }
}

/// <summary>
/// Removes the weekly deposit limit at ATMs. The limit is a constant compiled into the ATM screen, so while its
/// methods run, the weekly total is swapped for a huge negative number (every limit check passes) and then put
/// back. The real total keeps counting deposits.
/// </summary>
[HarmonyPatch]
internal static class AtmNoLimit
{
    private const float Masked = -1e9f;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var t = typeof(ATMInterface);
        foreach (var name in new[] { "Update", "UpdateAvailableAmounts", "DefaultAmountSelection", "SetSelectedAmount",
                     "AmountSelected", "DepositButtonPressed", "GetAmountFromIndex" })
        {
            var m = AccessTools.Method(t, name);
            if (m != null)
                yield return m;
            else
                Core.Log.Warning($"ATM method not found: {name}");
        }
    }

    [HarmonyPrefix]
    private static void Prefix(out float __state)
    {
        __state = float.NaN;
        if (!Config.NoDepositLimit.On || ATM.WeeklyDepositSum <= Masked / 2)
            return;
        __state = ATM.WeeklyDepositSum;
        ATM.WeeklyDepositSum = Masked;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    private static void Postfix(float __state)
    {
        if (!float.IsNaN(__state))
            ATM.WeeklyDepositSum = __state + (ATM.WeeklyDepositSum - Masked);
    }
}

[HarmonyPatch(typeof(ATMInterface), "Update")]
internal static class AtmLimitLabel
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ATMInterface __instance)
    {
        if (Config.NoDepositLimit.On && __instance.depositLimitText != null)
            __instance.depositLimitText.text = $"{MoneyManager.FormatAmount(ATM.WeeklyDepositSum)} this week (no limit)";
    }
}
