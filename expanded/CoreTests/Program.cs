using System;
using System.IO;

static class Program
{
    static int passed;

    static void Main()
    {
        TestCatalog();
        TestPetGesture();
        TestRestGesture();
        TestTeaseGesture();
        TestMouseClickAwakensImmediately();
        TestCompanionDefinitions();
        TestExpandedGallery();
        TestCatRigDefinition();
        TestCatRigJointCaps();
        TestDirectTeaseTrigger();
        TestTreeInteraction();
        TestRiverInteraction();
        TestRainbowInteraction();
        TestDandelionInteraction();
        TestEnvironmentBoundsAndBadInput();
        TestEnvironmentReset();
        TestTwoHandsShareEnvironment();
        Console.WriteLine($"PASS {passed}/17");
    }

    static void Check(bool condition,string name)
    {
        if(!condition)throw new Exception("FAIL: "+name);
        passed++;
        Console.WriteLine("PASS: "+name);
    }

    static FieldHand Hand(float x,float y,float vx=0f,float vy=0f)
        =>new FieldHand{Active=true,Open=true,X=x,Y=y,VX=vx,VY=vy};

    static void Step(SpringEnvironmentWorld world,int frames,FieldHand first,FieldHand second=default)
    {
        for(int i=0;i<frames;i++)world.Step(.02f,first,second);
    }

    static void TestCatalog()
    {
        Check(AnimalCatalog.Cat().SpecialResource==null
            &&AnimalCatalog.Cat().MoveResource=="Magic/CatWalk"
            &&!AnimalCatalog.Cat().ImportedFacesRight
            &&AnimalCatalog.Crocodile().MotionStyle==AnimalMotionStyle.Walk,
            "cat uses the imported coloring while the catalog remains extensible");
    }

    static void TestPetGesture()
    {
        var gestures=new InteractionGestures();
        for(int i=0;i<10;i++)gestures.Step(.02f,Hand(.5f,.5f,.12f),default);
        Check(gestures[0]==HandAction.Pet,"slow palm motion becomes Pet");
    }

    static void TestRestGesture()
    {
        var gestures=new InteractionGestures();
        for(int i=0;i<30;i++)gestures.Step(.02f,Hand(.5f,.5f),default);
        Check(gestures[0]==HandAction.Rest,"still palm becomes Rest");
    }

    static void TestTeaseGesture()
    {
        var gestures=new InteractionGestures();
        foreach(float vx in new[]{.7f,.7f,-.7f,-.7f,.7f,.7f})
            gestures.Step(.06f,Hand(.5f,.5f,vx),default);
        Check(gestures[0]==HandAction.Tease,"left-right wiggle becomes Tease");
    }

    static void TestMouseClickAwakensImmediately()
    {
        var world=new MagicLibrary();
        world.Step(.016f,new ShadowContact(),Hand(.23f,.43f),default,false,true);
        Check(world.Pictures[0].Awake,"mouse click awakens an animal immediately");
    }

    static void TestCompanionDefinitions()
    {
        var companions=new AnimalCompanions();
        Check(companions.Animals.Length==2
            &&companions.FindByHome(0).Definition.Id=="owl"
            &&companions.FindByHome(2).Definition.Id=="fox",
            "original gallery still maps owl and fox to their home positions");
    }

    static void TestExpandedGallery()
    {
        var companions=new AnimalCompanions(AnimalCatalog.ExpandedGallery());
        Check(companions.Animals.Length==3
            &&companions.FindByHome(1).Definition.Id=="cat",
            "expanded gallery places the cat at the center home position");
    }

    static void TestCatRigDefinition()
    {
        var rig=ColoringRigCatalog.CatV2();
        Check(rig.Id==AnimalCatalog.Cat().ColoringRigId
            &&rig.SourceFacesRight&&rig.TemplateWidth==1160&&rig.TemplateHeight==700
            &&rig.Parts.Length==8&&rig.Parts[7].Id=="head"&&rig.Parts[7].ParentId=="body",
            "cat_v2 defines eight independently animatable coloring parts");
    }

    static void TestDirectTeaseTrigger()
    {
        var gestures=new InteractionGestures();
        gestures.TriggerTease(0);
        Check(gestures[0]==HandAction.Tease,
            "mouse preview can trigger the distinct cat teaser action directly");
    }

    static void TestCatRigJointCaps()
    {
        byte[] mask=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"TestData","Body.bytes"));
        bool covered=mask.Length==1160*700;
        foreach((float x,float y) in new[]{(.39f,.65f),(.51f,.65f),(.66f,.58f),(.76f,.58f),(.42f,.46f),(.70f,.42f)})
        {
            int px=(int)MathF.Round(x*1160);
            int py=699-(int)MathF.Round(y*700);
            covered&=mask[py*1160+px]==255;
        }
        Check(covered,"cat_v2 keeps body-colored caps underneath every moving joint");
    }

    static void TestTreeInteraction()
    {
        var world=new SpringEnvironmentWorld();
        Step(world,25,Hand(SpringEnvironmentWorld.TreeCenterX,SpringEnvironmentWorld.TreeCenterY,.7f));
        Check(world.TreeEnergy>0f&&world.Leaves.Count>0,"touching the tree releases falling leaves");
    }

    static void TestRiverInteraction()
    {
        var world=new SpringEnvironmentWorld();
        Step(world,25,Hand(.68f,SpringEnvironmentWorld.RiverCenterYAt(.68f),.8f,-.2f));
        Check(world.RiverEnergy>0f&&world.Ripples.Count>0&&world.Splashes.Count>0,
            "moving through the river creates flow, ripples, and splashes");
    }

    static void TestRainbowInteraction()
    {
        var world=new SpringEnvironmentWorld();
        Step(world,65,Hand(.72f,.20f));
        Check(world.RainbowReveal>.7f,"reaching into the sky reveals a rainbow");
    }

    static void TestDandelionInteraction()
    {
        var world=new SpringEnvironmentWorld();
        var head=world.Dandelions[0];
        Step(world,30,Hand(head.X,head.Y,.65f,-.15f));
        Check(world.Fluff.Count>0&&head.SeedsRemaining<head.Capacity,
            "brushing a dandelion releases its fluff");
    }

    static void TestEnvironmentBoundsAndBadInput()
    {
        var world=new SpringEnvironmentWorld();
        for(int i=0;i<1600;i++)world.Step(.05f,Hand(.20f,.35f,2f),
            Hand(.70f,SpringEnvironmentWorld.RiverCenterYAt(.70f),2f,-1f));
        float time=world.Time;
        world.Step(float.NaN,Hand(.5f,.5f),default);
        var invalid=Hand(float.NaN,.2f);world.Step(.02f,invalid,default);
        Check(world.Leaves.Count<=SpringEnvironmentWorld.MaxLeaves
            &&world.Ripples.Count<=SpringEnvironmentWorld.MaxRipples
            &&world.Splashes.Count<=SpringEnvironmentWorld.MaxSplashes
            &&world.Fluff.Count<=SpringEnvironmentWorld.MaxFluff
            &&world.Time>=time,"environment remains bounded and rejects invalid input");
    }

    static void TestEnvironmentReset()
    {
        var world=new SpringEnvironmentWorld();
        Step(world,30,Hand(.20f,.35f,.8f),
            Hand(.70f,SpringEnvironmentWorld.RiverCenterYAt(.70f),.8f));
        world.Reset();
        Check(world.Time==0f&&world.TreeEnergy==0f&&world.RiverEnergy==0f
            &&world.RainbowReveal==0f&&world.Leaves.Count==0&&world.Ripples.Count==0
            &&world.Splashes.Count==0&&world.Fluff.Count==0,
            "reset restores a clean environment");
    }

    static void TestTwoHandsShareEnvironment()
    {
        var world=new SpringEnvironmentWorld();
        Step(world,35,Hand(.20f,.35f,.7f),
            Hand(.70f,SpringEnvironmentWorld.RiverCenterYAt(.70f),.8f,-.2f));
        Check(world.Leaves.Count>0&&world.Ripples.Count>0,
            "two hands can affect separate parts of the environment simultaneously");
    }
}
