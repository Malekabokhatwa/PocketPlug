using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Features;

/// <summary>
/// Paying employees from the bank. Employees take their daily wage from cash in their locker each morning
/// (Employee.RemoveDailyWage), so paying means moving cash from the bank into the locker. Per-employee amounts
/// and per-property auto-pay are stored per save in UserData/PocketPlug/payroll/.
/// </summary>
internal static class Payroll
{
    private sealed class Data
    {
        public Dictionary<string, float> Amounts { get; set; } = new();
        public List<string> AutoPay { get; set; } = new();
    }

    private static Data _data;
    private static string _file;

    public static void Reset()
    {
        _data = null;
        _file = null;
    }

    private static Data Store
    {
        get
        {
            if (_data != null)
                return _data;
            _data = new Data();
            _file = Util.SaveKey.File("payroll", ".json");
            try
            {
                if (_file != null && File.Exists(_file))
                    _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(_file)) ?? new Data();
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Payroll settings couldn't be read, starting fresh: {e.Message}");
            }
            return _data;
        }
    }

    private static void Save()
    {
        if (_file == null)
            _file = Util.SaveKey.File("payroll", ".json");
        if (_file != null)
            File.WriteAllText(_file, JsonSerializer.Serialize(Store));
    }

    /// <summary>Owned properties that have at least one employee.</summary>
    public static List<Property> Properties()
    {
        var list = new List<Property>();
        var owned = Property.OwnedProperties;
        for (int i = 0; i < owned.Count; i++)
        {
            var p = owned[i];
            if (p != null && p.Employees != null && p.Employees.Count > 0)
                list.Add(p);
        }
        return list;
    }

    public static List<Employee> Employees(Property property)
    {
        var list = new List<Employee>();
        var emps = property?.Employees;
        if (emps == null)
            return list;
        for (int i = 0; i < emps.Count; i++)
        {
            if (emps[i] != null)
                list.Add(emps[i]);
        }
        return list;
    }

    public static string Id(Employee e) => e.GUID.ToString();

    /// <summary>The amount you set for this employee, or their daily wage.</summary>
    public static float Amount(Employee e) =>
        Store.Amounts.TryGetValue(Id(e), out float a) ? a : e.DailyWage;

    public static void SetAmount(Employee e, float amount)
    {
        Store.Amounts[Id(e)] = Math.Clamp(MathF.Floor(amount), 0f, 1_000_000f);
        Save();
    }

    public static bool AutoPay(Property p) => Store.AutoPay.Contains(p.PropertyCode);

    public static void SetAutoPay(Property p, bool on)
    {
        Store.AutoPay.Remove(p.PropertyCode);
        if (on)
            Store.AutoPay.Add(p.PropertyCode);
        Save();
    }

    /// <summary>Cash in the employee's locker, or -1 if they have no locker.</summary>
    public static float LockerCash(Employee e)
    {
        var home = e.GetHome();
        return home != null ? home.GetCashSum() : -1f;
    }

    /// <summary>Moves cash from the bank into the employee's locker.</summary>
    public static bool Pay(Employee e, float amount, out string error)
    {
        error = null;
        amount = MathF.Floor(amount);
        var home = e.GetHome();
        if (amount <= 0f)
            error = "Set an amount first.";
        else if (home == null || home.Storage == null)
            error = $"{e.FirstName} has no locker.";
        else if (Bank.Online < amount)
            error = "Not enough money in the bank.";
        if (error != null)
            return false;

        // Add to cash already in the locker (like the game's own EmployeeHome.RemoveCash), or put new cash in.
        bool added = false;
        var slots = home.Storage.ItemSlots;
        for (int i = 0; i < slots.Count && !added; i++)
        {
            var cash = slots[i]?.ItemInstance?.TryCast<CashInstance>();
            if (cash == null)
                continue;
            cash.ChangeBalance(amount);
            slots[i].ReplicateStoredInstance();
            added = true;
        }
        if (!added)
        {
            var cash = NetworkSingleton<MoneyManager>.Instance.GetCashInstance(amount);
            if (!home.Storage.CanItemFit(cash, 1))
            {
                error = $"{e.FirstName}'s locker is full.";
                return false;
            }
            home.Storage.InsertItem(cash, true);
        }

        NetworkSingleton<MoneyManager>.Instance.CreateOnlineTransaction("Payroll", -amount, 1f, e.FullName);
        Daily.RecordPayroll(amount);
        return true;
    }

    /// <summary>Pays every employee at a property their set amount. Returns how many were paid and the total.</summary>
    public static (int paid, float total, string error) PayAll(Property p)
    {
        int paid = 0;
        float total = 0f;
        string lastError = null;
        foreach (var e in Employees(p))
        {
            float amount = Amount(e);
            if (Pay(e, amount, out string error))
            {
                paid++;
                total += amount;
            }
            else
            {
                lastError = error;
            }
        }
        return (paid, total, lastError);
    }

    /// <summary>Morning: top each employee at auto-pay properties up to their set amount.</summary>
    public static void AutoPayMorning()
    {
        if (!Config.Payroll.On)
            return;
        int paid = 0;
        float total = 0f;
        var problems = new List<string>();
        foreach (var p in Properties())
        {
            if (!AutoPay(p))
                continue;
            foreach (var e in Employees(p))
            {
                float locker = LockerCash(e);
                float shortfall = Amount(e) - Math.Max(0f, locker);
                if (shortfall < 1f)
                    continue;
                if (Pay(e, shortfall, out string error))
                {
                    paid++;
                    total += shortfall;
                }
                else
                {
                    problems.Add(error);
                }
            }
        }

        if (!Singleton<NotificationsManager>.InstanceExists || (paid == 0 && problems.Count == 0))
            return;
        string title = paid > 0 ? $"Payroll: paid {MoneyManager.FormatAmount(total)}" : "Payroll couldn't pay";
        string detail = problems.Count > 0 ? problems[0] : $"{paid} employee{(paid == 1 ? "" : "s")} topped up from your bank.";
        Singleton<NotificationsManager>.Instance.SendNotification(title, detail, Util.Assets.Sprite("app_payroll"), 6f, true);
    }
}
