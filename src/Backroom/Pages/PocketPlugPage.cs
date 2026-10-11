using System;
using PocketPlug.Backroom.Kit;

namespace PocketPlug.Backroom.Pages;

/// <summary>Every PocketPlug setting (the same ones as the phone's Settings app), plus Backroom's own.</summary>
internal sealed class PocketPlugPage : ListPage
{
    public override string Id => "pocketplug";
    public override string Title => "PocketPlug";
    public override string Icon => "plug";

    private static readonly string[] Keys = { "B", "F1", "F2", "F3", "Insert", "Home", "Backquote" };

    protected override void Define()
    {
        Section("Backroom");
        Choice("Open key", "The key you hold to open Backroom.", () => Keys,
            () => Math.Max(0, Array.FindIndex(Keys, k => k.Equals(Config.BackroomKey.Value, StringComparison.OrdinalIgnoreCase))),
            i => Save(() => Config.BackroomKey.Value = Keys[i]), "lock");
        Stepper("Hold time", "How long you hold the key before Backroom opens.", () => Config.BackroomHold.Value,
            v => Save(() => Config.BackroomHold.Value = v), 0.15f, 1.0f, 0.05f, v => $"{v:0.00} s", "clock");

        Section("Backroom laptop");
        Action("Get a Wooden Square Table", "Puts a Wooden Square Table in your inventory. Place it at one of your properties, look at it, then install the laptop.", () =>
        {
            var def = Il2CppScheduleOne.Registry.GetItem("woodsquaretable");
            int given = Actions.ItemActions.Give(def, 1, null, null);
            if (given > 0)
                Done("Wooden Square Table added to your inventory");
            else
                Fail("No room in your inventory");
        }, "package");
        Action("Install laptop on this table", "Look at a Wooden Square Table you own (before opening Backroom), then run this. The laptop opens Backroom with E. Only PocketPlug remembers it: your save just has the table.", () =>
        {
            string problem = Desk.BackroomDesk.Install();
            if (problem == null)
                Done("Laptop installed: press E on it to open Backroom");
            else
                Fail(problem);
        }, "laptop");
        Action("Remove laptop from this table", "Look at the table with the laptop, then run this.", () =>
        {
            string problem = Desk.BackroomDesk.Remove();
            if (problem == null)
                Done("Laptop removed");
            else
                Fail(problem);
        }, "trash-2");

        Section("Features");
        foreach (var toggle in Config.Toggles)
        {
            if (toggle == Config.Backroom)
                continue;   // switching Backroom off from inside Backroom would close it mid-click
            var t = toggle;
            Toggle(t.Title, t.Description, () => t.On, on => Config.Set(t, on));
        }

        Section("Stack limits");
        foreach (var setting in Config.Stacks)
            Limit(setting);

        Section("Stations");
        foreach (var setting in Config.Stations)
            Limit(setting);
    }

    private void Limit(StackSetting setting)
    {
        var s = setting;
        Stepper(s.Title, $"{s.Description}. 0 keeps the game's value. Shift steps by 100.", () => s.Entry.Value,
            v => Config.Set(s, (int)v), 0, Config.MaxStackLimit, 10, v => v <= 0 ? "Game" : $"{v:0}");
    }

    private static void Save(Action change)
    {
        change();
        Config.Set(Config.Backroom, Config.Backroom.On);   // writes the file and notifies like any other change
    }
}
