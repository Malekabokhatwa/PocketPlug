using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Compass;
using Il2CppTMPro;
using PocketPlug.Util;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PocketPlug.Features;

/// <summary>
/// Deal markers on the compass show the customer's portrait (the one from the Contacts app) in a green ring, their
/// name, and the game's own distance label underneath, at any distance.
/// </summary>
internal static class CompassDeals
{
    private const float Ring = 36f;
    private const float NameY = -30f;
    private const float DistanceY = -46f;

    private sealed class Decoration
    {
        public CompassManager.Element Element;
        public GameObject Root;
        public Image Photo;
        public TextMeshProUGUI Name;
        public readonly List<GameObject> Hidden = new();
        public NPC Customer;
        public TextMeshProUGUI Distance;   // our copy of the game's distance label, under the name
        public int LastShownMeters = -1;
    }

    private static readonly Dictionary<System.IntPtr, Decoration> Decorations = new();
    private static readonly HashSet<System.IntPtr> Seen = new();

    /// <summary>Dev-only test markers (element, npc), decorated like real deals.</summary>
    internal static readonly List<(CompassManager.Element element, NPC npc)> DevMarkers = new();

    private static readonly Vector4 ClipPadding = new(0f, -60f, 0f, 0f);
    private static RectMask2D _clip;

    public static void LateUpdate()
    {
        if (!Config.CompassDeals.On || !Singleton<CompassManager>.InstanceExists || !PlayerSingleton<PlayerCamera>.InstanceExists)
            return;

        // Compass markers sit in a 30px-tall container that clips with RectMask2D; extend it down for the name and distance.
        if (_clip == null)
        {
            _clip = Singleton<CompassManager>.Instance.ElementUIContainer.GetComponent<RectMask2D>();
            if (_clip != null)
                _clip.padding = ClipPadding;
        }

        var seen = Seen;
        seen.Clear();
        var contracts = Contract.Contracts;
        for (int i = 0; i < contracts.Count; i++)
        {
            var contract = contracts[i];
            if (contract == null || contract.State != EQuestState.Active)
                continue;
            var npc = contract.Customer != null ? contract.Customer.GetComponent<NPC>() : null;

            var entries = contract.Entries;
            for (int j = 0; j < entries.Count; j++)
            {
                var element = entries[j].compassElement;
                if (element == null || element.Rect == null)
                    continue;
                seen.Add(element.Rect.Pointer);
                var deco = Decorate(element, npc);
                UpdateDistance(deco);
            }
        }

        foreach (var (element, npc) in DevMarkers)
        {
            seen.Add(element.Rect.Pointer);
            UpdateDistance(Decorate(element, npc));
        }

        // Elements that went away with their deal.
        if (Decorations.Count > seen.Count)
        {
            var stale = new List<System.IntPtr>();
            foreach (var key in Decorations.Keys)
            {
                if (!seen.Contains(key))
                    stale.Add(key);
            }
            foreach (var key in stale)
                Decorations.Remove(key);
        }
    }

    /// <summary>Called when settings change: put the vanilla look back if the feature was switched off.</summary>
    public static void Refresh()
    {
        if (Config.CompassDeals.On)
            return;
        foreach (var deco in Decorations.Values)
            Undo(deco);
        Decorations.Clear();
        if (_clip != null)
            _clip.padding = Vector4.zero;
        _clip = null;
    }

    private static Decoration Decorate(CompassManager.Element element, NPC npc)
    {
        if (Decorations.TryGetValue(element.Rect.Pointer, out var deco) && deco.Root != null)
        {
            if (npc != null && (deco.Customer == null || deco.Customer.Pointer != npc.Pointer))
                SetCustomer(deco, npc);
            return deco;
        }

        deco = new Decoration { Element = element };
        var rect = element.Rect;
        var label = element.DistanceLabel;

        // Hide the deal icon (and anything else the game put on the element) except the distance label.
        for (int i = 0; i < rect.childCount; i++)
        {
            var child = rect.GetChild(i).gameObject;
            if (label != null && child.Pointer == label.gameObject.Pointer)
                continue;
            if (child.activeSelf)
            {
                child.SetActive(false);
                deco.Hidden.Add(child);
            }
        }

        var root = Ui.Rect("PocketPlug_Deal", rect);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(Ring, Ring);
        root.anchoredPosition = Vector2.zero;
        deco.Root = root.gameObject;

        // Circle mask with the portrait inside, then the ring on top.
        var mask = Ui.Image("Mask", root, Color.white, Assets.Sprite("disc"));
        Ui.Stretch(mask.rectTransform, 3, 3, 3, 3);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        deco.Photo = Ui.Image("Photo", mask.transform, Color.white);
        Ui.Stretch(deco.Photo.rectTransform);
        deco.Photo.preserveAspect = true;
        var ring = Ui.Image("Ring", root, Color.white, Assets.Sprite("ring"));
        Ui.Stretch(ring.rectTransform);

        if (label != null)
        {
            // The name reuses the distance label's style; the label itself moves under the name.
            deco.Name = Object.Instantiate(label.gameObject, root).GetComponent<TextMeshProUGUI>();
            deco.Name.gameObject.name = "Name";
            var nameRect = deco.Name.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.sizeDelta = new Vector2(220, 20);
            nameRect.anchoredPosition = new Vector2(0, NameY);
            deco.Name.alignment = TextAlignmentOptions.Center;
            deco.Name.enableWordWrapping = false;

            // A copy of the game's distance label (same font, style and units) sits under the name. The game's own
            // label is hidden: it blanks itself beyond 50 m every frame, so updating it would mean a new string
            // every frame. The copy only changes when the shown number does.
            deco.Distance = Object.Instantiate(label.gameObject, root).GetComponent<TextMeshProUGUI>();
            deco.Distance.gameObject.name = "Distance";
            var distRect = deco.Distance.rectTransform;
            distRect.anchorMin = distRect.anchorMax = new Vector2(0.5f, 0.5f);
            distRect.pivot = new Vector2(0.5f, 0.5f);
            distRect.sizeDelta = new Vector2(220, 20);
            distRect.anchoredPosition = new Vector2(0, DistanceY);
            deco.Distance.alignment = TextAlignmentOptions.Center;
            deco.Distance.enableWordWrapping = false;
            label.gameObject.SetActive(false);
        }

        SetCustomer(deco, npc);
        Decorations[rect.Pointer] = deco;
        return deco;
    }

    private static void SetCustomer(Decoration deco, NPC npc)
    {
        deco.Customer = npc;
        deco.Photo.sprite = Npcs.Mugshot(npc);
        deco.Photo.color = deco.Photo.sprite != null ? Color.white : new Color(0.3f, 0.3f, 0.3f, 1f);
        if (deco.Name != null)
            deco.Name.text = npc != null ? npc.FullName : "Deal";
    }

    private static void UpdateDistance(Decoration deco)
    {
        var element = deco.Element;
        if (deco.Distance == null || element.TargetTransform == null)
            return;
        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        float meters = Vector3.Distance(cam.position, element.TargetTransform.position);
        // Only rewrite when the shown number changes: a new string every frame is pure GC churn.
        int rounded = Mathf.CeilToInt(meters);
        if (rounded == deco.LastShownMeters)
            return;
        deco.LastShownMeters = rounded;
        deco.Distance.text = UnitsUtility.FormatShortDistance(meters, UnitsUtility.ERoundingType.Up, 0);
    }

    private static void Undo(Decoration deco)
    {
        if (deco.Root != null)
            Object.Destroy(deco.Root);
        foreach (var go in deco.Hidden)
        {
            if (go != null)
                go.SetActive(true);
        }
        if (deco.Element?.DistanceLabel != null)
            deco.Element.DistanceLabel.gameObject.SetActive(true);
    }
}
