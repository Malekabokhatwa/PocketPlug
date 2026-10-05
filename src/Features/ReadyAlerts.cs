using System;
using System.Collections.Generic;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PocketPlug.Features;

/// <summary>
/// Phone notifications when grows and stations finish. Every couple of seconds each pot, mushroom bed and station is
/// checked; a notification goes out only when one changes from "working" to "done" (never for things already done
/// when the save loads). Several finishing at once are grouped into one notification.
/// </summary>
internal static class ReadyAlerts
{
    private sealed class Kind
    {
        public string Singular, Plural, ItemId;
        public readonly List<string> Places = new();
    }

    private static readonly Dictionary<IntPtr, int> State = new();
    private static float _next;
    private static bool _primed;

    private static readonly Kind Plants = new() { Singular = "Plant ready to harvest", Plural = "plants ready to harvest", ItemId = "plasticpot" };
    private static readonly Kind Shrooms = new() { Singular = "Shrooms ready to harvest", Plural = "mushroom beds ready", ItemId = "mushroombed" };
    private static readonly Kind Mixers = new() { Singular = "Mixing done", Plural = "mixing stations done", ItemId = "mixingstation" };
    private static readonly Kind Racks = new() { Singular = "Drying done", Plural = "drying racks done", ItemId = "dryingrack" };
    private static readonly Kind Chem = new() { Singular = "Chemistry station done", Plural = "chemistry stations done", ItemId = "chemistrystation" };
    private static readonly Kind Ovens = new() { Singular = "Lab oven done", Plural = "lab ovens done", ItemId = "laboven" };
    private static readonly Kind Cauldrons = new() { Singular = "Cauldron done", Plural = "cauldrons done", ItemId = "cauldron" };
    private static readonly Kind[] Kinds = { Plants, Shrooms, Mixers, Racks, Chem, Ovens, Cauldrons };

    public static void Reset()
    {
        State.Clear();
        _primed = false;
        _next = Time.unscaledTime + 5f;
    }

    public static void Update()
    {
        if (!Config.ReadyAlerts.On || Time.unscaledTime < _next || !Singleton<NotificationsManager>.InstanceExists)
            return;
        _next = Time.unscaledTime + 2f;

        foreach (var pot in Object.FindObjectsOfType<Pot>())
            Check(Plants, pot, pot.Plant != null && pot.Plant.IsFullyGrown ? 1 : 0, rising: true);
        foreach (var bed in Object.FindObjectsOfType<MushroomBed>())
            Check(Shrooms, bed, bed.CurrentColony != null && bed.CurrentColony.IsFullyGrown ? 1 : 0, rising: true);
        foreach (var mixer in Object.FindObjectsOfType<MixingStation>())
            Check(Mixers, mixer, mixer.CurrentMixOperation != null && mixer.IsMixingDone ? 1 : 0, rising: true);
        foreach (var chem in Object.FindObjectsOfType<ChemistryStation>())
            Check(Chem, chem, chem.CurrentCookOperation != null && chem.CurrentCookOperation.IsComplete() ? 1 : 0, rising: true);
        foreach (var oven in Object.FindObjectsOfType<LabOven>())
            Check(Ovens, oven, oven.IsReadyForHarvest() ? 1 : 0, rising: true);
        // The cauldron and drying rack move finished items straight to their output slot.
        foreach (var cauldron in Object.FindObjectsOfType<Cauldron>())
            Check(Cauldrons, cauldron, cauldron.OutputSlot != null ? cauldron.OutputSlot.Quantity : 0, rising: false);
        foreach (var rack in Object.FindObjectsOfType<DryingRack>())
            Check(Racks, rack, rack.OutputSlot != null ? rack.OutputSlot.Quantity : 0, rising: false);

        if (_primed)
            Send();
        foreach (var kind in Kinds)
            kind.Places.Clear();
        _primed = true;
    }

    /// <param name="rising">true: a 0 → 1 flag; false: notify whenever the count goes up.</param>
    private static void Check(Kind kind, BuildableItem item, int value, bool rising)
    {
        if (item == null)
            return;
        bool had = State.TryGetValue(item.Pointer, out int before);
        State[item.Pointer] = value;
        if (!had || !_primed)
            return;
        bool done = rising ? before == 0 && value == 1 : value > before;
        if (done)
            kind.Places.Add(item.ParentProperty != null ? item.ParentProperty.PropertyName : "");
    }

    private static void Send()
    {
        foreach (var kind in Kinds)
        {
            if (kind.Places.Count == 0)
                continue;
            string title = kind.Places.Count == 1 ? kind.Singular : $"{kind.Places.Count} {kind.Plural}";
            string place = kind.Places[0];
            bool samePlace = kind.Places.TrueForAll(p => p == place);
            string subtitle = samePlace && place != "" ? place : "At your properties";
            var icon = Registry.GetItem(kind.ItemId)?.Icon;
            Singleton<NotificationsManager>.Instance.SendNotification(title, subtitle, icon, 5f, true);
        }
    }
}
