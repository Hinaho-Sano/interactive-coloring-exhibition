using System;
using System.Threading;
using System.Runtime.InteropServices;
using UnityEngine;
#if AZURE_KINECT
using Microsoft.Azure.Kinect.Sensor;
#endif

public sealed class ExhibitionKinectInput : MonoBehaviour
{
    public bool startInMousePractice;
    public volatile int silhouetteMaxDepth=3800;
    public volatile bool backgroundReady;
    volatile bool captureBackground;
    public void CaptureBackground() { captureBackground=true; }
    public void ResetPlayerSelection() { GetComponent<MediaPipeGunPose>()?.ResetSelection(); }
#if AZURE_KINECT
    sealed class Snapshot
    {
        public byte[] Pixels;
        public int Width,Height;
        public double Timestamp;
        public byte[] Mask;
    }
    Thread worker;
    volatile bool stopping;
    readonly object gate=new object();
    Snapshot pending;
    string error,progress;
    IExhibitionInputTarget target;
    MediaPipeGunPose handPose;
    Device activeDevice;
    bool attemptedStart;

    [DllImport("k4a", CallingConvention=CallingConvention.Cdecl)]
    static extern int k4a_set_debug_message_handler(IntPtr callback,IntPtr context,LogLevel level);

    void OnEnable()
    {
        if(worker!=null&&worker.IsAlive) return;
        stopping=false;
        attemptedStart=false;
        foreach(var behaviour in GetComponents<MonoBehaviour>())
            if(behaviour is IExhibitionInputTarget inputTarget){target=inputTarget;break;}
        if(target==null){Debug.LogError("No exhibition input target is attached.");enabled=false;return;}
        handPose=GetComponent<MediaPipeGunPose>();
        if(handPose==null) handPose=gameObject.AddComponent<MediaPipeGunPose>();
        if(startInMousePractice) target.MousePractice=true;
        target.SetStatus(target.MousePractice?"Mouse practice / Kinect standby":"Azure Kinect starting...");
        if(!target.MousePractice) StartWorker();
    }
    void StartWorker()
    {
        if(attemptedStart||(worker!=null&&worker.IsAlive)) return;
        attemptedStart=true;
        stopping=false;
        target.SetStatus("Azure Kinect starting...");
        worker=new Thread(Run){IsBackground=true,Name="Azure Kinect Color Capture"};
        worker.Start();
    }
    void Run()
    {
        try
        {
            Microsoft.Azure.Kinect.Sensor.Logger.Initialize();
            k4a_set_debug_message_handler(IntPtr.Zero,IntPtr.Zero,LogLevel.Off);
            using(var device=Device.Open(0))
            {
                lock(gate) activeDevice=device;
                device.StartCameras(new DeviceConfiguration{
                    ColorFormat=ImageFormat.ColorBGRA32,ColorResolution=ColorResolution.R720p,
                    DepthMode=DepthMode.NFOV_Unbinned,CameraFPS=FPS.FPS30,SynchronizedImagesOnly=true
                });
                using(var transform=device.GetCalibration().CreateTransformation())
                using(var transformed=new Image(ImageFormat.Depth16,1280,720))
                {
                ushort[] background=null;
                lock(gate) progress="Color camera ready / show your hand";
                while(!stopping)
                {
                    try
                    {
                        using(var capture=device.GetCapture(TimeSpan.FromMilliseconds(100)))
                        using(var color=capture.Color)
                        {
                            if(color==null) continue;
                            var s=new Snapshot{Width=color.WidthPixels,Height=color.HeightPixels,Timestamp=color.DeviceTimestamp.TotalSeconds};
                            s.Pixels=new byte[s.Width*s.Height*4];
                            var memory=color.Memory.Span;
                            for(int row=0;row<s.Height;row++)
                                memory.Slice(row*color.StrideBytes,s.Width*4).CopyTo(s.Pixels.AsSpan(row*s.Width*4,s.Width*4));
                            transform.DepthImageToColorCamera(capture,transformed);
                            var depth=transformed.GetPixels<ushort>().Span;
                            const int mw=320,mh=180;
                            var sampled=new ushort[mw*mh];
                            for(int y=0;y<mh;y++) for(int x=0;x<mw;x++) sampled[y*mw+x]=depth[(y*720/mh)*1280+x*1280/mw];
                            if(captureBackground) { background=sampled; captureBackground=false; backgroundReady=true; }
                            s.Mask=new byte[mw*mh*4];
                            for(int i=0;i<sampled.Length;i++)
                            {
                                int z=sampled[i];
                                if(!DepthForeground.IsVisible(z,background==null?0:background[i],background!=null,silhouetteMaxDepth)) continue;
                                int j=i*4;
                                s.Mask[j]=30; s.Mask[j+1]=135; s.Mask[j+2]=190; s.Mask[j+3]=235;
                            }
                            // Only retain the latest frame. No inference backlog or body tracking queue.
                            lock(gate) pending=s;
                        }
                    }
                    catch(TimeoutException) { }
                }
                }
                device.StopCameras();
            }
        }
        catch(Exception ex) { if(!stopping) lock(gate) error=ex.GetType().Name+": "+ex.Message; }
        finally { lock(gate) activeDevice=null; }
    }
    void Update()
    {
        if(target!=null&&!target.MousePractice&&!attemptedStart) StartWorker();
        Snapshot s; string e,p;
        lock(gate) { s=pending; pending=null; e=error; error=null; p=progress; progress=null; }
        if(p!=null) target.SetStatus(p);
        if(s!=null)
        {
            GunInput left=default,right=default;
            if(!target.MousePractice)
            {
                handPose.ReadPair(s.Pixels,s.Width,s.Height,s.Timestamp,out left,out right);
                target.SetStatus(handPose.Status);
            }
            target.SetFrame(s.Pixels,s.Width,s.Height,left,right,s.Mask);
        }
        if(e!=null) { target.SetStatus("Kinect error: "+e); Debug.LogError(e); }
    }
    void OnDisable()
    {
        stopping=true;
        Device device;
        lock(gate) device=activeDevice;
        try { device?.StopCameras(); } catch(ObjectDisposedException) { }
        if(worker!=null&&!worker.Join(5000)) Debug.LogWarning("Kinect capture is still shutting down. Restart Unity before updating scripts.");
        worker=null;
        attemptedStart=false;
        lock(gate) pending=null;
    }
#endif
}

