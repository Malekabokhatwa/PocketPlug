using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Vehicles;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using Console = Il2CppScheduleOne.Console;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Backroom.Pages;

/// <summary>Teleporting to places, properties and suppliers, and spawning vehicles.</summary>
internal sealed class TravelPage : ListPage
{
    public override string Id => "travel";
    public override string Title => "Travel";
    public override string Icon => "map-pin";

    private static PlayerMovement Movement => PlayerSingleton<PlayerMovement>.InstanceExists ? PlayerSingleton<PlayerMovement>.Instance : null;

    protected override void Define()
    {
        Section("Go to");
        Link("Places", "Landmarks around town.", _ => DefinePlaces(), "map-pin");
        Link("Properties", "Every property and business, owned or not.", _ => DefineProperties(), "building-2");
        Link("Suppliers", "Where your suppliers meet you.", _ => DefineSuppliers(), "users");

        Section("Vehicles");
        Link("Spawn a vehicle", "Any vehicle, parked in front of you. It's yours.", _ => DefineVehicles(), "car");
    }

    private void DefinePlaces()
    {
        Section("Places");
        var container = Console.Instance != null ? Console.Instance.TeleportPointsContainer : null;
        if (container == null)
            return;
        var seen = new HashSet<string>();
        var points = new List<Transform>();
        for (int i = 0; i < container.childCount; i++)
        {
            var point = container.GetChild(i);
            if (seen.Add(point.name))
                points.Add(point);
        }
        points.Sort((a, b) => string.Compare(K.Pretty(a.name), K.Pretty(b.name), System.StringComparison.OrdinalIgnoreCase));
        foreach (var point in points)
        {
            var target = point;
            string name = K.Pretty(point.name);
            Action(name, $"Teleports you to {name}.", () => Go(target.position, target.rotation, name));
        }
    }

    private void DefineProperties()
    {
        Section("Properties");
        var all = new List<Property>();
        foreach (var p in Property.Properties)
        {
            if (p != null && p.SpawnPoint != null)
                all.Add(p);
        }
        all.Sort((a, b) => b.IsOwned != a.IsOwned ? (a.IsOwned ? -1 : 1) : string.Compare(a.PropertyName, b.PropertyName, System.StringComparison.OrdinalIgnoreCase));
        foreach (var p in all)
        {
            var property = p;
            Action(p.PropertyName, $"Teleports you to {p.PropertyName}.", () =>
                Go(property.SpawnPoint.position + Vector3.up, property.SpawnPoint.rotation, property.PropertyName),
                value: () => property.IsOwned ? "Owned" : "");
        }
    }

    private void DefineSuppliers()
    {
        Section("Suppliers");
        var locations = SupplierLocation.AllLocations;
        if (locations == null || locations.Count == 0)
        {
            Readout("No supplier spots", "None are known in this save.", () => "");
            return;
        }
        for (int i = 0; i < locations.Count; i++)
        {
            var l = locations[i];
            if (l == null || l.SupplierStandPoint == null)
                continue;
            var target = l.SupplierStandPoint;
            string name = string.IsNullOrEmpty(l.LocationName) ? l.name : l.LocationName;
            Action(name, $"Teleports you to the supplier spot at {name}.", () => Go(target.position + target.forward * 1.5f, Quaternion.LookRotation(-target.forward), name));
        }
    }

    private void DefineVehicles()
    {
        Section("Vehicles");
        if (!NetworkSingleton<VehicleManager>.InstanceExists)
            return;
        var prefabs = NetworkSingleton<VehicleManager>.Instance.VehiclePrefabs;
        var list = new List<LandVehicle>();
        for (int i = 0; i < prefabs.Count; i++)
        {
            if (prefabs[i] != null && !string.IsNullOrEmpty(prefabs[i].VehicleCode))
                list.Add(prefabs[i]);
        }
        list.Sort((a, b) => a.VehiclePrice.CompareTo(b.VehiclePrice));
        foreach (var v in list)
        {
            string code = v.VehicleCode, name = string.IsNullOrEmpty(v.VehicleName) ? code : v.VehicleName;
            Action(name, $"Spawns a {name} in front of you, owned by you. Dealership price: {K.Money(v.VehiclePrice)}.", () =>
            {
                var mover = Movement;
                if (mover == null)
                    return;
                var t = mover.transform;
                var spawned = NetworkSingleton<VehicleManager>.Instance.SpawnAndReturnVehicle(code, t.position + t.forward * 4f + Vector3.up, t.rotation, true);
                if (spawned != null)
                    Done($"Spawned a {name}");
                else
                    Fail($"Couldn't spawn a {name}");
            }, value: () => K.Money(v.VehiclePrice));
        }
    }

    private static void Go(Vector3 position, Quaternion rotation, string name)
    {
        var mover = Movement;
        if (mover == null)
            return;
        mover.Teleport(position, rotation, false);
        Done($"Teleported to {name}");
    }
}
