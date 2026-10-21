using System;

public struct HandPoint
{
    public float X,Y,Z;
    public HandPoint(float x,float y,float z) { X=x; Y=y; Z=z; }
}

public static class GunPoseRules
{
    // Three extended fingers are enough; thumb position and finger spreading are optional.
    public static bool IsOpenPalm(HandPoint[] p)
    {
        if(p==null||p.Length!=21) return false;
        foreach(var q in p) if(float.IsNaN(q.X)||float.IsNaN(q.Y)||float.IsNaN(q.Z)||float.IsInfinity(q.X)||float.IsInfinity(q.Y)||float.IsInfinity(q.Z)) return false;
        if(Distance(p[5],p[17])<1e-6) return false;
        int extended=0;
        foreach(int mcp in new[]{5,9,13,17})
            if(Angle(p[mcp],p[mcp+1],p[mcp+3])>=120&&Distance(p[mcp+3],p[0])>=Distance(p[mcp+1],p[0])*1.05) extended++;
        return extended>=3;
    }
    static double Distance(HandPoint a,HandPoint b)
    {
        double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return Math.Sqrt(x*x+y*y+z*z);
    }
    static double Angle(HandPoint a,HandPoint b,HandPoint c)
    {
        double ax=a.X-b.X,ay=a.Y-b.Y,az=a.Z-b.Z,bx=c.X-b.X,by=c.Y-b.Y,bz=c.Z-b.Z;
        double length=Math.Sqrt((ax*ax+ay*ay+az*az)*(bx*bx+by*by+bz*bz));
        return length<1e-9?0:Math.Acos(Math.Max(-1,Math.Min(1,(ax*bx+ay*by+az*bz)/length)))*180/Math.PI;
    }
    public static bool IsFingerGun(HandPoint[] p)
    {
        if(p==null||p.Length!=21) return false;
        foreach(var q in p) if(float.IsNaN(q.X)||float.IsNaN(q.Y)||float.IsNaN(q.Z)||float.IsInfinity(q.X)||float.IsInfinity(q.Y)||float.IsInfinity(q.Z)) return false;
        if(Angle(p[5],p[6],p[8])<150||Distance(p[8],p[0])<Distance(p[6],p[0])*1.12) return false;
        foreach(int mcp in new[]{9,13,17})
            if(Angle(p[mcp],p[mcp+1],p[mcp+3])>150||Distance(p[mcp+3],p[0])>Distance(p[mcp+1],p[0])*1.15) return false;
        double palm=Distance(p[5],p[17]);
        return palm>1e-6&&Distance(p[4],p[5])>palm*.35;
    }
}
