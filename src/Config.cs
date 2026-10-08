using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace PocketPlug;

/// <summary>A feature that can be switched on and off from the Settings app.</summary>
internal sealed class Toggle
{
    public readonly string Key;
    public readonly string Title;
    public readonly string Description;
    public MelonPreferences_Entry<bool> Entry;

    public Toggle(string key, string title, string description)
    {
        Key = key;
        Title = title;
        Description = description;
    }

    public bool On => Entry.Value;
}

/// <summary>A number editable from the Settings app: a stack limit for an item type, or a station limit.</summary>
internal sealed class StackSetting
{
    public readonly ItemGroup Group;
    public readonly string Key;
    public readonly string Title;
    public readonly string Description;
    public readonly int Default;
    public MelonPreferences_Entry<int> Entry;

    public StackSetting(ItemGroup group, string key, string title, string description, int @default = 250)
    {
        Group = group;
        Key = key;
        Title = title;
        Description = description;
        Default = @default;
    }
}

/// <summary>
/// All settings live in <c>UserData/PocketPlug.cfg</c>. The file is watched, so edits apply while the game runs,
/// and the Settings app writes back to it.
/// </summary>
internal static class Config
{
    public const int MaxStackLimit = 9999;

    internal static readonly string FilePath = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug.cfg");

    public static readonly Toggle CompassDeals = new("CompassDeals", "Deal compass",
        "Shows the customer's photo, name and distance on the compass.");
    public static readonly Toggle SkateStamina = new("SkateStamina", "Endless skating",
        "Infinite stamina while riding a skateboard.");
    public static readonly Toggle BankApp = new("BankApp", "Bank app",
        "Deposit and withdraw from your phone, with transaction history.");
    public static readonly Toggle NoDepositLimit = new("NoDepositLimit", "No deposit limit",
        "Removes the weekly $10,000 deposit limit at ATMs and in the Bank app.");
    public static readonly Toggle DealerTransfers = new("DealerTransfers", "Dealer transfers",
        "Text a dealer to send the cash they hold to your bank account.");
    public static readonly Toggle ReadyAlerts = new("ReadyAlerts", "Ready alerts",
        "Notifies you when plants, shrooms and stations are done.");
    public static readonly Toggle DealExpiry = new("DealExpiry", "Deal reminders",
        "Notifies you when a deal has about an hour left.");
    public static readonly Toggle StackLimits = new("StackLimits", "Bigger stacks",
        "Raises stack limits per item type (set below).");
    public static readonly Toggle StationLimits = new("StationLimits", "Station limits",
        "Bigger mixing station batches and drying racks (set below).");
    public static readonly Toggle EmployeeAlerts = new("EmployeeAlerts", "Employee alerts",
        "Notifies you when an employee is out of supplies or stuck.");
    public static readonly Toggle Payroll = new("Payroll", "Payroll app",
        "Pay employees from your bank, per property, with optional auto-pay.");
    public static readonly Toggle DealerSweep = new("DealerSweep", "Dealer auto-sweep",
        "Every morning, your dealers' cash goes to your bank.");
    public static readonly Toggle DailyReport = new("DailyReport", "Daily report",
        "Every morning, PocketPlug AAB texts you yesterday's numbers.");

    public static readonly Toggle[] Toggles =
    {
        CompassDeals, SkateStamina, BankApp, NoDepositLimit, DealerTransfers, ReadyAlerts, DealExpiry, StackLimits,
        StationLimits, EmployeeAlerts, Payroll, DealerSweep, DailyReport
    };

    public static readonly StackSetting Mk1MixLimit =
        new(ItemGroup.None, "Mk1MixLimit", "Mixing station", "Items per mix (game: 10)", 125);
    public static readonly StackSetting Mk2MixLimit =
        new(ItemGroup.None, "Mk2MixLimit", "Mixing station Mk2", "Items per mix (game: 20)", 250);
    public static readonly StackSetting DryingRackCapacity =
        new(ItemGroup.None, "DryingRackCapacity", "Drying rack", "Items at once (game: 20)", 250);

    public static readonly StackSetting[] Stations = { Mk1MixLimit, Mk2MixLimit, DryingRackCapacity };

    public static readonly StackSetting[] Stacks =
    {
        new(ItemGroup.Product, "ProductLimit", "Products", "Weed, meth, cocaine, shrooms, your mixes"),
        new(ItemGroup.Packaging, "PackagingLimit", "Packaging", "Baggies, jars, bricks"),
        new(ItemGroup.Mixer, "MixerLimit", "Mix ingredients", "Cuke, Banana, Mega Bean..."),
        new(ItemGroup.Precursor, "PrecursorLimit", "Precursors", "Coca leaf, pseudo, liquid meth..."),
        new(ItemGroup.Seed, "SeedLimit", "Seeds", "Weed and coca seeds"),
        new(ItemGroup.Soil, "SoilLimit", "Soil", "Soils and mushroom substrate"),
        new(ItemGroup.Additive, "AdditiveLimit", "Grow additives", "Fertilizer, PGR, Speed Grow"),
        new(ItemGroup.ShroomSupply, "ShroomSupplyLimit", "Shroom supplies", "Spore syringes, shroom spawn"),
        new(ItemGroup.Placeable, "PlaceableLimit", "Placeables", "Furniture, stations, lights, pots..."),
        new(ItemGroup.Other, "OtherLimit", "Everything else", "Acid, trash bags, spray paint..."),
    };

    private static MelonPreferences_Category _category;
    private static DateTime _lastWrite;
    private static float _nextCheck;

    /// <summary>Raised after settings change, from the file or the Settings app.</summary>
    public static event Action Changed;

    public static void Init()
    {
        _category = MelonPreferences.CreateCategory("PocketPlug", "PocketPlug");

        foreach (var t in Toggles)
            t.Entry = _category.CreateEntry(t.Key, true, t.Title, t.Description);

        foreach (var s in Stacks)
            s.Entry = _category.CreateEntry(s.Key, s.Default, s.Title, $"Stack limit for {s.Description.ToLowerInvariant()}. 0 = game default.");

        foreach (var s in Stations)
            s.Entry = _category.CreateEntry(s.Key, s.Default, s.Title, $"{s.Title}: {s.Description}. 0 = game default.");

        _category.SetFilePath(FilePath, autoload: true, printmsg: false);
        Save();
    }

    public static int LimitFor(ItemGroup group)
    {
        foreach (var s in Stacks)
        {
            if (s.Group == group)
                return s.Entry.Value;
        }
        return 0;
    }

    public static void Set(Toggle toggle, bool on)
    {
        toggle.Entry.Value = on;
        Save();
        Changed?.Invoke();
    }

    public static void Set(StackSetting setting, int value)
    {
        setting.Entry.Value = Math.Clamp(value, 0, MaxStackLimit);
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        _category.SaveToFile(printmsg: false);
        _lastWrite = LastWrite();
    }

    /// <summary>Reloads the file if it was edited outside the game. Checked about once a second.</summary>
    public static void ReloadIfChanged(float now)
    {
        if (now < _nextCheck)
            return;
        _nextCheck = now + 1f;

        var write = LastWrite();
        if (write == _lastWrite)
            return;

        _lastWrite = write;
        _category.LoadFromFile(printmsg: false);
        Core.Log.Msg("Settings reloaded from file.");
        Changed?.Invoke();
    }

    private static DateTime LastWrite() => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

    public static IEnumerable<string> Summary()
    {
        foreach (var t in Toggles)
            yield return $"{t.Key}={(t.On ? "on" : "off")}";
    }
}
