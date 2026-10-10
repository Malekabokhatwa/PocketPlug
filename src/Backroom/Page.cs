using UnityEngine;

namespace PocketPlug.Backroom;

/// <summary>
/// One entry in the rail (Items, Money, ...). A page builds its UI once, into the area right of the rail, the
/// first time it's shown, and gets OnShow, Tick and OnHide while it's the current page.
/// </summary>
internal abstract class Page
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    /// <summary>Lucide glyph name (assets/icons/br_*.png).</summary>
    public abstract string Icon { get; }

    public RectTransform Root { get; internal set; }
    public bool Built { get; internal set; }

    /// <summary>Breadcrumb text shown above the page, e.g. "ITEMS › PRODUCTS".</summary>
    public virtual string Breadcrumb => Title.ToUpperInvariant();

    /// <summary>Position text shown right of the breadcrumb, e.g. "12 / 48"; empty hides it.</summary>
    public virtual string Counter => "";

    protected internal abstract void Build(RectTransform root);
    protected internal virtual void OnShow() { }
    protected internal virtual void OnHide() { }

    /// <summary>Every frame while the page is visible and the menu is open.</summary>
    protected internal virtual void Tick() { }

    /// <summary>Keyboard navigation inside the page. Return true if the key was used.</summary>
    protected internal virtual bool OnNavigate(Nav key) => false;
}

internal enum Nav
{
    Up,
    Down,
    Left,
    Right,
    Submit,
    Back,
    NextSection,
    PreviousSection,
}
