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
        public GameObject GameIcon;
        public Vector2 LabelPos;
        public NPC Customer;
    }

    private static readonly Dictionary<System.IntPtr, Decoration> Decorations = new();

    public static void LateUpdate()
    {
        if (!Config.CompassDeals.On || !Singleton<CompassManager>.InstanceExists || !PlayerSingleton<PlayerCamera>.InstanceExists)
            return;

        var seen = new HashSet<System.IntPtr>();
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

        // The deal icon is the child the game instantiated next to the distance label.
        for (int i = 0; i < rect.childCount; i++)
        {
            var child = rect.GetChild(i);
            if (label == null || child.Pointer != label.transform.Pointer)
            {
                deco.GameIcon = child.gameObject;
                break;
            }
        }
        if (deco.GameIcon != null)
            deco.GameIcon.SetActive(false);

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

            deco.LabelPos = label.rectTransform.anchoredPosition;
            label.rectTransform.anchoredPosition = new Vector2(deco.LabelPos.x, DistanceY);
        }

        SetCustomer(deco, npc);
        Decorations[rect.Pointer] = deco;
        return deco;
    }

    private static void SetCustomer(Decoration deco, NPC npc)
    {
        deco.Customer = npc;
        deco.Photo.sprite = npc != null ? npc.MugshotSprite : null;
        deco.Photo.color = deco.Photo.sprite != null ? Color.white : new Color(0.3f, 0.3f, 0.3f, 1f);
        if (deco.Name != null)
            deco.Name.text = npc != null ? npc.FullName : "Deal";
    }

    private static void UpdateDistance(Decoration deco)
    {
        var element = deco.Element;
        if (element.DistanceLabel == null || element.TargetTransform == null)
            return;
        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        float meters = Vector3.Distance(cam.position, element.TargetTransform.position);
        element.DistanceLabel.text = UnitsUtility.FormatShortDistance(meters, UnitsUtility.ERoundingType.Up, 0);
    }

    private static void Undo(Decoration deco)
    {
        if (deco.Root != null)
            Object.Destroy(deco.Root);
        if (deco.GameIcon != null)
            deco.GameIcon.SetActive(true);
        if (deco.Element?.DistanceLabel != null)
            deco.Element.DistanceLabel.rectTransform.anchoredPosition = deco.LabelPos;
    }
}
