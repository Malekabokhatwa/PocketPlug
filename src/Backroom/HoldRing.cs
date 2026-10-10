using Il2CppTMPro;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Backroom;

/// <summary>
/// The hold-to-open indicator: a thin white ring that fills around the crosshair while the key is held, and a
/// short line of text when the menu can't open right now.
/// </summary>
internal sealed class HoldRing
{
    private const float Size = 46f;
    private readonly GameObject _root;
    private readonly Image _fill, _track;
    private readonly CanvasGroup _group;
    private readonly TextMeshProUGUI _reason;
    private float _reasonUntil;

    public HoldRing(Transform parent)
    {
        _root = new GameObject("PocketPlug Backroom Ring");
        _root.transform.SetParent(parent, false);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 499;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _group = _root.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
        var rect = _root.GetComponent<RectTransform>();

        _track = K.Rect("Track", rect).gameObject.AddComponent<Image>();
        var track = _track;
        track.sprite = Util.Assets.Sprite("br_ring_track");
        track.raycastTarget = false;
        K.At(track.rectTransform, 0.5f, 0.5f, Size, Size);
        track.rectTransform.pivot = new Vector2(0.5f, 0.5f);

        _fill = K.Rect("Fill", rect).gameObject.AddComponent<Image>();
        _fill.sprite = Util.Assets.Sprite("br_ring");
        _fill.type = Image.Type.Filled;
        _fill.fillMethod = Image.FillMethod.Radial360;
        _fill.fillOrigin = (int)Image.Origin360.Top;
        _fill.fillClockwise = true;
        _fill.fillAmount = 0f;
        _fill.color = Theme.Select;
        _fill.raycastTarget = false;
        K.At(_fill.rectTransform, 0.5f, 0.5f, Size, Size);
        _fill.rectTransform.pivot = new Vector2(0.5f, 0.5f);

        _reason = K.Label("Reason", rect, "", Theme.Small, Theme.Text, Theme.SemiBold, TextAlignmentOptions.Center);
        K.At(_reason.rectTransform, 0.5f, 0.5f, 600, 28, 0, -50);
        _reason.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _reason.fontStyle = FontStyles.Normal;
        _reason.outlineWidth = 0.2f;
        _reason.outlineColor = new Color32(0, 0, 0, 200);

        _group.alpha = 0f;
    }

    /// <summary>Hold progress 0..1 (0 hides the ring).</summary>
    public void Set(float progress)
    {
        if (_root == null)
            return;
        _fill.fillAmount = Mathf.Clamp01(progress);
        bool reason = Time.unscaledTime < _reasonUntil;
        _reason.gameObject.SetActive(reason);
        _track.gameObject.SetActive(progress > 0f);
        _fill.gameObject.SetActive(progress > 0f);
        _group.alpha = progress > 0f || reason ? 1f : 0f;
    }

    public void Refuse(string text)
    {
        _reason.text = text;
        _reasonUntil = Time.unscaledTime + 1.6f;
        Set(0f);
    }
}
