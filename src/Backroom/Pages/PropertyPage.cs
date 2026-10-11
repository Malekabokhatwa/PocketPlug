using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.PlayerScripts;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using Business = Il2CppScheduleOne.Property.Business;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Backroom.Pages;

/// <summary>Properties and businesses: take ownership, hire employees, teleport there.</summary>
internal sealed class PropertyPage : ListPage
{
    public override string Id => "property";
    public override string Title => "Property";
    public override string Icon => "building-2";

    private static readonly EEmployeeType[] Hires = { EEmployeeType.Botanist, EEmployeeType.Handler, EEmployeeType.Chemist, EEmployeeType.Cleaner };

    protected override void Define()
    {
        var properties = new List<Property>();
        var businesses = new List<Property>();
        foreach (var p in Property.Properties)
        {
            if (p == null)
                continue;
            (p.TryCast<Business>() != null ? businesses : properties).Add(p);
        }
        AddGroup("Properties", properties);
        AddGroup("Businesses", businesses);
    }

    private void AddGroup(string title, List<Property> list)
    {
        if (list.Count == 0)
            return;
        Section(title);
        list.Sort((a, b) => b.IsOwned != a.IsOwned ? (a.IsOwned ? -1 : 1) : a.Price.CompareTo(b.Price));
        foreach (var p in list)
        {
            var property = p;
            Link(p.PropertyName, $"{p.PropertyName}. Price {K.Money(p.Price)}.", _ => DefineProperty(property), "building-2",
                () => property.IsOwned ? $"Owned · {property.Employees.Count}/{property.EmployeeCapacity} staff" : K.Money(property.Price),
                p.PropertyName);
        }
    }

    private void DefineProperty(Property p)
    {
        Section(p.PropertyName);
        Readout("Status", "Whether you own it, and its staff.", () => p.IsOwned ? $"Owned · {p.Employees.Count}/{p.EmployeeCapacity} staff" : $"For sale · {K.Money(p.Price)}");
        Action("Take ownership", "Makes the property yours for free, like buying it.", () =>
        {
            if (p.IsOwned)
            {
                Fail($"You already own {p.PropertyName}");
                return;
            }
            p.SetOwned();
            Done($"You now own {p.PropertyName}");
        }, "check", enabled: () => !p.IsOwned);
        if (p.SpawnPoint != null)
        {
            Action("Teleport there", $"Takes you to {p.PropertyName}.", () =>
            {
                if (!PlayerSingleton<PlayerMovement>.InstanceExists)
                    return;
                PlayerSingleton<PlayerMovement>.Instance.Teleport(p.SpawnPoint.position + Vector3.up, p.SpawnPoint.rotation, false);
                Done($"Teleported to {p.PropertyName}");
            }, "map-pin");
        }

        if (p.EmployeeCapacity <= 0)
            return;
        Section("Hire");
        foreach (var type in Hires)
        {
            var t = type;
            Action($"Hire a {t.ToString().ToLowerInvariant()}", $"Adds a new {t.ToString().ToLowerInvariant()} to {p.PropertyName}. You still pay their daily wage.", () =>
            {
                if (!p.IsOwned)
                {
                    Fail("Take ownership first");
                    return;
                }
                if (p.Employees.Count >= p.EmployeeCapacity)
                {
                    Fail($"{p.PropertyName} has no room for more staff");
                    return;
                }
                NetworkSingleton<EmployeeManager>.Instance.CreateNewEmployee(p, t);
                Done($"Hired a {t.ToString().ToLowerInvariant()} at {p.PropertyName}");
            }, "user", enabled: () => p.IsOwned && p.Employees.Count < p.EmployeeCapacity);
        }
    }
}
