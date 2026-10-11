using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.PlayerScripts.Health;
using PocketPlug.Backroom.Kit;
using EPursuitLevel = Il2CppScheduleOne.PlayerScripts.PlayerCrimeData.EPursuitLevel;

namespace PocketPlug.Backroom.Pages;

/// <summary>The first page: the actions people reach for most, and whatever you ran recently.</summary>
internal sealed class HomePage : ListPage
{
    public override string Id => "home";
    public override string Title => "Home";
    public override string Icon => "house";

    private string _recentKey = "";

    protected override void Define()
    {
        Section("Quick");
        Action("Add $1,000 cash", "Adds $1,000 to the cash you carry.", () =>
        {
            NetworkSingleton<MoneyManager>.Instance.ChangeCashBalance(1000f, true, true);
            Done("Added $1,000 cash");
        }, "banknote");
        Action("Heal to full", "Restores your health.", () =>
        {
            var health = Player.Local?.Health;
            if (health == null || !health.IsAlive)
            {
                Fail("Can't heal right now");
                return;
            }
            health.SetHealth(PlayerHealth.MaxHealth);
            Done("Healed to full");
        }, "heart");
        Action("Clear wanted level", "Ends any police pursuit.", () =>
        {
            var crime = Player.Local?.CrimeData;
            if (crime == null)
                return;
            crime.SetPursuitLevel(EPursuitLevel.None);
            crime.ClearCrimes();
            Done("Wanted level cleared");
        }, "shield");
        Action("Skip to noon", "Jumps the clock to 12:00 PM.", () =>
        {
            NetworkSingleton<TimeManager>.Instance.SetTime_Server(1200);
            Done("Time set to 12:00 PM");
        }, "clock");
        Action("Sleep now", "Starts sleeping right away.", () =>
        {
            NetworkSingleton<SleepController>.Instance.StartSleep();
            Done("Sleeping");
        }, "moon");

        Section("Recent");
        if (Backroom.Recents.Count == 0)
            Readout("Nothing yet", "Actions you run anywhere in Backroom show up here, so you can repeat them in one click.", () => "", "history");
        foreach (var (title, run) in Backroom.Recents)
        {
            var again = run;
            Action(title, "Runs this again.", again, "history");
        }
        _recentKey = RecentKey();
    }

    private static string RecentKey()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (title, _) in Backroom.Recents)
            sb.Append(title).Append('|');
        return sb.ToString();
    }

    protected internal override void OnShow()
    {
        if (RecentKey() != _recentKey)
            Rebuild();
        base.OnShow();
    }
}
