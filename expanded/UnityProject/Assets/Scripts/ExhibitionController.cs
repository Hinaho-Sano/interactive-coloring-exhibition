using System;
using System.Collections;
using System.IO;
using UnityEngine;

public sealed class ExhibitionController : MonoBehaviour
{
    public bool mousePractice=true,showSilhouette=true;
    public float controlSpan=.65f,soundLevel=.16f;
    readonly ShadowContact shadow=new ShadowContact();
    readonly MagicLibrary world=new MagicLibrary();
    readonly AnimalCompanions companions=new AnimalCompanions(AnimalCatalog.ExpandedGallery());
    readonly InteractionGestures gestures=new InteractionGestures();
    readonly FieldHand[] hands=new FieldHand[2];
    readonly Vector2[] previous=new Vector2[2];
    readonly double[] seen={-100,-100};
    readonly float[] frameAt={-100,-100};
    sealed class AnimationArt
    {
        public Texture2D Sheet;
        public Texture2D[] Frames=Array.Empty<Texture2D>();
        public string RigId;
        public ColoringPaperRig PaperRig;
        public bool UsesSeparateFrames=>Frames!=null&&Frames.Length>0;
    }
    sealed class AnimalArt { public AnimationArt Idle,Move,Pet,Special,Imported; }
    AnimalArt[] animalArt;
    Texture2D background,silhouette,glow;
    readonly Color32[] shadowPixels=new Color32[320*180];
    Camera view; Font font; AudioSource audioSource; AudioClip chime;
    string status="Kinect starting...";
    bool settings,mouseDown,capturing; float captureAt,lastMask=-100,mouseTeaseUntil=-100;
    Vector2 mouse; Rect viewport;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if(FindAnyObjectByType<ExhibitionController>()!=null)return;
        var go=new GameObject("The waking library");go.AddComponent<ExhibitionController>();go.AddComponent<ExhibitionKinectInput>();
    }
    void Awake()
    {
        Application.runInBackground=true;Application.targetFrameRate=60;
        // Desktop prototyping should work immediately. Turn this off from F1
        // when the Kinect is connected for the exhibition.
        mousePractice=true;
        TryStartAutomatedCapture();
        view=Camera.main;if(view==null){view=new GameObject("Exhibition Camera").AddComponent<Camera>();view.tag="MainCamera";}
        view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=Color.black;
        background=Resources.Load<Texture2D>("Magic/PlateauDay")??Resources.Load<Texture2D>("Magic/LibraryDay")??Resources.Load<Texture2D>("Magic/Library");
        EnsureAnimalArt();
        silhouette=new Texture2D(320,180,TextureFormat.RGBA32,false);silhouette.wrapMode=TextureWrapMode.Clamp;
        glow=new Texture2D(64,64,TextureFormat.RGBA32,false);
        var pixels=new Color[4096];for(int y=0;y<64;y++)for(int x=0;x<64;x++){float d=new Vector2((x-31.5f)/31.5f,(y-31.5f)/31.5f).magnitude;pixels[y*64+x]=new Color(1,1,1,Mathf.Pow(Mathf.Max(0,1-d),2));}glow.SetPixels(pixels);glow.Apply();
        font=Font.CreateDynamicFontFromOSFont(new[]{"Yu Gothic","Meiryo","Arial"},24);
        if(FindAnyObjectByType<AudioListener>()==null)view.gameObject.AddComponent<AudioListener>();
        audioSource=gameObject.AddComponent<AudioSource>();audioSource.spatialBlend=0;
        const int rate=22050;var samples=new float[rate*2];
        for(int i=0;i<samples.Length;i++){double t=(double)i/rate;samples[i]=(float)((Math.Sin(t*2*Math.PI*523.25)+Math.Sin(t*2*Math.PI*784)*.4+Math.Sin(t*2*Math.PI*1046.5)*.3)*Math.Exp(-t*3)*.25);}
        chime=AudioClip.Create("A companion awakens",samples.Length,1,rate,false);chime.SetData(samples,0);
        if(GetComponent<ColoringImportBridge>()==null)gameObject.AddComponent<ColoringImportBridge>();
    }
    void TryStartAutomatedCapture()
    {
        const string prefix="-exhibitionCapture=";
        foreach(string argument in Environment.GetCommandLineArgs())
        {
            if(!argument.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;
            StartCoroutine(CaptureAndQuit(Path.GetFullPath(argument.Substring(prefix.Length).Trim('"'))));
            break;
        }
    }
    static IEnumerator CaptureAndQuit(string path)
    {
        yield return new WaitForSecondsRealtime(2f);
        string directory=Path.GetDirectoryName(path);if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(path);
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit(0);
    }
    static AnimationArt LoadAnimation(string path)
    {
        if(string.IsNullOrEmpty(path))return null;
        var sheet=Resources.Load<Texture2D>(path);
        if(sheet!=null)return new AnimationArt{Sheet=sheet};
        var frames=Resources.LoadAll<Texture2D>(path);
        Array.Sort(frames,(a,b)=>string.CompareOrdinal(a.name,b.name));
        return frames.Length>0?new AnimationArt{Frames=frames}:null;
    }
    void EnsureAnimalArt()
    {
        if(animalArt!=null&&animalArt.Length==companions.Animals.Length)return;
        animalArt=new AnimalArt[companions.Animals.Length];
        for(int i=0;i<animalArt.Length;i++)
        {
            var definition=companions.Animals[i].Definition;
            animalArt[i]=new AnimalArt{
                Idle=LoadAnimation(definition.IdleResource),
                Move=LoadAnimation(definition.MoveResource),
                Pet=LoadAnimation(definition.PetResource),
                Special=LoadAnimation(definition.SpecialResource)
            };
        }
    }
    public void SetImportedColoring(Texture2D texture,string sourceName)
    {
        if(texture==null)return;
        EnsureAnimalArt();
        for(int i=0;i<companions.Animals.Length;i++)
        {
            if(companions.Animals[i].Definition.Id!="cat")continue;
            var previous=animalArt[i].Imported;
            bool isCatV2=sourceName.IndexOf("cat_v2",StringComparison.OrdinalIgnoreCase)>=0;
            ColoringPaperRig paperRig=isCatV2?ColoringPaperRig.Create(texture,ColoringRigCatalog.CatV2()):null;
            animalArt[i].Imported=new AnimationArt{
                Frames=new[]{texture},
                RigId=paperRig!=null?"cat_v2":null,
                PaperRig=paperRig
            };
            if(previous!=null&&previous.PaperRig!=null)previous.PaperRig.Destroy();
            if(previous!=null&&previous.Frames!=null)
                foreach(var oldTexture in previous.Frames)if(oldTexture!=null&&oldTexture!=texture)Destroy(oldTexture);
            status=(paperRig!=null?"8部位リグで塗り絵を読み込みました: ":"塗り絵を読み込みました: ")+sourceName;
            Debug.Log("Imported coloring: "+sourceName);
            return;
        }
        Destroy(texture);
    }
    public void SetStatus(string value){status=value;}
    public void SetFrame(byte[] pixels,int width,int height,GunInput left,GunInput right,byte[] mask)
    {
        if(mask!=null&&mask.Length==320*180*4)
        {
            shadow.SetMask(mask);lastMask=Time.unscaledTime;
            for(int y=0;y<180;y++)for(int x=0;x<320;x++)
            {
                int sx=319-x,src=(y*320+sx)*4;bool inside=mask[src+3]>100;
                shadowPixels[(179-y)*320+x]=new Color32(0,0,0,(byte)(inside?70:0));
            }
            silhouette.SetPixels32(shadowPixels);silhouette.Apply(false);
        }
        if(!mousePractice){Receive(0,left);Receive(1,right);}
    }
    void Receive(int i,GunInput hand)
    {
        if(!hand.Tracked){hands[i].Active=false;return;}
        var p=new Vector2(1-hand.AimX,hand.AimY);double dt=hand.Timestamp-seen[i];
        var v=dt>.01&&dt<.3?Vector2.ClampMagnitude((p-previous[i])/(float)dt,2):Vector2.zero;
        hands[i]=new FieldHand{Active=p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1,Open=hand.Pose,X=p.x,Y=p.y,VX=v.x,VY=v.y};
        previous[i]=p;seen[i]=hand.Timestamp;frameAt[i]=Time.unscaledTime;
    }
    void Update()
    {
        if(mousePractice&&(Input.GetKeyDown(KeyCode.T)||Input.GetMouseButtonDown(1)))
            mouseTeaseUntil=Time.unscaledTime+1.25f;
        float w=Screen.width,h=Screen.height,aspect=16f/9;
        viewport=w/h>aspect?new Rect((w-h*aspect)/2,0,h*aspect,h):new Rect(0,(h-w/aspect)/2,w,w/aspect);
        for(int i=0;i<2;i++)if(Time.unscaledTime-frameAt[i]>.3f)hands[i].Active=false;
        if(mousePractice)
        {
            var p=new Vector2((mouse.x-viewport.x)/viewport.width,(mouse.y-viewport.y)/viewport.height);
            var v=Vector2.ClampMagnitude((p-previous[0])/Mathf.Max(.01f,Time.unscaledDeltaTime),2);
            bool teaserPreview=Time.unscaledTime<mouseTeaseUntil;
            hands[0]=new FieldHand{Active=mouseDown||teaserPreview,Open=true,X=p.x,Y=p.y,VX=v.x,VY=v.y};previous[0]=p;hands[1].Active=false;
        }
        world.Step(Time.unscaledDeltaTime,shadow,hands[0],hands[1],Time.unscaledTime-lastMask<.35f,mousePractice);
        gestures.Step(Time.unscaledDeltaTime,hands[0],hands[1]);
        if(mousePractice&&Time.unscaledTime<mouseTeaseUntil)gestures.TriggerTease(0,.12f);
        companions.Step(Time.unscaledDeltaTime,world.Time,world,gestures,hands[0],hands[1]);
        if(world.NewAwakenings>0){audioSource.pitch=.9f+world.Awakened*.12f;audioSource.PlayOneShot(chime,soundLevel);}
        if(capturing&&Time.unscaledTime>=captureAt){GetComponent<ExhibitionKinectInput>()?.CaptureBackground();capturing=false;ResetWorld();}
    }
    Rect R(float x,float y,float w,float h)=>new Rect(viewport.x+x*viewport.width,viewport.y+y*viewport.height,w*viewport.width,h*viewport.height);
    void Fill(Rect r,Color c){GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=Color.white;}
    void Halo(float x,float y,float size,Color c){GUI.color=c;GUI.DrawTexture(R(x-size/2,y-size*.8889f,size,size*1.77778f),glow);GUI.color=Color.white;}
    GUIStyle Label(int size,Color c,TextAnchor alignment=TextAnchor.MiddleLeft)
    {var s=new GUIStyle(GUI.skin.label){font=font,fontSize=size,alignment=alignment,wordWrap=true};s.normal.textColor=c;return s;}
    void OnGUI()
    {
        mouse=Event.current.mousePosition;
        if(Event.current.type==EventType.MouseDown&&Event.current.button==0&&!settings)mouseDown=true;
        if(Event.current.type==EventType.MouseDown&&Event.current.button==1&&!settings){mouseDown=true;mouseTeaseUntil=Time.unscaledTime+1.25f;}
        if(Event.current.type==EventType.MouseUp)mouseDown=false;
        if(Event.current.type==EventType.KeyDown){if(Event.current.keyCode==KeyCode.F1){settings=!settings;mouseDown=false;Event.current.Use();}else if(Event.current.keyCode==KeyCode.R){ResetWorld();Event.current.Use();}else if(Event.current.keyCode==KeyCode.T&&!settings){mouseTeaseUntil=Time.unscaledTime+1.25f;Event.current.Use();}}
        if(background!=null)GUI.DrawTexture(viewport,background,ScaleMode.StretchToFill);else Fill(viewport,new Color(.025f,.045f,.08f));
        float t=world.Time;
        DrawHomeAnimals(t);
        if(showSilhouette&&Time.unscaledTime-lastMask<.35f)GUI.DrawTexture(viewport,silhouette);
        DrawAnimalAuras(t);
        DrawAnimals();
        DrawTeaseLures(t);
        for(int i=0;i<2;i++)if(hands[i].Active)
        {
            var h=hands[i];Color c=h.Open?new Color(1,.85f,.45f,.7f):new Color(.4f,.75f,.9f,.35f);
            Halo(h.X,h.Y,.065f,c);
        }
        float u=Screen.height/1000f;
        GUI.Label(R(.04f,.035f,.7f,.05f),"めざめる物語の台地",Label(Mathf.RoundToInt(25*u),new Color(1,.88f,.64f)));
        bool animalOutside=companions.AnyActive;
        bool teasing=false;foreach(var a in companions.Animals)if(a.Active&&a.Action==AnimalAction.Teased)teasing=true;
        string hint=teasing?"キラキラをねらって……ぴょん！":animalOutside?(mousePractice?"左ドラッグで撫でる／右クリック・Tで猫じゃらし／手を止める。":"ゆっくり撫でる／左右に振って誘う／手を止める。"):"動物に、手のひらで触れてみよう。";
        GUI.Label(R(.1f,.91f,.8f,.055f),hint,Label(Mathf.RoundToInt(22*u),new Color(1,.93f,.78f),TextAnchor.MiddleCenter));
        if(capturing)GUI.Label(R(.1f,.35f,.8f,.2f),"背景を記録します。カメラの外へ移動してください\n"+Mathf.CeilToInt(captureAt-Time.unscaledTime),Label(Mathf.RoundToInt(30*u),Color.white,TextAnchor.MiddleCenter));
        if(settings)Settings(Mathf.Max(.7f,u));
    }
    void Settings(float u)
    {
        Rect r=new Rect(Screen.width-455*u,25*u,425*u,570*u);Fill(r,new Color(.02f,.035f,.07f,.97f));float x=r.x+24*u,y=r.y+20*u,w=r.width-48*u;
        GUI.Label(new Rect(x,y,w,35*u),"展示設定 / F1で閉じる",Label((int)(20*u),Color.white));y+=45*u;
        GUI.Label(new Rect(x,y,w,65*u),status,Label((int)(14*u),new Color(.6f,.8f,.95f)));y+=70*u;
        mousePractice=GUI.Toggle(new Rect(x,y,w,30*u),mousePractice," マウス操作（動物をクリックしてドラッグ）");y+=35*u;
        showSilhouette=GUI.Toggle(new Rect(x,y,w,30*u),showSilhouette," 自分のシルエットを表示");y+=40*u;
        GUI.Label(new Rect(x,y,w,45*u),"開いた手のひらで動物に触れると目覚めます。\n反応に合わせて淡い光が集まります。",Label((int)(15*u),Color.white));y+=60*u;
        GUI.Label(new Rect(x,y,w,25*u),"音量",Label((int)(16*u),Color.white));y+=30*u;soundLevel=GUI.HorizontalSlider(new Rect(x,y,w,20*u),soundLevel,0,.35f);y+=35*u;
        var sensor=GetComponent<ExhibitionKinectInput>();if(sensor!=null){GUI.Label(new Rect(x,y,w,25*u),"シルエット距離 "+(sensor.silhouetteMaxDepth/1000f).ToString("F1")+"m",Label((int)(16*u),Color.white));y+=30*u;sensor.silhouetteMaxDepth=Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x,y,w,20*u),sensor.silhouetteMaxDepth,1500,5000));y+=35*u;if(GUI.Button(new Rect(x,y,w,35*u),"背景を登録する（5秒後）")){capturing=true;captureAt=Time.unscaledTime+5;settings=false;}y+=45*u;}
        if(GUI.Button(new Rect(x,y,w,35*u),"動物を元の場所に戻す（R）"))ResetWorld();
    }
    void ResetWorld(){world.Reset();companions.Reset();gestures.Reset();}
    void DrawAnimalAuras(float t)
    {
        foreach(var animal in companions.Animals)
        {
            if(!animal.Active)continue;
            float fade=Mathf.Clamp01(animal.Age/.35f);
            if(animal.Returning)fade*=1-Mathf.Clamp01((animal.ReturnAge-1.05f)/.55f);
            float response=animal.Action==AnimalAction.Teased?1.35f:animal.Action==AnimalAction.Petted?1.15f:1;
            Halo(animal.X,animal.Y,.2f,new Color(1,.82f,.42f,.09f*fade*response));
            int count=animal.Action==AnimalAction.Teased?11:8;
            for(int k=0;k<count;k++)
            {
                float phase=k*2.399f+animal.Home*.73f;
                float life=Mathf.Repeat(t*.22f+k*.137f+animal.Home*.19f,1);
                float pulse=Mathf.Sin(life*Mathf.PI);
                float radius=.035f+life*.065f;
                float x=animal.X+Mathf.Sin(t*.62f+phase)*radius;
                float y=animal.Y+Mathf.Cos(t*.48f+phase)*radius*.42f-life*.045f;
                Color c=k%3==0?new Color(.58f,.91f,1):new Color(1,.84f,.46f);
                Halo(x,y,(.006f+.011f*pulse)*response,new Color(c.r,c.g,c.b,.48f*pulse*fade));
            }
        }
    }
    void DrawTeaseLures(float t)
    {
        foreach(var animal in companions.Animals)
        {
            if(!animal.Active||animal.Action!=AnimalAction.Teased)continue;
            var hand=hands[animal.Hand];if(!hand.Active)continue;
            float x=hand.X+Mathf.Sin(t*15f)*.008f;
            float y=hand.Y+Mathf.Cos(t*12f)*.006f;
            Halo(x,y,.115f,new Color(1,.72f,.2f,.34f));
            Halo(x,y,.052f,new Color(1,.94f,.58f,.85f));
            for(int k=0;k<7;k++)
            {
                float a=t*(2.2f+k*.13f)+k*.897f;
                float radius=.025f+.012f*Mathf.Sin(t*3+k);
                Halo(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius*.55f,.012f,new Color(1,.9f,.48f,.82f));
            }
        }
    }
    void DrawHomeAnimals(float t)
    {
        for(int home=0;home<world.Pictures.Length;home++)
        {
            var p=world.Pictures[home];
            var animal=companions.FindByHome(home);if(animal==null)continue;
            int index=Array.IndexOf(companions.Animals,animal);if(index<0||animalArt==null||index>=animalArt.Length)continue;
            var definition=animal.Definition;var art=animalArt[index];
            AnimationArt clip=art.Imported??art.Idle??art.Move;if(clip==null)continue;
            Texture2D texture=clip.UsesSeparateFrames?clip.Frames[0]:clip.Sheet;if(texture==null)continue;
            int frameCount=clip.UsesSeparateFrames?1:Mathf.Max(1,definition.IdleFrames);
            float cell=1f/frameCount;
            float sourceWidth=texture.width/(float)frameCount;
            float alpha=1;
            if(animal.Active)alpha=animal.Returning
                ?Mathf.Clamp01((animal.ReturnAge-1.1f)/.5f)
                :Mathf.Clamp01(1-animal.Age/.35f);
            float breath=1+Mathf.Sin(t*2.2f+home)*.018f;
            float size=definition.ScreenSize*breath;
            float height=definition.ScreenSize*1.77778f*texture.height/sourceWidth/breath;
            float bob=Mathf.Sin(t*1.8f+home)*.004f;
            Rect rect=R(p.X-size/2,p.Y-height/2-bob,size,height);
            var matrix=GUI.matrix;
            GUIUtility.RotateAroundPivot(p.Sway*90+Mathf.Sin(t*1.35f+home)*1.2f,rect.center);
            Halo(p.X,p.Y,.22f,new Color(1,.72f,.28f,p.Glow*.55f));
            GUI.color=new Color(1,1,1,alpha);
            GUI.DrawTextureWithTexCoords(rect,texture,new Rect(0,0,cell,1));
            GUI.color=Color.white;
            if(p.Energy>.01f)for(int k=0;k<5;k++){float a=t*1.4f+k*1.256f+home;Halo(p.X+Mathf.Sin(a)*.08f,p.Y+Mathf.Cos(a)*.1f,.015f,new Color(1,.86f,.35f,p.Energy*.9f));}
            GUI.matrix=matrix;
        }
    }
    void DrawAnimals()
    {
        for(int hand=0;hand<2;hand++)
        {
            bool following=false;foreach(var a in companions.Animals)if(a.Active&&!a.Returning&&a.Hand==hand)following=true;
            if(!following)continue;
            var trail=companions.Trails[hand];
            for(int k=1;k<trail.Count;k++)
            {
                var p=trail[k-1];var q=trail[k];
                float alpha=Mathf.Clamp01(1-(world.Time-q.Time)/4)*.55f;
                Vector2 start=R(p.X,p.Y,0,0).position,end=R(q.X,q.Y,0,0).position;
                var delta=end-start;var matrix=GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,start);
                Fill(new Rect(start.x,start.y-1,delta.magnitude,2),new Color(1,.8f,.32f,alpha));GUI.matrix=matrix;
            }
        }
        for(int i=0;i<companions.Animals.Length;i++)
        {
            var a=companions.Animals[i];if(!a.Active)continue;
            var definition=a.Definition;var art=animalArt[i];
            int configuredFrames;AnimationArt clip=ClipForAction(art,definition,a.Action,out configuredFrames);if(clip==null)continue;
            // A generated walk strip should only play while the companion is
            // actually travelling.  When the hand stops, return immediately
            // to the child's original coloring instead of walking in place.
            if((a.Action==AnimalAction.Follow||a.Action==AnimalAction.Returning)
                &&a.Speed<.015f&&art.Imported!=null&&ReferenceEquals(clip,art.Move))
            {
                clip=art.Imported;configuredFrames=1;
            }
            int frameCount=clip.UsesSeparateFrames?clip.Frames.Length:Mathf.Max(1,configuredFrames);
            float emerge=Mathf.SmoothStep(0,1,Mathf.Clamp01(a.Age/.65f));
            float motion=Mathf.Clamp01(a.Speed*5);
            float cycle=Mathf.Repeat(a.ActionAge*definition.FramesPerSecond/Mathf.Max(1,frameCount),1);
            float bob=definition.MotionStyle==AnimalMotionStyle.Fly
                ?Mathf.Sin(a.Age*8)*definition.BobStrength
                :Mathf.Sin(cycle*Mathf.PI)*definition.BobStrength*motion;
            if(a.Action==AnimalAction.Petted)bob+=Mathf.Abs(Mathf.Sin(a.ActionAge*6))*.006f;
            if(a.Action==AnimalAction.Teased)bob+=Mathf.Abs(Mathf.Sin(a.ActionAge*9))*.018f;
            int frame=frameCount>1?Mathf.Min(frameCount-1,(int)(cycle*frameCount)):0;
            Texture2D texture=clip.UsesSeparateFrames?clip.Frames[frame]:clip.Sheet;if(texture==null)continue;
            bool livingColoring=art.Imported!=null&&ReferenceEquals(clip,art.Imported);
            float puppetPhase=a.ActionAge*(a.Action==AnimalAction.Teased?12f:a.Action==AnimalAction.Follow?10.5f:2.4f);
            float puppetPulse=Mathf.Sin(puppetPhase);
            float scaleX=1,scaleY=1,puppetTilt=0;
            if(livingColoring)
            {
                if(a.Action==AnimalAction.Teased)
                {
                    float beat=Mathf.Repeat(a.ActionAge,1.05f)/1.05f;
                    float crouch=beat<.23f?Mathf.Sin(beat/.23f*Mathf.PI):0;
                    float rise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.46f,beat));
                    float lower=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,1f,beat));
                    float stand=rise*(1-lower);
                    scaleX=1+.07f*crouch-.025f*stand;
                    scaleY=1-.11f*crouch+.055f*stand;
                    bob+=stand*.012f;
                    puppetTilt=0;
                }
                else if(a.Action==AnimalAction.Follow||a.Action==AnimalAction.Returning)
                {
                    float step=Mathf.Abs(puppetPulse);
                    // The body carries the gait.  Limbs only need a small
                    // anchored swing, which reads better and keeps the paper
                    // joints from opening up.
                    scaleX=1+.03f*step;scaleY=1-.025f*step;
                    bob+=step*.027f;puppetTilt=puppetPulse*.8f;
                }
                else if(a.Action==AnimalAction.Petted)
                {
                    scaleX=1+.025f*puppetPulse;scaleY=1-.018f*puppetPulse;
                    puppetTilt=Mathf.Sin(a.ActionAge*1.8f)*2.5f;
                }
                else
                {
                    scaleX=1+.016f*puppetPulse;scaleY=1-.012f*puppetPulse;
                    puppetTilt=Mathf.Sin(a.ActionAge*1.2f)*1.2f;
                }
            }
            float size=definition.ScreenSize*Mathf.Lerp(.75f,1,emerge)*scaleX;
            float cell=clip.UsesSeparateFrames?1f:1f/Mathf.Max(1,frameCount);
            float sourceWidth=clip.UsesSeparateFrames?texture.width:texture.width/(float)Mathf.Max(1,frameCount);
            float height=definition.ScreenSize*Mathf.Lerp(.75f,1,emerge)*1.77778f*texture.height/sourceWidth*scaleY;
            Halo(a.X,a.Y,.19f,new Color(1,.75f,.3f,.5f*(1-emerge)));
            Rect rect=R(a.X-size/2,a.Y-height/2-bob,size,height);
            var matrix=GUI.matrix;
            float tilt=definition.MotionStyle==AnimalMotionStyle.Fly?3:5;
            if(a.Action==AnimalAction.Teased)tilt+=3;
            GUIUtility.RotateAroundPivot(Mathf.Sin(a.Age*7)*tilt*motion+puppetTilt,rect.center);
            float returning=a.Returning?1-Mathf.Clamp01((a.ReturnAge-1.1f)/.5f):1;
            GUI.color=new Color(1,1,1,Mathf.Clamp01(a.Age/.2f)*returning);
            int sourceFrame=clip.UsesSeparateFrames?0:frame;
            bool sourceFacesRight=livingColoring?definition.ImportedFacesRight:true;
            if(livingColoring&&clip.RigId=="cat_v2")sourceFacesRight=true;
            bool drawUnmirrored=a.FacingRight==sourceFacesRight;
            if(livingColoring&&clip.PaperRig!=null)
                DrawPaperRig(clip.PaperRig,rect,a.Action,a.ActionAge,drawUnmirrored);
            else
                GUI.DrawTextureWithTexCoords(rect,texture,drawUnmirrored?new Rect(sourceFrame*cell,0,cell,1):new Rect((sourceFrame+1)*cell,0,-cell,1));
            GUI.color=Color.white;GUI.matrix=matrix;
        }
    }
    // Compose bone rotations in the cat's own coordinate space.  Calling
    // GUIUtility.RotateAroundPivot twice uses two different screen spaces and
    // made the paper parts drift apart when the whole cat stood up.
    static Matrix4x4 TransformRigPart(Matrix4x4 parent,float degrees,Vector2 pivot,float scaleY=1f)
    {
        return parent
            *Matrix4x4.Translate(new Vector3(pivot.x,pivot.y,0))
            *Matrix4x4.Rotate(Quaternion.Euler(0,0,degrees))
            *Matrix4x4.Scale(new Vector3(1,scaleY,1))
            *Matrix4x4.Translate(new Vector3(-pivot.x,-pivot.y,0));
    }
    static void DrawPaperRig(ColoringPaperRig rig,Rect rect,AnimalAction action,float age,bool unmirrored)
    {
        float gait=Mathf.Sin(age*10.5f);
        float slow=Mathf.Sin(age*2.4f);
        float tease=Mathf.Sin(age*12f);
        float facingSign=unmirrored?1f:-1f;
        float stand=0;
        Matrix4x4 rigMatrix=GUI.matrix;
        Matrix4x4 poseMatrix=rigMatrix;
        if(action==AnimalAction.Teased)
        {
            float beat=Mathf.Repeat(age,1.05f)/1.05f;
            float rise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.46f,beat));
            float lower=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,1f,beat));
            stand=rise*(1-lower);
            float supportX=unmirrored?.405f:.595f;
            Vector2 support=new Vector2(rect.x+supportX*rect.width,rect.y+.91f*rect.height);
            // Lean the complete paper cat back from its planted hind paw.  A
            // gentler shared rotation reads as "standing up" without asking
            // the cut-paper joints to cover a large gap.
            poseMatrix=TransformRigPart(rigMatrix,-24f*stand*facingSign,support);
        }
        Matrix4x4 tailRootMatrix=poseMatrix;
        foreach(var part in rig.Definition.Parts)
        {
            float angle=0;
            float partScaleY=1;
            if(action==AnimalAction.Follow||action==AnimalAction.Returning)
            {
                if(part.Id=="hind_leg_near"){angle=gait*3.6f;partScaleY=1-Mathf.Max(0,-gait)*.028f;}
                else if(part.Id=="hind_leg_far"){angle=-gait*2.6f;partScaleY=1-Mathf.Max(0,gait)*.021f;}
                else if(part.Id=="front_leg_near"){angle=-gait*4f;partScaleY=1-Mathf.Max(0,gait)*.03f;}
                else if(part.Id=="front_leg_far"){angle=gait*2.8f;partScaleY=1-Mathf.Max(0,-gait)*.022f;}
                else if(part.Id=="tail_root")angle=slow*4;
                else if(part.Id=="tail_tip")angle=slow*5;
                else if(part.Id=="head")angle=-gait*.8f;
            }
            else if(action==AnimalAction.Petted)
            {
                if(part.Id=="head")angle=3+slow*2;
                else if(part.Id=="tail_root")angle=slow*5;
                else if(part.Id=="tail_tip")angle=slow*9;
            }
            else if(action==AnimalAction.Teased)
            {
                // Keep every attachment nearly closed.  The shared lean and
                // stretch do most of the pose; the forepaws only reach a
                // little, so the neck and shoulders never pull apart.
                if(part.Id=="head")angle=tease*.4f;
                else if(part.Id=="front_leg_near"){angle=stand*-4;partScaleY=1+stand*.06f;}
                else if(part.Id=="front_leg_far"){angle=stand*-3;partScaleY=1+stand*.04f;}
                else if(part.Id=="tail_root")angle=stand*4+tease*2;
                else if(part.Id=="tail_tip")angle=stand*5+tease*2.5f;
            }
            else
            {
                if(part.Id=="head")angle=slow*1.5f;
                else if(part.Id=="tail_root")angle=slow*2.5f;
                else if(part.Id=="tail_tip")angle=slow*4.5f;
            }

            Texture2D texture=rig.Part(part.Id);if(texture==null)continue;
            float pivotX=unmirrored?part.PivotX:1-part.PivotX;
            Vector2 pivot=new Vector2(rect.x+pivotX*rect.width,rect.y+part.PivotY*rect.height);
            Matrix4x4 parentMatrix=part.ParentId=="tail_root"?tailRootMatrix:poseMatrix;
            GUI.matrix=TransformRigPart(parentMatrix,angle*facingSign,pivot,partScaleY);
            if(part.Id=="tail_root")tailRootMatrix=GUI.matrix;
            GUI.DrawTextureWithTexCoords(rect,texture,unmirrored?new Rect(0,0,1,1):new Rect(1,0,-1,1));
        }
        GUI.matrix=rigMatrix;
    }
    static AnimationArt ClipForAction(AnimalArt art,AnimalDefinition definition,AnimalAction action,out int frameCount)
    {
        // A photographed coloring stays the same character in every action.
        // The body language is added procedurally in DrawAnimals.
        if((action==AnimalAction.Follow||action==AnimalAction.Returning)&&art.Move!=null)
        {
            frameCount=definition.MoveFrames;return art.Move;
        }
        if(art.Imported!=null){frameCount=1;return art.Imported;}
        if(action==AnimalAction.Teased&&art.Special!=null){frameCount=definition.SpecialFrames;return art.Special;}
        if(action==AnimalAction.Petted&&art.Pet!=null){frameCount=definition.PetFrames;return art.Pet;}
        if(action==AnimalAction.Resting&&art.Idle!=null){frameCount=definition.IdleFrames;return art.Idle;}
        if(art.Move!=null){frameCount=definition.MoveFrames;return art.Move;}
        frameCount=definition.IdleFrames;return art.Idle;
    }
    void OnApplicationFocus(bool focused){if(!focused){mouseDown=false;hands[0].Active=hands[1].Active=false;}}
    void OnDestroy()
    {
        if(animalArt!=null)foreach(var art in animalArt)
            if(art?.Imported!=null){
                if(art.Imported.PaperRig!=null)art.Imported.PaperRig.Destroy();
                if(art.Imported.Frames!=null)foreach(var texture in art.Imported.Frames)if(texture!=null)Destroy(texture);
            }
        if(silhouette!=null)Destroy(silhouette);if(glow!=null)Destroy(glow);if(font!=null)Destroy(font);if(chime!=null)Destroy(chime);
    }
}
