using System;

// Time-based interaction model; only an open palm can wake a companion.
public sealed class MagicLibrary
{
    public sealed class Painting
    {
        public float X,Y,Contact,AwakeTime=-1,Glow;
        public float TouchX,TouchY,Energy,Sway,SwayVelocity;
        public bool Touching;
        public int Hand;
        public bool Awake=>AwakeTime>=0;
    }
    public readonly Painting[] Pictures={new Painting{X=.23f,Y=.43f},new Painting{X=.5f,Y=.36f},new Painting{X=.77f,Y=.43f}};
    public int Awakened { get { int n=0; foreach(var p in Pictures) if(p.Awake)n++; return n; } }
    public int NewAwakenings;
    public float Time;
    public void Reset() { foreach(var p in Pictures){p.Contact=p.Glow=p.Energy=p.Sway=p.SwayVelocity=0;p.AwakeTime=-1;p.Touching=false;} Time=0; }
    public static bool Calm(FieldHand h)=>h.Active&&h.Open&&h.Y<.85f&&h.VX*h.VX+h.VY*h.VY<.035f;
    public void Step(float dt,ShadowContact shadow,FieldHand left,FieldHand right,bool live,bool practice)
    {
        NewAwakenings=0;
        if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0)return;
        dt=Math.Min(dt,.05f);Time+=dt;
        var hands=new[]{left,right};
        for(int i=0;i<Pictures.Length;i++)
        {
            var p=Pictures[i]; bool overlap=false;
            FieldHand touching=default;
            for(int j=0;j<2;j++){var h=hands[j];if(h.Active&&h.Open&&Math.Abs(h.X-p.X)<.096f&&Math.Abs(h.Y-p.Y)<.154f&&(practice||live&&shadow.Touches(h.X,h.Y))){overlap=true;touching=h;p.Hand=j;}}
            if(overlap)
            {
                p.TouchX=touching.X;p.TouchY=touching.Y;
                p.Energy=Math.Min(1,p.Energy+dt*5);
                p.SwayVelocity+=((touching.X-p.X)*70+touching.VX*5)*dt;
            }
            else p.Energy=Math.Max(0,p.Energy-dt*.4f);
            p.Touching=overlap;
            p.SwayVelocity+=(-p.Sway*22-p.SwayVelocity*5)*dt;
            p.Sway+=p.SwayVelocity*dt;
            p.Sway=Math.Max(-.055f,Math.Min(.055f,p.Sway));
            // A mouse click should feel immediate; Kinect keeps the deliberate
            // half-second dwell that prevents accidental activation on a wall.
            p.Contact=Math.Max(0,Math.Min(.55f,p.Contact+(overlap?dt*(practice?40:1):-dt*2)));
            p.Glow+=( (overlap?1:0)-p.Glow)*dt*4;
            if(!p.Awake&&p.Contact>=.5f)
            {
                p.AwakeTime=Time;NewAwakenings++;
            }
        }
    }
}
