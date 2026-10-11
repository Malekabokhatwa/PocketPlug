using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using Il2CppScheduleOne.SpecialCustomers;
using Il2CppTMPro;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using UnityEngine.UI;
using Console = Il2CppScheduleOne.Console;

namespace PocketPlug.Backroom.Pages;

/// <summary>Every NPC by region, with their portrait: unlock, relationship, teleport. Plus special customer groups.</summary>
internal sealed class PeoplePage : ListPage
{
    public override string Id => "people";
    public override string Title => "People";
    public override string Icon => "users";

    private static readonly string[] Relations = { "Hostile", "Unfriendly", "Neutral", "Friendly", "Loyal" };

    private NPC _person;          // the NPC whose detail list is open
    private readonly Dictionary<string, NPC> _byRow = new();
    private Image _photo;
    private TextMeshProUGUI _photoCaption;

    protected override void Define()
    {
        _byRow.Clear();
        var byRegion = new SortedDictionary<string, List<NPC>>();
        foreach (var npc in NPCManager.NPCRegistry)
        {
            if (npc == null || string.IsNullOrEmpty(npc.FirstName))
                continue;
            string region = K.Pretty(npc.Region.ToString());
            if (!byRegion.TryGetValue(region, out var list))
                byRegion[region] = list = new List<NPC>();
            list.Add(npc);
        }
        foreach (var (region, list) in byRegion)
        {
            Section(region);
            list.Sort((a, b) => string.Compare(a.FullName, b.FullName, System.StringComparison.OrdinalIgnoreCase));
            foreach (var npc in list)
            {
                var person = npc;
                Link(npc.FullName, $"Lives in {region}. Open to unlock them, set how they feel about you, or go to them.",
                    _ => DefinePerson(person), value: () => Status(person), path: npc.FullName);
                _byRow[npc.FullName] = npc;
            }
        }

        Section("Special customers");
        Link("Customer groups", "Queue a special group of customers (hippies, bikers...). They arrive the next day.", _ => DefineGroups(), "users");
    }

    private void DefinePerson(NPC npc)
    {
        _person = npc;
        Section(npc.FullName);
        Readout("Relationship", "How they feel about you, from Hostile to Loyal.", () => RelationText(npc), "heart");
        Stepper("Set relationship", "0 is hostile, 5 is loyal.", () => npc.RelationData != null ? npc.RelationData.RelationDelta : 0f,
            v => npc.RelationData?.SetRelationship(v, true), 0f, 5f, 0.5f, v => $"{v:0.0}  {Relations[Mathf.Clamp(Mathf.FloorToInt(v), 0, 4)]}");
        Action("Unlock", "Unlocks them as a contact (as if you'd been introduced).", () =>
        {
            if (npc.RelationData == null)
                return;
            if (npc.RelationData.Unlocked)
            {
                Fail($"{npc.FirstName} is already unlocked");
                return;
            }
            npc.RelationData.Unlock(NPCRelationData.EUnlockType.DirectApproach, true);
            Done($"Unlocked {npc.FullName}");
        }, "lock", enabled: () => npc.RelationData != null && !npc.RelationData.Unlocked);
        Action("Teleport to them", "Takes you to wherever they are right now.", () =>
        {
            Console.SubmitCommand($"teleport {npc.ID}");
            Done($"Teleported to {npc.FirstName}");
        }, "map-pin");
    }

    private void DefineGroups()
    {
        Section("Customer groups");
        if (!NetworkSingleton<SpecialCustomerManager>.InstanceExists)
            return;
        var groups = NetworkSingleton<SpecialCustomerManager>.Instance._specialCustomerGroups;
        for (int i = 0; groups != null && i < groups.Count; i++)
        {
            var g = groups[i];
            if (g == null || string.IsNullOrEmpty(g.GroupId))
                continue;
            string id = g.GroupId, name = string.IsNullOrEmpty(g.GroupName) ? K.Pretty(id) : g.GroupName;
            Action(name, $"Queues the {name} group. They arrive the next day.", () =>
            {
                NetworkSingleton<SpecialCustomerManager>.Instance.QueueCustomerGroup(id);
                Done($"{name} are coming tomorrow");
            });
        }
    }

    private static string Status(NPC npc) => npc.RelationData == null ? "" : npc.RelationData.Unlocked ? RelationName(npc) : "Locked";

    private static string RelationName(NPC npc) =>
        Relations[Mathf.Clamp(Mathf.FloorToInt(npc.RelationData.RelationDelta), 0, 4)];

    private static string RelationText(NPC npc) =>
        npc.RelationData == null ? "—" : $"{RelationName(npc)}  ({npc.RelationData.RelationDelta:0.0} / 5)";

    /// <summary>The info box shows the selected person's portrait.</summary>
    protected override void OnInfo(Row row)
    {
        if (_photo == null)
        {
            var frame = K.Box("PhotoFrame", InfoExtra, Theme.Raised, 12);
            K.At(frame.rectTransform, 0, 1, 168, 168, 0, 0);
            _photo = K.Rect("Photo", frame.rectTransform).gameObject.AddComponent<Image>();
            _photo.preserveAspect = true;
            _photo.raycastTarget = false;
            K.Fill(_photo.rectTransform, 6, 6, 6, 6);
            _photoCaption = K.Label("Caption", InfoExtra, "", Theme.Small, Theme.Text2, Theme.Medium);
            K.At(_photoCaption.rectTransform, 0, 1, 360, Theme.Line(Theme.Small), 0, -178);
        }
        NPC npc = null;
        if (row != null && _byRow.TryGetValue(row.Title, out var found))
            npc = found;
        else if (_person != null && row != null && Breadcrumb.Contains(_person.FullName.ToUpperInvariant()))
            npc = _person;
        var sprite = npc != null ? Util.Npcs.Mugshot(npc) : null;
        _photo.transform.parent.gameObject.SetActive(sprite != null);
        _photo.sprite = sprite;
        _photoCaption.text = npc != null ? RelationText(npc) : "";
    }
}
