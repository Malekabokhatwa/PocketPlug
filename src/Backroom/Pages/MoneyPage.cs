using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using PocketPlug.Backroom.Kit;

namespace PocketPlug.Backroom.Pages;

/// <summary>Cash and bank balance: presets and a custom amount, clamped like the game (0 to $1B).</summary>
internal sealed class MoneyPage : ListPage
{
    public override string Id => "money";
    public override string Title => "Money";
    public override string Icon => "banknote";

    private const float MaxBalance = 1_000_000_000f;
    private float _amount = 1000f;

    private static MoneyManager Money => NetworkSingleton<MoneyManager>.InstanceExists ? NetworkSingleton<MoneyManager>.Instance : null;
    private static float Cash => Money?.cashBalance ?? 0f;
    private static float Bank => Money?.sync___get_value_onlineBalance() ?? 0f;

    protected override void Define()
    {
        Section("Cash");
        Readout("Cash on hand", "The cash you're carrying.", () => K.Money(Cash), "banknote");
        foreach (float preset in new[] { 100f, 1_000f, 10_000f, 100_000f })
        {
            float amount = preset;
            Action($"Add {K.Money(amount)} cash", $"Adds {K.Money(amount)} to the cash you carry.", () => ChangeCash(amount));
        }
        Action("Clear cash", "Sets your cash to $0.", () => ChangeCash(-Cash), hold: true);

        Section("Bank");
        Readout("Bank balance", "Your online balance.", () => K.Money(Bank), "landmark");
        foreach (float preset in new[] { 1_000f, 10_000f, 100_000f, 1_000_000f })
        {
            float amount = preset;
            Action($"Add {K.Money(amount)} to bank", $"Deposits {K.Money(amount)} straight into your account.", () => ChangeBank(amount));
        }
        Action("Clear bank balance", "Sets your bank balance to $0.", () => ChangeBank(-Bank), hold: true);

        Section("Custom amount");
        Stepper("Amount", "Pick an amount, then add or remove it below. Shift steps ten times faster.",
            () => _amount, v => _amount = v, 100f, MaxBalance, 100f, v => K.Money(v));
        Action("Add amount to cash", "Adds the amount above to your cash.", () => ChangeCash(_amount));
        Action("Remove amount from cash", "Takes the amount above from your cash (never below $0).", () => ChangeCash(-_amount));
        Action("Add amount to bank", "Adds the amount above to your bank balance.", () => ChangeBank(_amount));
        Action("Remove amount from bank", "Takes the amount above from your bank balance (never below $0).", () => ChangeBank(-_amount));
    }

    private static void ChangeCash(float delta)
    {
        var money = Money;
        if (money == null)
            return;
        float wanted = delta;
        delta = Math.Clamp(delta, -money.cashBalance, MaxBalance - money.cashBalance);
        if (delta == 0f)
        {
            Fail(wanted < 0 ? "No cash to remove" : "Cash is already at the limit");
            return;
        }
        money.ChangeCashBalance(delta, true, true);
        Done(delta > 0 ? $"Added {K.Money(delta)} cash" : $"Removed {K.Money(-delta)} cash");
    }

    private static void ChangeBank(float delta)
    {
        var money = Money;
        if (money == null)
            return;
        float balance = money.sync___get_value_onlineBalance();
        float wanted = delta;
        delta = Math.Clamp(delta, -balance, MaxBalance - balance);
        if (delta == 0f)
        {
            Fail(wanted < 0 ? "No balance to remove" : "Balance is already at the limit");
            return;
        }
        money.CreateOnlineTransaction("Backroom", delta, 1f, delta > 0 ? "Added in Backroom" : "Removed in Backroom");
        Done(delta > 0 ? $"Added {K.Money(delta)} to your bank" : $"Removed {K.Money(-delta)} from your bank");
    }
}
