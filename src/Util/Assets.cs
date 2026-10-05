using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PocketPlug.Util;

/// <summary>Loads the PNGs embedded in the DLL (assets/icons) as sprites, once.</summary>
internal static class Assets
{
    private static readonly Dictionary<string, Sprite> Cache = new();

    public static Sprite Sprite(string name)
    {
        if (Cache.TryGetValue(name, out var cached) && cached != null)
            return cached;

        using var stream = typeof(Assets).Assembly.GetManifestResourceStream($"PocketPlug.Icons.{name}.png");
        if (stream == null)
        {
            Core.Log.Warning($"Missing embedded icon: {name}");
            return null;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = $"PocketPlug_{name}" };
        ImageConversion.LoadImage(tex, ms.ToArray());
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

        var sprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = $"PocketPlug_{name}";
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Cache[name] = sprite;
        return sprite;
    }
}
