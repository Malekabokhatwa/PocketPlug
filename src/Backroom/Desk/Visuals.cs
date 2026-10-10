using UnityEngine;

namespace PocketPlug.Backroom.Desk;

/// <summary>
/// Copies only the look of a game object (meshes and materials, with their local layout), never its scripts,
/// network or save components. The Backroom laptop is built from these copies, so nothing it adds can reach
/// the game's save.
/// </summary>
internal static class Visuals
{
    /// <summary>Copies every MeshRenderer under <paramref name="source"/> into a new object under <paramref name="parent"/>.</summary>
    public static GameObject Clone(Transform source, Transform parent, string name)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        CopyChildren(source, root.transform, source);
        return root;
    }

    private static void CopyChildren(Transform from, Transform to, Transform sourceRoot)
    {
        var filter = from.GetComponent<MeshFilter>();
        var renderer = from.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null && filter.sharedMesh != null && renderer.enabled)
        {
            var part = new GameObject(from.name);
            part.transform.SetParent(to, false);
            // Place the part where it sits relative to the source root.
            part.transform.localPosition = sourceRoot.InverseTransformPoint(from.position);
            part.transform.localRotation = Quaternion.Inverse(sourceRoot.rotation) * from.rotation;
            part.transform.localScale = Divide(from.lossyScale, sourceRoot.lossyScale);
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var r = part.AddComponent<MeshRenderer>();
            r.sharedMaterials = renderer.sharedMaterials;
            r.shadowCastingMode = renderer.shadowCastingMode;
            r.receiveShadows = renderer.receiveShadows;
        }

        // LOD groups: copy only the most detailed level.
        for (int i = 0; i < from.childCount; i++)
        {
            var child = from.GetChild(i);
            if (!child.gameObject.activeSelf || IsLowerLod(child.name))
                continue;
            CopyChildren(child, to, sourceRoot);
        }
    }

    private static bool IsLowerLod(string name) =>
        name.EndsWith("_LOD1") || name.EndsWith("_LOD2") || name.EndsWith("_LOD3");

    private static Vector3 Divide(Vector3 a, Vector3 b) =>
        new(b.x != 0 ? a.x / b.x : a.x, b.y != 0 ? a.y / b.y : a.y, b.z != 0 ? a.z / b.z : a.z);

    /// <summary>World-space bounds of every renderer under <paramref name="root"/>.</summary>
    public static Bounds WorldBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    /// <summary>The game's laptop, found once: the prop prefab first, the copy placed in the map otherwise.</summary>
    public static Transform FindLaptop()
    {
        Transform fallback = null;
        foreach (var mf in Resources.FindObjectsOfTypeAll<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.sharedMesh.name != "Laptop_LOD0")
                continue;
            // The LOD0 mesh sits under the laptop's root (with its LOD1/LOD2 siblings), when it has one.
            var t = mf.transform.parent != null && mf.transform.parent.name.StartsWith("Laptop") ? mf.transform.parent : mf.transform;
            if (!mf.gameObject.scene.IsValid())
                return t;   // a prefab asset: never moves, never destroyed with the scene
            fallback ??= t;
        }
        return fallback;
    }
}
