using System;

public enum AnimalMotionStyle
{
    Fly,
    Hop,
    Walk,
    Swim
}

public enum AnimalAction
{
    Follow,
    Petted,
    Teased,
    Resting,
    Returning
}

// One data record describes one species. Adding an animal no longer requires
// another branch inside the movement or rendering loops.
[Serializable]
public sealed class AnimalDefinition
{
    public string Id;
    public string DisplayName;
    public int HomePainting;
    public float MoveSpeed;
    public float ScreenSize;
    public float FramesPerSecond;
    public float BobStrength;
    public AnimalMotionStyle MotionStyle;
    public string IdleResource;
    public string MoveResource;
    public string PetResource;
    public string SpecialResource;
    public string ColoringRigId;
    public bool ImportedFacesRight = true;
    public int IdleFrames = 1;
    public int MoveFrames = 4;
    public int PetFrames = 4;
    public int SpecialFrames = 4;
}

public static class AnimalCatalog
{
    // These are the two animals present in the original public exhibit.
    public static AnimalDefinition[] GalleryDefaults()
        => new[] { Owl(), Fox() };

    public static AnimalDefinition[] ExpandedGallery()
        => new[] { Owl(), Cat(), Fox() };

    public static AnimalDefinition Owl() => new AnimalDefinition
    {
        Id = "owl",
        DisplayName = "フクロウ",
        HomePainting = 0,
        MoveSpeed = .52f,
        ScreenSize = .13f,
        FramesPerSecond = 8f,
        BobStrength = .006f,
        MotionStyle = AnimalMotionStyle.Fly,
        IdleResource = "Magic/Owl",
        MoveResource = "Magic/OwlFlight"
    };

    public static AnimalDefinition Fox() => new AnimalDefinition
    {
        Id = "fox",
        DisplayName = "キツネ",
        HomePainting = 2,
        MoveSpeed = .44f,
        ScreenSize = .15f,
        FramesPerSecond = 7.2f,
        BobStrength = .025f,
        MotionStyle = AnimalMotionStyle.Hop,
        IdleResource = "Magic/Fox",
        MoveResource = "Magic/FoxHop"
    };

    // Ready-to-use profiles for the coloring pipeline. Missing resources simply
    // fall back to the idle image, so art can be added one action at a time.
    public static AnimalDefinition Cat() => new AnimalDefinition
    {
        Id = "cat",
        DisplayName = "ネコ",
        HomePainting = 1,
        MoveSpeed = .48f,
        ScreenSize = .15f,
        FramesPerSecond = 7f,
        BobStrength = .018f,
        MotionStyle = AnimalMotionStyle.Hop,
        // Idle still uses the child's processed coloring.  Walking uses a
        // four-pose storybook strip; a newly generated colored strip can
        // replace this resource without changing the controller.
        // The current photographed coloring template faces left in its source image.
        ImportedFacesRight = false,
        // New photographs made with the printable cat_v2 sheet use this rig.
        // ImportedFacesRight stays false until the current legacy sample is replaced.
        ColoringRigId = "cat_v2",
        IdleResource = null,
        MoveResource = "Magic/CatWalk",
        PetResource = null,
        SpecialResource = null
    };

    public static AnimalDefinition Dog() => new AnimalDefinition
    {
        Id = "dog",
        DisplayName = "イヌ",
        HomePainting = 1,
        MoveSpeed = .5f,
        ScreenSize = .16f,
        FramesPerSecond = 7f,
        BobStrength = .022f,
        MotionStyle = AnimalMotionStyle.Hop,
        IdleResource = "Magic/Dog",
        MoveResource = "Magic/DogRun",
        PetResource = "Magic/DogPet",
        SpecialResource = "Magic/DogFetch"
    };

    public static AnimalDefinition Bird() => new AnimalDefinition
    {
        Id = "bird",
        DisplayName = "トリ",
        HomePainting = 2,
        MoveSpeed = .56f,
        ScreenSize = .12f,
        FramesPerSecond = 9f,
        BobStrength = .008f,
        MotionStyle = AnimalMotionStyle.Fly,
        IdleResource = "Magic/Bird",
        MoveResource = "Magic/BirdFlight",
        PetResource = "Magic/BirdPerch",
        SpecialResource = "Magic/BirdFlutter"
    };

    public static AnimalDefinition Crocodile() => new AnimalDefinition
    {
        Id = "crocodile",
        DisplayName = "ワニ",
        HomePainting = 0,
        MoveSpeed = .34f,
        ScreenSize = .18f,
        FramesPerSecond = 5f,
        BobStrength = .008f,
        MotionStyle = AnimalMotionStyle.Walk,
        IdleResource = "Magic/Crocodile",
        MoveResource = "Magic/CrocodileWalk",
        PetResource = "Magic/CrocodileRest",
        SpecialResource = "Magic/CrocodileSnap"
    };
}
