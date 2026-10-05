using System;
using System.IO;
using System.Text;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Persistence;
using MelonLoader.Utils;
using PocketPlug.Phone;
using UnityEngine;
using GamePhone = Il2CppScheduleOne.UI.Phone.Phone;

namespace PocketPlug;

/// <summary>
/// Development helpers, active only when <c>UserData/PocketPlug.dev</c> exists. Commands are read from
/// <c>UserData/PocketPlug.cmd</c> (one per line) so the game can be tested without driving mouse and keyboard.
/// </summary>
internal static class DevTools
{
    private static readonly string FlagPath = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug.dev");
    private static readonly string CommandPath = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug.cmd");
    private static readonly string DumpPath = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug-dump.txt");

    private static bool _enabled;
    private static float _nextPoll;
    private static DateTime _lastCommandWrite;

    public static void Init()
    {
        _enabled = File.Exists(FlagPath);
        if (_enabled)
            Core.Log.Warning("Dev mode on (PocketPlug.dev found).");
    }

    public static void Poll(float now)
    {
        if (!_enabled || now < _nextPoll || !File.Exists(CommandPath))
            return;
        _nextPoll = now + 0.5f;

        // Wine can lag on deletes, so never run the same file twice.
        var write = File.GetLastWriteTimeUtc(CommandPath);
        if (write == _lastCommandWrite)
            return;
        _lastCommandWrite = write;

        string[] lines = File.ReadAllLines(CommandPath);
        File.Delete(CommandPath);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                Run(line.Trim());
            }
            catch (Exception e)
            {
                Core.Log.Error($"dev '{line}': {e}");
            }
        }
    }

    private static void Run(string line)
    {
        string[] parts = line.Split(' ', 2);
        string arg = parts.Length > 1 ? parts[1] : "";
        Core.Log.Msg($"dev> {line}");

        switch (parts[0])
        {
            case "load":
                foreach (var info in LoadManager.SaveGames)
                {
                    if (info == null)
                        continue;
                    Singleton<LoadManager>.Instance.StartGame(info, false, true);
                    return;
                }
                Core.Log.Warning("No save found.");
                break;
            case "phone":
                var menu = Singleton<Il2CppScheduleOne.UI.GameplayMenu>.Instance;
                if (arg == "close")
                {
                    menu.Close();
                }
                else
                {
                    menu.Open();
                    menu.SetScreen(Il2CppScheduleOne.UI.GameplayMenu.EGameplayScreen.Phone);
                }
                break;
            case "app":
                Apps.Open(arg);
                break;
            case "cash":
                NetworkSingleton<MoneyManager>.Instance.ChangeCashBalance(float.Parse(arg), true, false);
                break;
            case "click":
                // click <app> <path under the app container>
                var a = arg.Split(' ', 2);
                var target = Apps.Find(a[0])?.Container.transform.Find(a[1]);
                if (target == null)
                    Core.Log.Warning($"click: {arg} not found");
                else
                    target.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                break;
            case "transfer":
                // Runs the dealer transfer for the first recruited dealer, as if the message was sent.
                foreach (var d in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
                {
                    if (d == null || !d.IsRecruited)
                        continue;
                    if (arg != "")
                        d.SetCash(float.Parse(arg));
                    Features.DealerTransfers.Transfer(d, d.MSGConversation);
                    return;
                }
                Core.Log.Warning("transfer: no recruited dealer");
                break;
            case "deals":
                var cs = Il2CppScheduleOne.Quests.Contract.Contracts;
                Core.Log.Msg($"deals: {cs.Count}");
                for (int i = 0; i < cs.Count; i++)
                {
                    var c = cs[i];
                    var npc = c.Customer != null ? c.Customer.GetComponent<Il2CppScheduleOne.NPCs.NPC>() : null;
                    int withElement = 0;
                    for (int j = 0; j < c.Entries.Count; j++)
                        if (c.Entries[j].compassElement != null) withElement++;
                    Core.Log.Msg($"  {c.Title} | {c.State} | tracked={c.IsTracked} | dealer={(c.Dealer != null)} | customer={npc?.FullName} | compass={withElement} | expires={(c.Expires ? c.GetMinsUntilExpiry() + "m" : "no")}");
                }
                break;
            case "notify":
                Singleton<Il2CppScheduleOne.UI.NotificationsManager>.Instance.SendNotification("PocketPlug test", arg == "" ? "Notifications work" : arg,
                    Il2CppScheduleOne.Registry.GetItem("mixingstation")?.Icon, 5f, true);
                break;
            case "money":
                Core.Log.Msg($"money: cash={Features.Bank.Cash} bank={Features.Bank.Online} week={Il2CppScheduleOne.Money.ATM.WeeklyDepositSum}");
                break;
            case "shot":
                // Unity writes its own frame, so this works even when the window is on another workspace.
                ScreenCapture.CaptureScreenshot(Path.Combine(MelonEnvironment.UserDataDirectory, $"PocketPlug-{(arg == "" ? "shot" : arg)}.png"));
                break;
            case "dump":
                Dump(arg);
                break;
            default:
                Core.Log.Warning($"Unknown dev command: {parts[0]}");
                break;
        }
    }

    /// <summary>Writes the transform tree under a GameObject (found by name) with components and rect sizes.</summary>
    private static void Dump(string name)
    {
        var root = GameObject.Find(name);
        if (root == null)
        {
            Core.Log.Warning($"dump: '{name}' not found");
            return;
        }

        var sb = new StringBuilder();
        Walk(root.transform, 0, sb);
        File.AppendAllText(DumpPath, $"==== {name}\n{sb}\n");
        Core.Log.Msg($"dump: wrote {name} to {DumpPath}");
    }

    private static void Walk(Transform t, int depth, StringBuilder sb)
    {
        sb.Append(' ', depth * 2).Append(t.name);
        if (!t.gameObject.activeSelf)
            sb.Append(" [off]");
        var rect = t.TryCast<RectTransform>();
        if (rect != null)
            sb.Append($" rect={rect.rect.width:F0}x{rect.rect.height:F0} pos={rect.anchoredPosition}");
        float rz = t.localEulerAngles.z;
        if (rz > 0.5f)
            sb.Append($" rot={rz:F0}");
        if (t.localScale != Vector3.one)
            sb.Append($" scale={t.localScale}");
        var img = t.GetComponent<UnityEngine.UI.Image>();
        if (img != null)
            sb.Append($" color={img.color} sprite={(img.sprite != null ? img.sprite.name : "-")}");
        var text = t.GetComponent<UnityEngine.UI.Text>();
        if (text != null)
            sb.Append($" font={(text.font != null ? text.font.name : "NULL")} size={text.fontSize} text='{(text.text.Length > 24 ? text.text.Substring(0, 24) : text.text)}'");
        var components = t.GetComponents<Component>();
        foreach (var c in components)
        {
            string type = c.GetIl2CppType().Name;
            if (type != "Transform" && type != "RectTransform")
                sb.Append(" <").Append(type).Append('>');
        }
        sb.Append('\n');
        if (depth > 12)
            return;
        for (int i = 0; i < t.childCount; i++)
            Walk(t.GetChild(i), depth + 1, sb);
    }
}
