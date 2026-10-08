using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI;
using UnityEngine;

namespace PocketPlug.Features;

/// <summary>One notification per deal when about an in-game hour is left to deliver it.</summary>
internal static class DealReminders
{
    private const int WarnAtMinutes = 60;
    private static readonly HashSet<string> Warned = new();
    private static float _next;

    public static void Reset()
    {
        Warned.Clear();
        _next = Time.unscaledTime + 5f;
    }

    public static void Update()
    {
        if (!Config.DealExpiry.On || Time.unscaledTime < _next || !Singleton<NotificationsManager>.InstanceExists)
            return;
        _next = Time.unscaledTime + 2f;

        var contracts = Contract.Contracts;
        for (int i = 0; i < contracts.Count; i++)
        {
            var c = contracts[i];
            if (c == null || c.State != EQuestState.Active || !c.Expires || c.Dealer != null || Warned.Contains(c.GUID.ToString()))
                continue;

            int mins = c.GetMinsUntilExpiry();
            if (mins <= 0 || mins > WarnAtMinutes)
                continue;

            Warned.Add(c.GUID.ToString());
            var npc = c.Customer != null ? c.Customer.GetComponent<NPC>() : null;
            string who = npc != null ? npc.FullName : "A customer";
            Singleton<NotificationsManager>.Instance.SendNotification(
                "<color=#FFB43C>Deal ending soon</color>", $"{who}: {mins} min left", Util.Npcs.Mugshot(npc), 6f, true);
        }
    }
}
