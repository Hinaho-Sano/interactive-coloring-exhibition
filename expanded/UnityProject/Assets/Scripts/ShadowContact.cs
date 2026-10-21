using System;

// Display-space mask/echo. Kinect pixels are top-down and horizontally unmirrored.
public sealed class ShadowContact
{
    public const int Width=320,Height=180;
    byte[] mask;
    public readonly float[] Echo=new float[Width*Height];
    public readonly float[] Warmth=new float[Width*Height];
    public void SetMask(byte[] rgba) { mask=rgba!=null&&rgba.Length==Width*Height*4?rgba:null; }
    public bool Available=>mask!=null;
    public bool Inside(float x,float y)
    {
        if(mask==null||float.IsNaN(x)||float.IsNaN(y)||x<0||x>1||y<0||y>1) return false;
        int px=Math.Min(Width-1,(int)((1-x)*Width)),py=Math.Min(Height-1,(int)(y*Height));
        return mask[(py*Width+px)*4+3]>100;
    }
    public bool Touches(float x,float y)
    {
        if(Inside(x,y)) return true;
        const float r=.014f;
        return Inside(x-r,y)||Inside(x+r,y)||Inside(x,y-r)||Inside(x,y+r);
    }
    public void Step(float dt,FieldHand left,FieldHand right)
    {
        if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0) return;
        dt=Math.Min(dt,.1f);
        float fade=(float)Math.Exp(-dt/18);
        for(int i=0;i<Echo.Length;i++) Echo[i]*=fade;
        Paint(left,0,dt); Paint(right,1,dt);
    }
    void Paint(FieldHand hand,float warmth,float dt)
    {
        if(!hand.Active||!Touches(hand.X,hand.Y)) return;
        int x0=Math.Max(0,(int)((hand.X-.13f)*Width)),x1=Math.Min(Width-1,(int)((hand.X+.13f)*Width));
        int y0=Math.Max(0,(int)((hand.Y-.22f)*Height)),y1=Math.Min(Height-1,(int)((hand.Y+.22f)*Height));
        for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
        {
            float px=(x+.5f)/Width,py=(y+.5f)/Height;
            if(!Inside(px,py)) continue;
            float dx=(px-hand.X)*16f/9,dy=py-hand.Y;
            float weight=(float)Math.Exp(-(dx*dx+dy*dy)/.012f);
            int i=y*Width+x;
            Echo[i]=Math.Min(1,Echo[i]+weight*dt*2);
            Warmth[i]+=(warmth-Warmth[i])*weight*dt*4;
        }
    }
    public void Reset() { Array.Clear(Echo,0,Echo.Length); Array.Clear(Warmth,0,Warmth.Length); }
}
