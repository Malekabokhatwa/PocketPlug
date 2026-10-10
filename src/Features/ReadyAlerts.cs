using System;
using System.Collections.Generic;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Features;

/// <summary>
/// Phone notifications when grows and stations finish. Only items at owned properties are checked (from each
/// property's BuildableItems list, never a scene-wide search), a few per frame, so it never costs a visible frame.
/// A notification goes out only when an item changes from "working" to "done", never for things already done when
/// the save loads. Several finishing in the same pass are grouped into one notification.
/// </summary>
internal static class ReadyAlerts
{
    private const int ItemsPerFrame = 12;
    private const int NewItemsPerFrame = 3;   // working out an item's type costs more than checking it
    private static int _classifiedThisFrame;
    private const float PassInterval = 2f;

    private sealed class Kind
    {
        public string Singular, Plural, ItemId;
        public readonly List<string> Places = new();
    }

    /// <summary>What an item is, and how to read its state; worked out once per item.</summary>
    private sealed class Tracked
    {
        public Kind Kind;
        public UnityEngine.Object Owner;   // for the destroyed check
        public Func<int> Read;
        public bool Rising;
        public int Last;
        public bool Seen;
        /// <summary>Optional output slot quantity: a rise also counts as done (stations that empty their operation the moment it finishes).</summary>
        public Func<int> Output;
        public int LastOutput;
        public int Pass;
    }

    private static int _passId;

    private static readonly Kind Plants = new() { Singular = "Plant ready to harvest", Plural = "plants ready to harvest", ItemId = "plasticpot" };
    private static readonly Kind Shrooms = new() { Singular = "Shrooms ready to harvest", Plural = "mushroom beds ready", ItemId = "mushroombed" };
    private static readonly Kind Mixers = new() { Singular = "Mixing done", Plural = "mixing stations done", ItemId = "mixingstation" };
    private static readonly Kind Racks = new() { Singular = "Drying done", Plural = "drying racks done", ItemId = "dryingrack" };
    private static readonly Kind Chem = new() { Singular = "Chemistry station done", Plural = "chemistry stations done", ItemId = "chemistrystation" };
    private static readonly Kind Ovens = new() { Singular = "Lab oven done", Plural = "lab ovens done", ItemId = "laboven" };
    private static readonly Kind Cauldrons = new() { Singular = "Cauldron done", Plural = "cauldrons done", ItemId = "cauldron" };
    private static readonly Kind[] Kinds = { Plants, Shrooms, Mixers, Racks, Chem, Ovens, Cauldrons };

    private static readonly Dictionary<IntPtr, Tracked> Cache = new();
    private static readonly List<BuildableItem> Pass = new();
    private static readonly List<IntPtr> Stale = new();
    private static int _cursor;
    private static float _nextPass;
    private static bool _primed;

    /// <summary>
    /// The first TryCast&lt;T&gt; per type does one-time IL2CPP interop setup (~10 ms for these seven). Doing it
    /// while the loading screen is up means it never costs a frame during play.
    /// </summary>
    public static void Warmup()
    {
        var probe = new Il2CppSystem.Object();
        probe.TryCast<BuildableItem>();
        probe.TryCast<Pot>();
        probe.TryCast<MushroomBed>();
        probe.TryCast<MixingStation>();
        probe.TryCast<ChemistryStation>();
        probe.TryCast<LabOven>();
        probe.TryCast<Cauldron>();
        probe.TryCast<DryingRack>();

        // Walk the property lists once and work out every item's type now (first successful casts are the
        // costly part), then start clean. Items spawned later are classified a few per frame as usual.
        StartPassInner();
        foreach (var item in Pass)
        {
            if (item != null && !Cache.ContainsKey(item.Pointer))
                Cache[item.Pointer] = Track(item);
        }
        Core.Log.Msg($"Ready alerts: {Cache.Count} items prepared while loading.");
        Pass.Clear();
        _cursor = 0;
        _nextPass = Time.unscaledTime + 5f;
    }

    public static void Reset()
    {
        Cache.Clear();
        Pass.Clear();
        _cursor = 0;
        _primed = false;
        _nextPass = Time.unscaledTime + 5f;
    }

    public static void Update()
    {
        if (!Config.ReadyAlerts.On || !Singleton<NotificationsManager>.InstanceExists)
            return;

        if (_cursor >= Pass.Count)
        {
            if (Pass.Count > 0)
                FinishPass();
            if (Time.unscaledTime < _nextPass)
                return;
            StartPass();
            return;
        }

        _classifiedThisFrame = 0;
        int end = Math.Min(_cursor + ItemsPerFrame, Pass.Count);
        for (; _cursor < end && _classifiedThisFrame < NewItemsPerFrame; _cursor++)
            Check(Pass[_cursor]);
    }

    private static void StartPass()
    {
        Util.Perf.Begin();
        StartPassInner();
        Util.Perf.End($"ReadyAlerts.StartPass ({Pass.Count} items)");
    }

    private static void StartPassInner()
    {
        _nextPass = Time.unscaledTime + PassInterval;
        Pass.Clear();
        _cursor = 0;
        var owned = Property.OwnedProperties;
        for (int i = 0; i < owned.Count; i++)
        {
            var items = owned[i]?.BuildableItems;
            if (items == null)
                continue;
            for (int j = 0; j < items.Count; j++)
            {
                if (items[j] != null)
                    Pass.Add(items[j]);
            }
        }
    }

    private static void FinishPass()
    {
        // Forget items that weren't in this pass (sold, picked up, destroyed).
        Stale.Clear();
        foreach (var kv in Cache)
        {
            if (kv.Value.Pass != _passId)
                Stale.Add(kv.Key);
        }
        foreach (var key in Stale)
            Cache.Remove(key);
        _passId++;

        if (_primed)
            Send();
        foreach (var kind in Kinds)
            kind.Places.Clear();
        _primed = true;
        Pass.Clear();
        _cursor = 0;
    }

    /// <summary>
    /// Items that aren't tracked (furniture, lights...) are cached too, with no kind, so they're classified once
    /// instead of on every pass.
    /// </summary>
    private static Tracked Track(BuildableItem item) => Classify(item) ?? new Tracked { Owner = item };

    private static void Check(BuildableItem item)
    {
        if (item == null)
            return;
        if (!Cache.TryGetValue(item.Pointer, out var t) || t.Owner == null)
        {
            // New item, or the cached one was destroyed and its native pointer reused.
            _classifiedThisFrame++;
            t = Track(item);
            Cache[item.Pointer] = t;
        }
        t.Pass = _passId;
        if (t.Kind == null)
            return;

        int value = t.Read();
        int output = t.Output != null ? t.Output() : 0;
        bool had = t.Seen;
        int before = t.Last;
        int outputBefore = t.LastOutput;
        t.Last = value;
        t.LastOutput = output;
        t.Seen = true;
        if (!had || !_primed)
            return;

        bool done = t.Rising ? before == 0 && value == 1 : value > before;
        // Known recipes empty the operation the moment they finish, so only the output rise shows it. A new-recipe
        // mix already alerted when its flag rose; its output appears later when the player reveals it.
        if (t.Output != null && output > outputBefore && before == 0)
            done = true;
        if (done)
            t.Kind.Places.Add(item.ParentProperty != null ? item.ParentProperty.PropertyName : "");
    }

    /// <summary>Null for items that never finish anything (furniture, lights...).</summary>
    private static Tracked Classify(BuildableItem item)
    {
        var pot = item.TryCast<Pot>();
        if (pot != null)
            return new Tracked { Owner = pot, Kind = Plants, Rising = true, Read = () => pot.Plant != null && pot.Plant.IsFullyGrown ? 1 : 0 };
        var bed = item.TryCast<MushroomBed>();
        if (bed != null)
            return new Tracked { Owner = bed, Kind = Shrooms, Rising = true, Read = () => bed.CurrentColony != null && bed.CurrentColony.IsFullyGrown ? 1 : 0 };
        var mixer = item.TryCast<MixingStation>();
        if (mixer != null)
            return new Tracked
            {
                Kind = Mixers, Owner = mixer, Rising = true,
                Read = () => mixer.CurrentMixOperation != null && mixer.IsMixingDone ? 1 : 0,
                Output = () => mixer.OutputSlot != null ? mixer.OutputSlot.Quantity : 0
            };
        var chem = item.TryCast<ChemistryStation>();
        if (chem != null)
            return new Tracked
            {
                Kind = Chem, Owner = chem, Rising = true,
                Read = () => chem.CurrentCookOperation != null && chem.CurrentCookOperation.IsComplete() ? 1 : 0,
                Output = () => chem.OutputSlot != null ? chem.OutputSlot.Quantity : 0
            };
        var oven = item.TryCast<LabOven>();
        if (oven != null)
            return new Tracked { Owner = oven, Kind = Ovens, Rising = true, Read = () => oven.IsReadyForHarvest() ? 1 : 0 };
        // The cauldron and drying rack move finished items straight to their output slot.
        var cauldron = item.TryCast<Cauldron>();
        if (cauldron != null)
            return new Tracked { Owner = cauldron, Kind = Cauldrons, Rising = false, Read = () => cauldron.OutputSlot != null ? cauldron.OutputSlot.Quantity : 0 };
        var rack = item.TryCast<DryingRack>();
        if (rack != null)
            return new Tracked { Owner = rack, Kind = Racks, Rising = false, Read = () => rack.OutputSlot != null ? rack.OutputSlot.Quantity : 0 };
        return null;
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
