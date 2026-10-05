using Il2CppScheduleOne.NPCs;
using UnityEngine;

namespace PocketPlug.Util;

internal static class Npcs
{
    /// <summary>
    /// The portrait the Contacts app shows. <c>NPC.MugshotSprite</c> throws for NPCs without appearance data, so
    /// this returns null instead.
    /// </summary>
    public static Sprite Mugshot(NPC npc)
    {
        if (npc == null)
            return null;
        try
        {
            return npc.MugshotSprite;
        }
        catch
        {
            return null;
        }
    }
}
