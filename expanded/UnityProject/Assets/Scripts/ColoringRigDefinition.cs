using System;

// A template-space pivot is expressed in normalized image coordinates.
// X grows to the right and Y grows down, matching the rectified PNG.
[Serializable]
public sealed class ColoringRigPartDefinition
{
    public string Id;
    public string ParentId;
    public string MaskResource;
    public float PivotX;
    public float PivotY;
    public int DrawOrder;
}

[Serializable]
public sealed class ColoringRigDefinition
{
    public string Id;
    public bool SourceFacesRight;
    public int TemplateWidth;
    public int TemplateHeight;
    public ColoringRigPartDefinition[] Parts;
}

public static class ColoringRigCatalog
{
    // The pivots are tied to template/cat_v2/reference.png (1160 x 700).
    // The masks will use the same resource names, so their artwork can be
    // refined without changing animation code.
    public static ColoringRigDefinition CatV2() => new ColoringRigDefinition
    {
        Id = "cat_v2",
        SourceFacesRight = true,
        TemplateWidth = 1160,
        TemplateHeight = 700,
        Parts = new[]
        {
            Part("hind_leg_far",  "body", "ColoringRigs/CatV2/HindLegFar",  .51f, .65f, 0),
            Part("front_leg_far", "body", "ColoringRigs/CatV2/FrontLegFar", .76f, .58f, 1),
            Part("tail_root",     "body", "ColoringRigs/CatV2/TailRoot",    .42f, .46f, 2),
            Part("tail_tip", "tail_root", "ColoringRigs/CatV2/TailTip",    .29f, .37f, 3),
            Part("body",             null, "ColoringRigs/CatV2/Body",        .57f, .52f, 4),
            Part("hind_leg_near", "body", "ColoringRigs/CatV2/HindLegNear", .39f, .65f, 5),
            Part("front_leg_near","body", "ColoringRigs/CatV2/FrontLegNear",.66f, .58f, 6),
            Part("head",          "body", "ColoringRigs/CatV2/Head",        .70f, .42f, 7)
        }
    };

    static ColoringRigPartDefinition Part(
        string id,
        string parentId,
        string maskResource,
        float pivotX,
        float pivotY,
        int drawOrder)
        => new ColoringRigPartDefinition
        {
            Id = id,
            ParentId = parentId,
            MaskResource = maskResource,
            PivotX = pivotX,
            PivotY = pivotY,
            DrawOrder = drawOrder
        };
}
