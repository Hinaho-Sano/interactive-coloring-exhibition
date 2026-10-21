using System;
using System.Runtime.InteropServices;
using UnityEngine;
#if MEDIAPIPE_HANDS
using Mediapipe.Tasks.Vision.HandLandmarker;
#endif

public sealed class MediaPipeGunPose : MonoBehaviour
{
    public string Status { get; private set; }="Hand tracker starting...";
#if MEDIAPIPE_HANDS
    HandLandmarker landmarker;
    byte[] rgb;
    readonly HandPoint[] points=new HandPoint[21];
    readonly Vector2[] previousPalm=new Vector2[2];
    readonly double[] previousSeen={-100,-100},lastOpen={-100,-100};
    long lastTimestamp=-1;
    bool failed;
#endif
    public void ResetSelection()
    {
#if MEDIAPIPE_HANDS
        for(int i=0;i<2;i++) previousSeen[i]=lastOpen[i]=-100;
#endif
    }
    public void ReadPair(byte[] bgra,int width,int height,double time,out GunInput left,out GunInput right)
    {
        left=new GunInput{Timestamp=time,BodyId=1}; right=new GunInput{Timestamp=time,BodyId=2};
#if MEDIAPIPE_HANDS
        if(failed) return;
        try
        {
            if(landmarker==null)
            {
                string path=System.IO.Path.Combine(Application.streamingAssetsPath,"hand_landmarker.task");
                if(!System.IO.File.Exists(path)) { Status="Missing hand model"; return; }
                landmarker=HandLandmarker.CreateFromOptions(new HandLandmarkerOptions(
                    new Mediapipe.Tasks.Core.BaseOptions(modelAssetBuffer:System.IO.File.ReadAllBytes(path)),
                    runningMode:Mediapipe.Tasks.Vision.Core.RunningMode.VIDEO,numHands:2,
                    minHandDetectionConfidence:.45f,minHandPresenceConfidence:.45f,minTrackingConfidence:.45f));
            }
            long timestamp=(long)(time*1000);
            if(timestamp<=lastTimestamp) return;
            lastTimestamp=timestamp;
            int rw=Math.Min(960,width),rh=height*rw/width;
            if(rgb==null||rgb.Length!=rw*rh*3) rgb=new byte[rw*rh*3];
            for(int y=0;y<rh;y++) for(int x=0;x<rw;x++)
            {
                int i=((y*height/rh)*width+(width-1-x*width/rw))*4,j=(y*rw+x)*3;
                rgb[j]=bgra[i+2]; rgb[j+1]=bgra[i+1]; rgb[j+2]=bgra[i];
            }
            var timing=System.Diagnostics.Stopwatch.StartNew();
            var pin=GCHandle.Alloc(rgb,GCHandleType.Pinned);
            try
            {
                using(var image=new Mediapipe.Image(Mediapipe.ImageFormat.Types.Format.Srgb,rw,rh,rw*3,pin.AddrOfPinnedObject(),IgnoreFree))
                {
                    var result=landmarker.DetectForVideo(image,timestamp);
                    int count=Math.Min(2,result.handLandmarks?.Count??0);
                    if(count==0) { Status="Show one or both open palms"; return; }
                    var palms=new Vector2[count]; var labels=new string[count];
                    for(int i=0;i<count;i++)
                    {
                        var marks=result.handLandmarks[i].landmarks;
                        foreach(int index in new[]{0,5,9,13,17}) palms[i]+=new Vector2(marks[index].x,marks[index].y);
                        palms[i]/=5;
                        if(result.handedness!=null&&i<result.handedness.Count&&result.handedness[i].categories.Count>0)
                            labels[i]=result.handedness[i].categories[0].categoryName;
                    }
                    int firstSlot;
                    if(count==2)
                        firstSlot=Cost(0,palms[0],labels[0],time)+Cost(1,palms[1],labels[1],time)<=Cost(1,palms[0],labels[0],time)+Cost(0,palms[1],labels[1],time)?0:1;
                    else firstSlot=Cost(0,palms[0],labels[0],time)<=Cost(1,palms[0],labels[0],time)?0:1;
                    int ready=0;
                    for(int i=0;i<count;i++)
                    {
                        int slot=i==0?firstSlot:1-firstSlot;
                        var palm=palms[i];
                        if(time-previousSeen[slot]>.5||Vector2.Distance(palm,previousPalm[slot])>.25f) lastOpen[slot]=-100;
                        previousSeen[slot]=time; previousPalm[slot]=palm;
                        bool open=false;
                        if(result.handWorldLandmarks!=null&&i<result.handWorldLandmarks.Count)
                        {
                            var world=result.handWorldLandmarks[i].landmarks;
                            if(world.Count==21)
                            {
                                for(int p=0;p<21;p++) points[p]=new HandPoint(world[p].x,world[p].y,world[p].z);
                                open=GunPoseRules.IsOpenPalm(points);
                            }
                        }
                        if(open) lastOpen[slot]=time;
                        var gun=new GunInput{Tracked=true,Pose=open||time-lastOpen[slot]<.18,Timestamp=time,BodyId=(uint)(slot+1),
                            AimX=1-palm.x,AimY=palm.y,HandX=1-palm.x,HandY=palm.y};
                        if(gun.Pose) ready++;
                        if(slot==0) left=gun; else right=gun;
                    }
                    Status=count+" HANDS / "+ready+" PALMS READY / "+timing.ElapsedMilliseconds+" ms";
                }
            }
            finally { pin.Free(); }
        }
        catch(Exception e) { failed=true; Status="MediaPipe error: "+e.Message; Debug.LogError(Status); }
#endif
    }
#if MEDIAPIPE_HANDS
    float Cost(int slot,Vector2 palm,string label,double time)
    {
        float cost=time-previousSeen[slot]<.5?Vector2.SqrMagnitude(palm-previousPalm[slot]):0;
        if(label!=(slot==0?"Left":"Right")) cost+=.12f;
        return cost;
    }
    static readonly Mediapipe.ImageFrame.Deleter IgnoreFree=NoFree;
    [AOT.MonoPInvokeCallback(typeof(Mediapipe.ImageFrame.Deleter))]
    static void NoFree(IntPtr pointer) { }
    void OnDisable() { landmarker?.Close(); landmarker=null; lastTimestamp=-1; failed=false; ResetSelection(); }
#endif
}
