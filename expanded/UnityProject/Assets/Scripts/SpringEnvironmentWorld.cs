using System;
using System.Collections.Generic;

// Pure state model for the spring environment mode. Coordinates use the same
// normalized, top-left-origin display space as FieldHand and ExhibitionController.
// Rendering and sound deliberately live elsewhere, so this class can be tested
// without Unity and the complete mode can be removed without touching animal code.
public sealed class SpringEnvironmentWorld
{
    public const int MaxLeaves = 96;
    public const int MaxRipples = 32;
    public const int MaxSplashes = 72;
    public const int MaxFluff = 160;

    // Interaction zones are public so the view can give visitors a subtle visual
    // invitation without duplicating the hit-test values.
    public const float TreeCenterX = .205f;
    public const float TreeCenterY = .345f;
    public const float TreeRadiusX = .155f;
    public const float TreeRadiusY = .245f;

    public const float RiverLeft = .34f;
    public const float RiverRight = .97f;
    public const float RiverTop = .59f;
    public const float RiverBottom = .95f;

    public const float SkyLeft = .47f;
    public const float SkyRight = .94f;
    public const float SkyTop = .055f;
    public const float SkyBottom = .36f;

    const float Aspect = 16f / 9f;
    const uint InitialRandomState = 0x51A7E123u;

    public struct LeafParticle
    {
        public float X, Y, VX, VY, Angle, Spin, Age, Lifetime, Size;
        public int Variant;
    }

    public struct RippleParticle
    {
        public float X, Y, Age, Lifetime, Strength, StartRadius;
    }

    public struct SplashParticle
    {
        public float X, Y, VX, VY, Age, Lifetime, Size;
    }

    public struct FluffParticle
    {
        public float X, Y, VX, VY, Age, Lifetime, Size, Phase;
    }

    public sealed class DandelionHead
    {
        public readonly float X;
        public readonly float Y;
        public readonly int Capacity;

        public int SeedsRemaining { get; internal set; }
        public float Glow { get; internal set; }
        public float Bend { get; internal set; }

        internal float Recharge;

        internal DandelionHead(float x, float y, int capacity)
        {
            X = x;
            Y = y;
            Capacity = capacity;
            SeedsRemaining = capacity;
        }
    }

    readonly List<LeafParticle> leaves = new List<LeafParticle>(MaxLeaves);
    readonly List<RippleParticle> ripples = new List<RippleParticle>(MaxRipples);
    readonly List<SplashParticle> splashes = new List<SplashParticle>(MaxSplashes);
    readonly List<FluffParticle> fluff = new List<FluffParticle>(MaxFluff);

    readonly DandelionHead[] dandelions =
    {
        new DandelionHead(.075f, .79f, 12),
        new DandelionHead(.14f, .89f, 12),
        new DandelionHead(.215f, .80f, 12),
        new DandelionHead(.285f, .91f, 12),
        new DandelionHead(.36f, .81f, 12),
        new DandelionHead(.47f, .91f, 12),
        new DandelionHead(.58f, .94f, 12),
        new DandelionHead(.72f, .955f, 12),
        new DandelionHead(.87f, .965f, 12)
    };

    readonly bool[] treeTouching = new bool[2];
    readonly bool[] riverTouching = new bool[2];
    readonly bool[] skyTouching = new bool[2];
    readonly float[] leafEmission = new float[2];
    readonly float[] rippleEmission = new float[2];
    readonly float[] splashEmission = new float[2];
    readonly float[] skyDwell = new float[2];
    readonly bool[,] dandelionTouching;
    readonly float[,] fluffEmission;

    uint randomState;
    float treeSwayVelocity;
    float riverTargetEnergy;
    float riverTargetFlow;
    float rainbowHold;

    public IReadOnlyList<LeafParticle> Leaves => leaves;
    public IReadOnlyList<RippleParticle> Ripples => ripples;
    public IReadOnlyList<SplashParticle> Splashes => splashes;
    public IReadOnlyList<FluffParticle> Fluff => fluff;
    public IReadOnlyList<DandelionHead> Dandelions => dandelions;

    public float Time { get; private set; }
    public float TreeSway { get; private set; }
    public float TreeEnergy { get; private set; }
    public float RiverEnergy { get; private set; }
    public float RiverFlow { get; private set; }
    public float RainbowReveal { get; private set; }
    public float RainbowCenterX { get; private set; }

    // Per-Step event counts are useful for rendering emphasis and optional sound,
    // but the first prototype does not require an audio dependency.
    public int NewLeaves { get; private set; }
    public int NewRipples { get; private set; }
    public int NewSplashes { get; private set; }
    public int NewFluff { get; private set; }
    public bool RainbowActivated { get; private set; }

    public SpringEnvironmentWorld()
    {
        dandelionTouching = new bool[2, dandelions.Length];
        fluffEmission = new float[2, dandelions.Length];
        Reset();
    }

    public void Reset()
    {
        leaves.Clear();
        ripples.Clear();
        splashes.Clear();
        fluff.Clear();

        Array.Clear(treeTouching, 0, treeTouching.Length);
        Array.Clear(riverTouching, 0, riverTouching.Length);
        Array.Clear(skyTouching, 0, skyTouching.Length);
        Array.Clear(leafEmission, 0, leafEmission.Length);
        Array.Clear(rippleEmission, 0, rippleEmission.Length);
        Array.Clear(splashEmission, 0, splashEmission.Length);
        Array.Clear(skyDwell, 0, skyDwell.Length);
        Array.Clear(dandelionTouching, 0, dandelionTouching.Length);
        Array.Clear(fluffEmission, 0, fluffEmission.Length);

        foreach (DandelionHead head in dandelions)
        {
            head.SeedsRemaining = head.Capacity;
            head.Glow = 0f;
            head.Bend = 0f;
            head.Recharge = 0f;
        }

        randomState = InitialRandomState;
        Time = 0f;
        TreeSway = TreeEnergy = treeSwayVelocity = 0f;
        RiverEnergy = riverTargetEnergy = 0f;
        RiverFlow = riverTargetFlow = 0f;
        RainbowReveal = rainbowHold = 0f;
        RainbowCenterX = .71f;
        ClearStepEvents();
    }

    public void Step(float seconds, FieldHand first, FieldHand second)
    {
        ClearStepEvents();
        if (!Finite(seconds) || seconds <= 0f) return;

        // A lost frame must not launch hundreds of particles or destabilize the
        // damped motion. The exhibition may pause briefly when another window opens.
        float dt = Math.Min(seconds, .05f);
        Time += dt;
        riverTargetEnergy = 0f;
        riverTargetFlow = 0f;

        StepHand(0, Sanitize(first), dt);
        StepHand(1, Sanitize(second), dt);
        StepEnvironment(dt);
        StepParticles(dt);
    }

    void ClearStepEvents()
    {
        NewLeaves = NewRipples = NewSplashes = NewFluff = 0;
        RainbowActivated = false;
    }

    void StepHand(int index, FieldHand hand, float dt)
    {
        bool intentional = hand.Active && hand.Open;
        float speed = intentional ? Length(hand.VX * Aspect, hand.VY) : 0f;

        float treeWeight = intentional
            ? EllipseWeight(hand.X, hand.Y, TreeCenterX, TreeCenterY, TreeRadiusX, TreeRadiusY)
            : 0f;
        bool onTree = treeWeight > 0f;
        if (onTree)
        {
            TreeEnergy = Clamp01(TreeEnergy + dt * (1.8f + treeWeight * 3.5f));
            treeSwayVelocity += Clamp(hand.VX, -2f, 2f) * treeWeight * dt * 7f;
            if (!treeTouching[index]) leafEmission[index] += 2.25f;
            leafEmission[index] += dt * treeWeight * (4f + Math.Min(speed, 2f) * 10f);
            while (leafEmission[index] >= 1f)
            {
                leafEmission[index] -= 1f;
                SpawnLeaf(hand, treeWeight);
            }
        }
        else leafEmission[index] = Math.Max(0f, leafEmission[index] - dt * 2f);
        treeTouching[index] = onTree;

        float riverWeight = intentional ? RiverWeightAt(hand.X, hand.Y) : 0f;
        bool onRiver = riverWeight > 0f;
        if (onRiver)
        {
            float verticalWeight = riverWeight;
            riverTargetEnergy += .35f + verticalWeight * .45f + Math.Min(speed, 2f) * .25f;
            riverTargetFlow += Clamp(hand.VX, -2f, 2f) * .45f;

            if (!riverTouching[index]) rippleEmission[index] += 1.15f;
            rippleEmission[index] += dt * (2.2f + speed * 4.2f);
            while (rippleEmission[index] >= 1f)
            {
                rippleEmission[index] -= 1f;
                SpawnRipple(hand.X, hand.Y, .55f + Math.Min(speed, 2f) * .25f);
            }

            if (speed > .24f)
            {
                splashEmission[index] += dt * (speed - .16f) * 8f;
                if (!riverTouching[index]) splashEmission[index] += 1f;
                while (splashEmission[index] >= 1f)
                {
                    splashEmission[index] -= 1f;
                    SpawnSplash(hand);
                }
            }
        }
        else
        {
            rippleEmission[index] = Math.Max(0f, rippleEmission[index] - dt * 2f);
            splashEmission[index] = Math.Max(0f, splashEmission[index] - dt * 3f);
        }
        riverTouching[index] = onRiver;

        bool inSky = intentional && hand.X >= SkyLeft && hand.X <= SkyRight
            && hand.Y >= SkyTop && hand.Y <= SkyBottom;
        if (inSky)
        {
            bool wasHidden = RainbowReveal < .04f;
            skyDwell[index] = Math.Min(1f, skyDwell[index] + dt);
            RainbowReveal = Clamp01(RainbowReveal + dt * (.52f + skyDwell[index] * 1.25f));
            RainbowCenterX += (hand.X - RainbowCenterX) * Math.Min(1f, dt * 1.7f);
            rainbowHold = 4.5f;
            if (wasHidden && RainbowReveal >= .04f) RainbowActivated = true;
        }
        else skyDwell[index] = Math.Max(0f, skyDwell[index] - dt * 2f);
        skyTouching[index] = inSky;

        StepDandelionHand(index, hand, intentional, speed, dt);
    }

    public static float RiverCenterYAt(float x)
    {
        float t = Clamp01((x - RiverLeft) / (RiverRight - RiverLeft));
        return .665f + t * .15f;
    }

    public static float RiverHalfWidthAt(float x)
    {
        float t = Clamp01((x - RiverLeft) / (RiverRight - RiverLeft));
        return .072f + t * .072f;
    }

    public static float RiverWeightAt(float x, float y)
    {
        if (x < RiverLeft || x > RiverRight) return 0f;
        float halfWidth = RiverHalfWidthAt(x);
        return Clamp01(1f - Math.Abs(y - RiverCenterYAt(x)) / halfWidth);
    }

    void StepDandelionHand(int handIndex, FieldHand hand, bool intentional, float speed, float dt)
    {
        for (int i = 0; i < dandelions.Length; i++)
        {
            DandelionHead head = dandelions[i];
            float weight = intentional
                ? EllipseWeight(hand.X, hand.Y, head.X, head.Y, .095f, .13f)
                : 0f;
            bool touching = weight > 0f;
            if (touching)
            {
                head.Glow = Clamp01(head.Glow + dt * (2f + weight * 5f));
                head.Bend = Clamp(head.Bend + hand.VX * weight * dt * 3.5f, -1f, 1f);
                head.Recharge = 0f;

                if (!dandelionTouching[handIndex, i]) fluffEmission[handIndex, i] += 1.75f;
                fluffEmission[handIndex, i] += dt * weight * (1.2f + Math.Min(speed, 2f) * 7f);
                while (fluffEmission[handIndex, i] >= 1f && head.SeedsRemaining > 0)
                {
                    fluffEmission[handIndex, i] -= 1f;
                    head.SeedsRemaining--;
                    SpawnFluff(head, hand);
                }
            }
            else fluffEmission[handIndex, i] = Math.Max(0f, fluffEmission[handIndex, i] - dt);
            dandelionTouching[handIndex, i] = touching;
        }
    }

    void StepEnvironment(float dt)
    {
        TreeEnergy = Math.Max(0f, TreeEnergy - dt * .32f);
        treeSwayVelocity += -TreeSway * 12f * dt;
        treeSwayVelocity *= (float)Math.Exp(-4.2f * dt);
        TreeSway = Clamp(TreeSway + treeSwayVelocity * dt, -1f, 1f);

        RiverEnergy += (Clamp01(riverTargetEnergy) - RiverEnergy) * Math.Min(1f, dt * 6f);
        RiverFlow += (Clamp(riverTargetFlow, -1f, 1f) - RiverFlow) * Math.Min(1f, dt * 4f);

        if (rainbowHold > 0f) rainbowHold = Math.Max(0f, rainbowHold - dt);
        else RainbowReveal = Math.Max(0f, RainbowReveal - dt * .11f);

        for (int i = 0; i < dandelions.Length; i++)
        {
            DandelionHead head = dandelions[i];
            bool touched = false;
            for (int hand = 0; hand < 2; hand++)
                touched |= dandelionTouching[hand, i];

            head.Glow = Math.Max(0f, head.Glow - dt * .5f);
            head.Bend += (0f - head.Bend) * Math.Min(1f, dt * 2.4f);
            if (touched || head.SeedsRemaining >= head.Capacity)
            {
                head.Recharge = 0f;
                continue;
            }

            // A head slowly grows its fluff back so a long-running exhibition
            // does not become empty after the first group of visitors.
            head.Recharge += dt;
            if (head.Recharge >= 1.35f)
            {
                int recovered = (int)(head.Recharge / 1.35f);
                head.Recharge -= recovered * 1.35f;
                head.SeedsRemaining = Math.Min(head.Capacity, head.SeedsRemaining + recovered);
            }
        }
    }

    void StepParticles(float dt)
    {
        for (int i = leaves.Count - 1; i >= 0; i--)
        {
            LeafParticle p = leaves[i];
            p.Age += dt;
            float breeze = (float)Math.Sin(Time * 1.35f + p.Variant * 1.7f + p.Y * 8f);
            p.VX += breeze * dt * .035f;
            p.VY += dt * .085f;
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.Angle += p.Spin * dt;
            if (p.Age >= p.Lifetime || p.Y > 1.08f || p.X < -.08f || p.X > 1.08f)
                leaves.RemoveAt(i);
            else leaves[i] = p;
        }

        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            RippleParticle p = ripples[i];
            p.Age += dt;
            p.X += (.018f + RiverFlow * .025f) * dt;
            if (p.Age >= p.Lifetime || p.X > 1.08f) ripples.RemoveAt(i);
            else ripples[i] = p;
        }

        for (int i = splashes.Count - 1; i >= 0; i--)
        {
            SplashParticle p = splashes[i];
            p.Age += dt;
            p.VX += RiverFlow * dt * .025f;
            p.VY += dt * .42f;
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            if (p.Age >= p.Lifetime || p.Y > RiverBottom + .08f) splashes.RemoveAt(i);
            else splashes[i] = p;
        }

        for (int i = fluff.Count - 1; i >= 0; i--)
        {
            FluffParticle p = fluff[i];
            p.Age += dt;
            float curl = (float)Math.Sin(Time * 1.15f + p.Phase + p.Y * 9f);
            p.VX += curl * dt * .023f;
            p.VY += (float)Math.Sin(Time * .8f + p.Phase) * dt * .006f;
            p.VX = Clamp(p.VX, -.18f, .18f);
            p.VY = Clamp(p.VY, -.12f, .055f);
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            if (p.Age >= p.Lifetime || p.Y < -.08f || p.X < -.08f || p.X > 1.08f)
                fluff.RemoveAt(i);
            else fluff[i] = p;
        }
    }

    void SpawnLeaf(FieldHand hand, float weight)
    {
        LeafParticle particle = new LeafParticle
        {
            X = TreeCenterX + NextSigned() * TreeRadiusX * .72f,
            Y = TreeCenterY - TreeRadiusY * .66f + Next01() * TreeRadiusY * .95f,
            VX = hand.VX * (.012f + weight * .018f) + NextSigned() * .025f,
            VY = .025f + Next01() * .04f,
            Angle = Next01() * 360f,
            Spin = NextSigned() * (40f + Next01() * 80f),
            Age = 0f,
            Lifetime = 4.2f + Next01() * 2.8f,
            Size = .007f + Next01() * .008f,
            Variant = (int)(Next01() * 4f)
        };
        AddBounded(leaves, particle, MaxLeaves);
        NewLeaves++;
    }

    void SpawnRipple(float x, float y, float strength)
    {
        float particleX = Clamp(x + NextSigned() * .012f, RiverLeft, RiverRight);
        float centerY = RiverCenterYAt(particleX);
        float halfWidth = RiverHalfWidthAt(particleX);
        RippleParticle particle = new RippleParticle
        {
            X = particleX,
            Y = Clamp(y + NextSigned() * .009f, centerY - halfWidth, centerY + halfWidth),
            Age = 0f,
            Lifetime = 1.05f + Next01() * .45f,
            Strength = Clamp01(strength),
            StartRadius = .006f + Next01() * .006f
        };
        AddBounded(ripples, particle, MaxRipples);
        NewRipples++;
    }

    void SpawnSplash(FieldHand hand)
    {
        float particleX = Clamp(hand.X + NextSigned() * .012f, RiverLeft, RiverRight);
        float centerY = RiverCenterYAt(particleX);
        float halfWidth = RiverHalfWidthAt(particleX);
        SplashParticle particle = new SplashParticle
        {
            X = particleX,
            Y = Clamp(hand.Y + NextSigned() * .008f, centerY - halfWidth, centerY + halfWidth),
            VX = hand.VX * .022f + NextSigned() * .035f,
            VY = -.075f - Next01() * .12f - Math.Abs(hand.VY) * .02f,
            Age = 0f,
            Lifetime = .55f + Next01() * .55f,
            Size = .0035f + Next01() * .005f
        };
        AddBounded(splashes, particle, MaxSplashes);
        NewSplashes++;
    }

    void SpawnFluff(DandelionHead head, FieldHand hand)
    {
        FluffParticle particle = new FluffParticle
        {
            X = head.X + NextSigned() * .011f,
            Y = head.Y - .045f + NextSigned() * .008f,
            VX = .015f + hand.VX * .035f + NextSigned() * .025f,
            VY = -.018f - Next01() * .035f + hand.VY * .012f,
            Age = 0f,
            Lifetime = 5.2f + Next01() * 3.2f,
            Size = .0045f + Next01() * .004f,
            Phase = Next01() * 6.2831853f
        };
        AddBounded(fluff, particle, MaxFluff);
        NewFluff++;
    }

    static void AddBounded<T>(List<T> list, T item, int capacity)
    {
        // Recycle the oldest visual when full so a new visitor always receives
        // immediate feedback, while memory and draw cost remain strictly bounded.
        if (list.Count >= capacity) list.RemoveAt(0);
        list.Add(item);
    }

    static FieldHand Sanitize(FieldHand hand)
    {
        if (!hand.Active || !Finite(hand.X) || !Finite(hand.Y)
            || !Finite(hand.VX) || !Finite(hand.VY))
            return default(FieldHand);

        hand.X = Clamp(hand.X, 0f, 1f);
        hand.Y = Clamp(hand.Y, 0f, 1f);
        hand.VX = Clamp(hand.VX, -2f, 2f);
        hand.VY = Clamp(hand.VY, -2f, 2f);
        return hand;
    }

    static float EllipseWeight(float x, float y, float centerX, float centerY, float radiusX, float radiusY)
    {
        float dx = (x - centerX) / radiusX;
        float dy = (y - centerY) / radiusY;
        float squared = dx * dx + dy * dy;
        return squared >= 1f ? 0f : 1f - squared;
    }

    float Next01()
    {
        uint x = randomState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        randomState = x == 0 ? InitialRandomState : x;
        return (randomState & 0x00FFFFFFu) / 16777215f;
    }

    float NextSigned() => Next01() * 2f - 1f;

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static float Length(float x, float y) => (float)Math.Sqrt(x * x + y * y);
    static float Clamp01(float value) => Clamp(value, 0f, 1f);
    static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
}
