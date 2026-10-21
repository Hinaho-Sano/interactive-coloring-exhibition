using System;

public struct GunInput
{
    public bool Tracked,Pose;
    public uint BodyId;
    public float AimX,AimY,HandX,HandY,RelativeHandY;
    public double Timestamp;
}
public static class DepthForeground
{
    public static bool IsVisible(int depth,int background,bool calibrated,int maxDepth)
        => depth>=450&&depth<=maxDepth&&(!calibrated||background==0||background-depth>=120);
}
public struct FieldHand
{
    public bool Active,Open;
    public float X,Y,VX,VY;
}

// A continuous elastic field. Rest positions retain visitors' changes until an explicit reset.
public sealed class ResonanceField
{
    public const int Columns=37,Rows=21;
    public const float Aspect=16f/9;
    public sealed class Node
    {
        public float X,Y,VX,VY,RestX,RestY,OriginX,OriginY,Memory,Warmth;
    }
    public readonly Node[] Nodes=new Node[Columns*Rows];
    readonly float[] forceX=new float[Columns*Rows],forceY=new float[Columns*Rows];
    public float Activity { get; private set; }
    public ResonanceField() { Reset(); }
    public void Reset()
    {
        for(int row=0;row<Rows;row++) for(int col=0;col<Columns;col++)
        {
            float x=.055f+col*.89f/(Columns-1),y=.13f+row*.72f/(Rows-1);
            y+=(float)Math.Sin(x*9+row*.28f)*.012f;
            Nodes[row*Columns+col]=new Node{X=x,Y=y,RestX=x,RestY=y,OriginX=x,OriginY=y,Warmth=.25f};
        }
        Activity=0;
    }
    static float Clamp(float x,float low,float high)=>Math.Max(low,Math.Min(high,x));
    static bool Valid(FieldHand h)=>h.Active&&!float.IsNaN(h.X)&&!float.IsInfinity(h.X)&&!float.IsNaN(h.Y)&&!float.IsInfinity(h.Y)&&!float.IsNaN(h.VX)&&!float.IsInfinity(h.VX)&&!float.IsNaN(h.VY)&&!float.IsInfinity(h.VY);
    public void Step(float seconds,float time,FieldHand left,FieldHand right,float spreadVelocity)
    {
        if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0) return;
        if(float.IsNaN(time)||float.IsInfinity(time)) time=0;
        if(float.IsNaN(spreadVelocity)||float.IsInfinity(spreadVelocity)) spreadVelocity=0;
        // Fixed substeps bound the springs even on a slow exhibition computer.
        float remaining=Math.Min(seconds,.1f);
        while(remaining>0)
        {
            float dt=Math.Min(remaining,1f/120); remaining-=dt;
            Advance(dt,time,left,right,spreadVelocity);
        }
    }
    void Advance(float dt,float time,FieldHand left,FieldHand right,float spreadVelocity)
    {
        bool l=Valid(left),r=Valid(right);
        float energy=0;
        for(int i=0;i<Nodes.Length;i++)
        {
            var n=Nodes[i];
            float fx=(n.RestX-n.X)*5.5f,fy=(n.RestY-n.Y)*5.5f;
            int col=i%Columns,row=i/Columns;
            if(col>0) Spring(n,Nodes[i-1],ref fx,ref fy);
            if(col<Columns-1) Spring(n,Nodes[i+1],ref fx,ref fy);
            if(row>0) Spring(n,Nodes[i-Columns],ref fx,ref fy);
            if(row<Rows-1) Spring(n,Nodes[i+Columns],ref fx,ref fy);
            // Small reversible breathing; this never writes to the world's memory.
            fy+=(float)Math.Sin(time*.55f+n.OriginX*8+n.OriginY*5)*.0018f;
            if(l) Influence(n,left,0,dt,ref fx,ref fy);
            if(r) Influence(n,right,1,dt,ref fx,ref fy);
            if(l&&r)
            {
                float mx=(left.X+right.X)*.5f,my=(left.Y+right.Y)*.5f;
                float dx=n.X-mx,dy=n.Y-my;
                float weight=(float)Math.Exp(-(dx*dx*Aspect*Aspect+dy*dy)/.10f);
                float stretch=Clamp(spreadVelocity,-1.5f,1.5f)*weight;
                fx+=dx*stretch*3; fy+=dy*stretch*3;
                n.RestX=Clamp(n.RestX+dx*stretch*dt*.16f,n.OriginX-.14f,n.OriginX+.14f);
                n.RestY=Clamp(n.RestY+dy*stretch*dt*.16f,n.OriginY-.14f,n.OriginY+.14f);
            }
            forceX[i]=fx; forceY[i]=fy;
        }
        float damping=(float)Math.Exp(-2.6f*dt);
        for(int i=0;i<Nodes.Length;i++)
        {
            var n=Nodes[i];
            n.VX=Clamp((n.VX+forceX[i]*dt)*damping,-.8f,.8f);
            n.VY=Clamp((n.VY+forceY[i]*dt)*damping,-.8f,.8f);
            n.X=Clamp(n.X+n.VX*dt,-.04f,1.04f); n.Y=Clamp(n.Y+n.VY*dt,-.04f,1.04f);
            energy+=Math.Abs(n.VX)+Math.Abs(n.VY);
        }
        Activity+=(Clamp(energy/Nodes.Length*8,0,1)-Activity)*Math.Min(1,dt*3);
    }
    static void Spring(Node n,Node other,ref float fx,ref float fy)
    {
        fx+=((other.X-other.RestX)-(n.X-n.RestX))*13;
        fy+=((other.Y-other.RestY)-(n.Y-n.RestY))*13;
    }
    static void Influence(Node n,FieldHand hand,float warmth,float dt,ref float fx,ref float fy)
    {
        float dx=hand.X-n.X,dy=hand.Y-n.Y;
        float weight=(float)Math.Exp(-(dx*dx*Aspect*Aspect+dy*dy)/.022f);
        float vx=Clamp(hand.VX,-2,2),vy=Clamp(hand.VY,-2,2);
        float pull=hand.Open?1.8f:-1.0f;
        fx+=(dx*pull+vx*.65f-dy*.12f)*weight;
        fy+=(dy*pull+vy*.65f+dx*.12f)*weight;
        // This slow plastic deformation remains after hands leave the scene.
        n.RestX=Clamp(n.RestX+(dx*.07f+vx*.08f)*weight*dt,n.OriginX-.14f,n.OriginX+.14f);
        n.RestY=Clamp(n.RestY+(dy*.07f+vy*.08f)*weight*dt,n.OriginY-.14f,n.OriginY+.14f);
        n.Memory=Clamp(n.Memory+weight*dt*.8f,0,1);
        n.Warmth+=(warmth-n.Warmth)*weight*dt*1.5f;
    }
}
