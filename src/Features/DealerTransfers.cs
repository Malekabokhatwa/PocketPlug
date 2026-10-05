using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI;
using UnityEngine;

namespace PocketPlug.Features;

/// <summary>
/// Adds a "send my money to my bank account" message to every recruited dealer's chat. When sent, the dealer
/// transfers the cash they're holding straight to the bank account and you get a "Name sent you $X" notification.
/// </summary>
internal static class DealerTransfers
{
    public const string Request = "Send my money to my bank account.";

    private static readonly HashSet<IntPtr> Added = new();
    private static float _next;

    public static void Reset()
    {
        Added.Clear();
        _next = Time.unscaledTime + 3f;
    }

    public static void Update()
    {
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + 2f;

        var dealers = Dealer.AllPlayerDealers;
        for (int i = 0; i < dealers.Count; i++)
        {
            var dealer = dealers[i];
            if (dealer == null || !dealer.IsRecruited)
                continue;
            var convo = dealer.MSGConversation;
            if (convo == null || Added.Contains(convo.Pointer))
                continue;
            Add(dealer, convo);
            Added.Add(convo.Pointer);
        }
    }

    private static void Add(Dealer dealer, MSGConversation convo)
    {
        var sendable = convo.CreateSendableMessage(Request);
        sendable.ShouldShowCheck = DelegateSupport.ConvertDelegate<SendableMessage.BoolCheck>(
            new Func<SendableMessage, bool>(_ => Config.DealerTransfers.On));
        sendable.onSent = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => Transfer(dealer, convo)));
    }

    public static void Transfer(Dealer dealer, MSGConversation convo)
    {
        float amount = Mathf.Floor(dealer.Cash);
        string reply;
        if (amount <= 0f)
        {
            reply = "I don't have any cash for you right now.";
        }
        else
        {
            dealer.SetCash(dealer.Cash - amount);
            Bank.Receive(amount, dealer.FullName);
            reply = $"Sent {MoneyManager.FormatAmount(amount)} to your account.";
            Singleton<NotificationsManager>.Instance.SendNotification(
                $"{dealer.FirstName} sent you {MoneyManager.FormatAmount(amount)}", "Bank transfer", dealer.MugshotSprite, 5f, true);
        }
        convo.SendMessage(new Message(reply, Message.ESenderType.Other, true, -1), true, true);
    }
}
