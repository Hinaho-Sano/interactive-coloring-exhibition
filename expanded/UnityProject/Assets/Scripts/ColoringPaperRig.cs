using System;
using System.Collections.Generic;
using UnityEngine;

// Builds one transparent texture per rig part from the child's single coloring.
// Every part keeps the original UV coordinates, so the child's marks never
// change when bones move.
public sealed class ColoringPaperRig
{
    readonly Dictionary<string, Texture2D> parts = new Dictionary<string, Texture2D>();

    public ColoringRigDefinition Definition { get; }

    ColoringPaperRig(ColoringRigDefinition definition)
    {
        Definition = definition;
    }

    public static ColoringPaperRig Create(Texture2D source, ColoringRigDefinition definition)
    {
        if (source == null || definition == null) return null;
        if (source.width != definition.TemplateWidth || source.height != definition.TemplateHeight)
            return null;

        Color32[] sourcePixels;
        try { sourcePixels = source.GetPixels32(); }
        catch (UnityException) { return null; }

        var rig = new ColoringPaperRig(definition);
        int expected = sourcePixels.Length;
        foreach (var part in definition.Parts)
        {
            TextAsset maskAsset = Resources.Load<TextAsset>(part.MaskResource);
            if (maskAsset == null || maskAsset.bytes.Length != expected)
            {
                rig.Destroy();
                return null;
            }

            byte[] mask = maskAsset.bytes;
            var pixels = new Color32[expected];
            for (int i = 0; i < expected; i++)
            {
                Color32 color = sourcePixels[i];
                color.a = (byte)(color.a * mask[i] / 255);
                pixels[i] = color;
            }

            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false)
            {
                name = source.name + "_" + part.Id,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            rig.parts.Add(part.Id, texture);
        }
        return rig;
    }

    public Texture2D Part(string id)
    {
        Texture2D texture;
        return parts.TryGetValue(id, out texture) ? texture : null;
    }

    public void Destroy()
    {
        foreach (Texture2D texture in parts.Values)
            if (texture != null) UnityEngine.Object.Destroy(texture);
        parts.Clear();
    }
}
