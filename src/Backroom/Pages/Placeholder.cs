using PocketPlug.Backroom.Kit;
using Il2CppTMPro;
using UnityEngine;

namespace PocketPlug.Backroom.Pages;

/// <summary>A page that isn't built yet.</summary>
internal sealed class Placeholder : Page
{
    private readonly string _id, _title, _icon;

    public Placeholder(string id, string title, string icon)
    {
        _id = id;
        _title = title;
        _icon = icon;
    }

    public override string Id => _id;
    public override string Title => _title;
    public override string Icon => _icon;

    protected internal override void Build(RectTransform root)
    {
        var text = K.Label("Soon", root, $"{_title} is being built.", Theme.Body, Theme.Text3, Theme.Medium, TextAlignmentOptions.Center);
        K.Fill(text.rectTransform);
    }
}
