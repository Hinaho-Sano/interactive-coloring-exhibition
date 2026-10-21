using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Standalone view/input adapter for the spring environment mode.  It shares
// the Kinect component with the animal exhibit through IExhibitionInputTarget,
// but deliberately has no dependency on the animal catalog or companion state.
public sealed class SpringEnvironmentController : MonoBehaviour, IExhibitionInputTarget
{
    public bool mousePractice = true;
    public bool showSilhouette = true;

    readonly SpringEnvironmentWorld world = new SpringEnvironmentWorld();
    readonly FieldHand[] hands = new FieldHand[2];
    readonly Vector2[] previous = new Vector2[2];
    readonly double[] seen = { -100d, -100d };
    readonly float[] frameAt = { -100f, -100f };
    readonly Color32[] shadowPixels = new Color32[320 * 180];

    Texture2D background;
    Texture2D silhouette;
    Texture2D glow;
    Texture2D ring;
    Texture2D leaf;
    Texture2D fluff;
    Texture2D rainbow;
    Camera view;
    Font font;

    string status = "Kinect starting...";
    bool settings;
    bool mouseDown;
    bool capturing;
    float captureAt;
    float lastMask = -100f;
    Vector2 mouse;
    Rect viewport;

    bool IExhibitionInputTarget.MousePractice
    {
        get => mousePractice;
        set => mousePractice = value;
    }

    void Awake()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 60;

        view = Camera.main;
        if (view == null)
        {
            view = new GameObject("Spring Environment Camera").AddComponent<Camera>();
            view.tag = "MainCamera";
        }
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(.72f, .9f, 1f);

        background = Resources.Load<Texture2D>("Magic/SpringPlateau")
            ?? Resources.Load<Texture2D>("Magic/PlateauDay");
        silhouette = new Texture2D(320, 180, TextureFormat.RGBA32, false)
        {
            name = "Environment silhouette",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        glow = CreateGlowTexture(64);
        ring = CreateRingTexture(96);
        leaf = CreateLeafTexture(64, 36);
        fluff = CreateFluffTexture(64);
        rainbow = CreateRainbowTexture(512, 256);
        font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Meiryo", "Arial" }, 24);

        TryStartAutomatedCapture();

        // A scene only needs this controller.  Adding the input last lets its
        // OnEnable find this IExhibitionInputTarget immediately.
        if (GetComponent<ExhibitionKinectInput>() == null)
            gameObject.AddComponent<ExhibitionKinectInput>();
    }

    void TryStartAutomatedCapture()
    {
        const string prefix = "-environmentCapture=";
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (!argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            StartCoroutine(CaptureAndQuit(Path.GetFullPath(argument.Substring(prefix.Length).Trim('"'))));
            break;
        }
    }

    static IEnumerator CaptureAndQuit(string path)
    {
        yield return new WaitForSecondsRealtime(2f);
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(path);
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit(0);
    }

    public void SetStatus(string value)
    {
        status = value ?? string.Empty;
    }

    public void SetFrame(byte[] pixels, int width, int height, GunInput leftInput, GunInput rightInput, byte[] mask)
    {
        if (mask != null && mask.Length == shadowPixels.Length * 4)
        {
            lastMask = Time.unscaledTime;
            for (int y = 0; y < 180; y++)
            {
                for (int x = 0; x < 320; x++)
                {
                    int sourceX = 319 - x;
                    int source = (y * 320 + sourceX) * 4;
                    bool inside = mask[source + 3] > 100;
                    shadowPixels[(179 - y) * 320 + x] = new Color32(0, 0, 0, (byte)(inside ? 66 : 0));
                }
            }
            silhouette.SetPixels32(shadowPixels);
            silhouette.Apply(false);
        }

        if (!mousePractice)
        {
            Receive(0, leftInput);
            Receive(1, rightInput);
        }
    }

    void Receive(int index, GunInput input)
    {
        if (!input.Tracked)
        {
            hands[index].Active = false;
            return;
        }

        Vector2 point = new Vector2(1f - input.AimX, input.AimY);
        double elapsed = input.Timestamp - seen[index];
        Vector2 velocity = elapsed > .01d && elapsed < .3d
            ? Vector2.ClampMagnitude((point - previous[index]) / (float)elapsed, 2f)
            : Vector2.zero;
        hands[index] = new FieldHand
        {
            Active = point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f,
            Open = input.Pose,
            X = point.x,
            Y = point.y,
            VX = velocity.x,
            VY = velocity.y
        };
        previous[index] = point;
        seen[index] = input.Timestamp;
        frameAt[index] = Time.unscaledTime;
    }

    void Update()
    {
        float screenWidth = Mathf.Max(1f, Screen.width);
        float screenHeight = Mathf.Max(1f, Screen.height);
        const float aspect = 16f / 9f;
        viewport = screenWidth / screenHeight > aspect
            ? new Rect((screenWidth - screenHeight * aspect) * .5f, 0f, screenHeight * aspect, screenHeight)
            : new Rect(0f, (screenHeight - screenWidth / aspect) * .5f, screenWidth, screenWidth / aspect);

        for (int i = 0; i < hands.Length; i++)
            if (Time.unscaledTime - frameAt[i] > .3f) hands[i].Active = false;

        if (mousePractice)
        {
            Vector2 point = new Vector2(
                (mouse.x - viewport.x) / Mathf.Max(1f, viewport.width),
                (mouse.y - viewport.y) / Mathf.Max(1f, viewport.height));
            Vector2 velocity = Vector2.ClampMagnitude(
                (point - previous[0]) / Mathf.Max(.01f, Time.unscaledDeltaTime), 2f);
            bool inside = point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
            hands[0] = new FieldHand
            {
                Active = mouseDown && inside,
                Open = true,
                X = point.x,
                Y = point.y,
                VX = velocity.x,
                VY = velocity.y
            };
            previous[0] = point;
            hands[1] = default(FieldHand);
        }

        world.Step(Time.unscaledDeltaTime, hands[0], hands[1]);

        if (capturing && Time.unscaledTime >= captureAt)
        {
            ExhibitionKinectInput sensor = GetComponent<ExhibitionKinectInput>();
            if (sensor != null) sensor.CaptureBackground();
            capturing = false;
            world.Reset();
        }
    }

    void OnGUI()
    {
        mouse = Event.current.mousePosition;
        HandleGuiInput(Event.current);

        if (background != null) GUI.DrawTexture(viewport, background, ScaleMode.StretchToFill);
        else Fill(viewport, new Color(.61f, .86f, .96f));

        DrawRainbow();
        DrawTreeAndLeaves();
        DrawRiver();
        DrawDandelions();

        if (showSilhouette && Time.unscaledTime - lastMask < .35f)
            GUI.DrawTexture(viewport, silhouette);

        for (int i = 0; i < hands.Length; i++)
        {
            if (!hands[i].Active) continue;
            FieldHand hand = hands[i];
            Color color = hand.Open
                ? new Color(1f, .94f, .64f, .72f)
                : new Color(.45f, .78f, 1f, .4f);
            Halo(hand.X, hand.Y, .055f, color);
        }

        float uiScale = Mathf.Max(.7f, Screen.height / 1000f);
        Rect title = R(.025f, .018f, .32f, .06f);
        Fill(title, new Color(.05f, .16f, .10f, .24f));
        GUI.Label(title, "  ふれる春の台地", Label(Mathf.RoundToInt(27f * uiScale), new Color(1f, .98f, .82f)));
        Rect hint = R(.12f, .91f, .76f, .057f);
        Fill(hint, new Color(.04f, .14f, .10f, .31f));
        GUI.Label(hint, HintText(), Label(Mathf.RoundToInt(20f * uiScale), new Color(1f, .99f, .9f), TextAnchor.MiddleCenter));

        if (capturing)
        {
            GUI.Label(R(.1f, .35f, .8f, .2f),
                "背景を記録します。カメラの外へ移動してください\n" + Mathf.CeilToInt(captureAt - Time.unscaledTime),
                Label(Mathf.RoundToInt(30f * uiScale), Color.white, TextAnchor.MiddleCenter));
        }

        if (settings) DrawSettings(uiScale);
    }

    void HandleGuiInput(Event current)
    {
        if (current.type == EventType.MouseDown && current.button == 0 && !settings)
            mouseDown = true;
        if (current.type == EventType.MouseUp && current.button == 0)
            mouseDown = false;
        if (current.type != EventType.KeyDown) return;

        if (current.keyCode == KeyCode.F1)
        {
            settings = !settings;
            mouseDown = false;
            current.Use();
        }
        else if (current.keyCode == KeyCode.R)
        {
            world.Reset();
            current.Use();
        }
    }

    string HintText()
    {
        if (world.NewFluff > 0 || world.Fluff.Count > 10) return "タンポポにそっと風を送ると、綿毛が旅立ちます";
        if (world.RiverEnergy > .18f) return "川をなぞる速さで、水の流れとしぶきが変わります";
        if (world.RainbowReveal > .08f) return "空へ手を伸ばすと、虹がゆっくり目を覚まします";
        if (world.TreeEnergy > .08f) return "木の葉に触れると、春の風がこぼれます";
        return mousePractice
            ? "左ドラッグで、木・川・空・タンポポに触れてみよう"
            : "手のひらをひらいて、自然に触れてみよう";
    }

    void DrawSettings(float scale)
    {
        Rect panel = new Rect(Screen.width - 455f * scale, 25f * scale, 425f * scale, 545f * scale);
        Fill(panel, new Color(.025f, .075f, .095f, .95f));
        float x = panel.x + 24f * scale;
        float y = panel.y + 20f * scale;
        float width = panel.width - 48f * scale;

        GUI.Label(new Rect(x, y, width, 35f * scale), "環境展示設定 / F1で閉じる", Label(Mathf.RoundToInt(20f * scale), Color.white));
        y += 45f * scale;
        GUI.Label(new Rect(x, y, width, 62f * scale), status, Label(Mathf.RoundToInt(14f * scale), new Color(.65f, .88f, 1f)));
        y += 70f * scale;

        bool previousMousePractice = mousePractice;
        mousePractice = GUI.Toggle(new Rect(x, y, width, 30f * scale), mousePractice, " マウス操作（左ドラッグ）");
        if (mousePractice != previousMousePractice)
        {
            hands[0] = hands[1] = default(FieldHand);
            mouseDown = false;
        }
        y += 35f * scale;
        showSilhouette = GUI.Toggle(new Rect(x, y, width, 30f * scale), showSilhouette, " 自分のシルエットを表示");
        y += 44f * scale;

        GUI.Label(new Rect(x, y, width, 62f * scale),
            "木、川、空、タンポポに手のひらを重ねます。\n動かす速さによって自然の反応が変わります。",
            Label(Mathf.RoundToInt(15f * scale), Color.white));
        y += 72f * scale;

        ExhibitionKinectInput sensor = GetComponent<ExhibitionKinectInput>();
        if (sensor != null)
        {
            GUI.Label(new Rect(x, y, width, 25f * scale),
                "シルエット距離 " + (sensor.silhouetteMaxDepth / 1000f).ToString("F1") + "m",
                Label(Mathf.RoundToInt(16f * scale), Color.white));
            y += 30f * scale;
            sensor.silhouetteMaxDepth = Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(x, y, width, 20f * scale), sensor.silhouetteMaxDepth, 1500f, 5000f));
            y += 38f * scale;
            if (GUI.Button(new Rect(x, y, width, 36f * scale), "背景を登録する（5秒後）"))
            {
                capturing = true;
                captureAt = Time.unscaledTime + 5f;
                settings = false;
            }
            y += 46f * scale;
        }

        if (GUI.Button(new Rect(x, y, width, 36f * scale), "自然の反応をリセット（R）"))
            world.Reset();
    }

    void DrawRainbow()
    {
        float reveal = Mathf.SmoothStep(0f, 1f, world.RainbowReveal);
        if (reveal <= .001f || rainbow == null) return;

        float centerX = Mathf.Clamp(world.RainbowCenterX, .62f, .79f);
        const float fullWidth = .58f;
        const float height = .35f;
        float visibleWidth = fullWidth * Mathf.Lerp(.16f, 1f, reveal);
        Rect destination = R(centerX - visibleWidth * .5f, .055f, visibleWidth, height);
        Rect source = new Rect(.5f - visibleWidth / fullWidth * .5f, 0f, visibleWidth / fullWidth, 1f);
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, .28f + reveal * .64f);
        GUI.DrawTextureWithTexCoords(destination, rainbow, source, true);
        GUI.color = old;
        Halo(centerX, .23f, .34f, new Color(1f, .92f, .64f, reveal * .055f));
    }

    void DrawTreeAndLeaves()
    {
        if (world.TreeEnergy > .001f)
        {
            float pulse = .93f + Mathf.Sin(world.Time * 2.1f) * .07f;
            float sway = world.TreeSway * .018f;
            Halo(SpringEnvironmentWorld.TreeCenterX, SpringEnvironmentWorld.TreeCenterY, .3f,
                new Color(.82f, 1f, .45f, world.TreeEnergy * .09f * pulse));
            Halo(SpringEnvironmentWorld.TreeCenterX - .075f + sway, SpringEnvironmentWorld.TreeCenterY - .09f, .12f,
                new Color(.92f, 1f, .54f, world.TreeEnergy * .055f));
            Halo(SpringEnvironmentWorld.TreeCenterX + .025f + sway * .72f, SpringEnvironmentWorld.TreeCenterY - .135f, .15f,
                new Color(.78f, 1f, .42f, world.TreeEnergy * .06f));
            Halo(SpringEnvironmentWorld.TreeCenterX + .085f + sway * .45f, SpringEnvironmentWorld.TreeCenterY - .04f, .11f,
                new Color(1f, .91f, .42f, world.TreeEnergy * .045f));
        }

        Color[] colors =
        {
            new Color(.64f, .86f, .18f),
            new Color(.95f, .76f, .15f),
            new Color(.4f, .72f, .16f),
            new Color(1f, .58f, .22f)
        };
        foreach (SpringEnvironmentWorld.LeafParticle particle in world.Leaves)
        {
            float fadeIn = Mathf.Clamp01(particle.Age / .18f);
            float fadeOut = Mathf.Clamp01((particle.Lifetime - particle.Age) / .7f);
            Color color = colors[Mathf.Abs(particle.Variant) % colors.Length];
            color.a = fadeIn * fadeOut * .9f;
            float width = particle.Size * 1.75f;
            float height = width * .58f * 16f / 9f;
            DrawRotated(leaf, R(particle.X - width * .5f, particle.Y - height * .5f, width, height), particle.Angle, color);
        }
    }

    void DrawRiver()
    {
        float energy = world.RiverEnergy;
        if (energy > .003f)
        {
            for (int row = 0; row < 5; row++)
            {
                for (int segment = 0; segment < 18; segment++)
                {
                    float x0 = Mathf.Lerp(SpringEnvironmentWorld.RiverLeft, SpringEnvironmentWorld.RiverRight, segment / 18f);
                    float x1 = Mathf.Lerp(SpringEnvironmentWorld.RiverLeft, SpringEnvironmentWorld.RiverRight, (segment + 1) / 18f);
                    float rowOffset = Mathf.Lerp(-.62f, .62f, row / 4f);
                    float baseY0 = SpringEnvironmentWorld.RiverCenterYAt(x0)
                        + SpringEnvironmentWorld.RiverHalfWidthAt(x0) * rowOffset;
                    float baseY1 = SpringEnvironmentWorld.RiverCenterYAt(x1)
                        + SpringEnvironmentWorld.RiverHalfWidthAt(x1) * rowOffset;
                    float phase0 = x0 * 28f - world.Time * (1.8f + world.RiverFlow * 1.4f) + row;
                    float phase1 = x1 * 28f - world.Time * (1.8f + world.RiverFlow * 1.4f) + row;
                    float y0 = baseY0 + Mathf.Sin(phase0) * .0045f;
                    float y1 = baseY1 + Mathf.Sin(phase1) * .0045f;
                    Line(Point(x0, y0), Point(x1, y1), 1.1f + energy * 1.3f,
                        new Color(.83f, .98f, 1f, .1f + energy * .26f));
                }
            }
        }

        foreach (SpringEnvironmentWorld.RippleParticle particle in world.Ripples)
        {
            float life = Mathf.Clamp01(particle.Age / particle.Lifetime);
            float width = particle.StartRadius * 2f + life * .09f;
            float height = width * (16f / 9f) * .3f;
            Color old = GUI.color;
            GUI.color = new Color(.8f, .98f, 1f, (1f - life) * particle.Strength * .8f);
            GUI.DrawTexture(R(particle.X - width * .5f, particle.Y - height * .5f, width, height), ring);
            GUI.color = old;
        }

        foreach (SpringEnvironmentWorld.SplashParticle particle in world.Splashes)
        {
            float fade = Mathf.Clamp01((particle.Lifetime - particle.Age) / .28f);
            float width = .007f + particle.Size * 1.3f;
            Halo(particle.X, particle.Y, width, new Color(.8f, .98f, 1f, fade * .9f));
            Vector2 point = Point(particle.X, particle.Y);
            Vector2 tail = point - new Vector2(particle.VX * viewport.width, particle.VY * viewport.height) * .055f;
            Line(tail, point, 1.2f, new Color(.82f, .98f, 1f, fade * .65f));
        }
    }

    void DrawDandelions()
    {
        foreach (SpringEnvironmentWorld.DandelionHead head in world.Dandelions)
        {
            float topX = head.X + head.Bend * .016f;
            float seedRatio = head.SeedsRemaining / (float)Mathf.Max(1, head.Capacity);
            float breathe = .045f + Mathf.Sin(world.Time * .9f + head.X * 19f) * .014f;
            Halo(topX, head.Y, .033f,
                new Color(1f, .94f, .56f, breathe + head.Glow * .28f));
            if (seedRatio > .02f)
                DrawCentered(fluff, topX, head.Y, .018f,
                    new Color(1f, .99f, .91f, .22f + seedRatio * .46f + head.Glow * .22f));
        }

        foreach (SpringEnvironmentWorld.FluffParticle particle in world.Fluff)
        {
            float fadeIn = Mathf.Clamp01(particle.Age / .2f);
            float fadeOut = Mathf.Clamp01((particle.Lifetime - particle.Age) / .8f);
            float alpha = fadeIn * fadeOut;
            float size = .014f + particle.Size * 1.3f;
            float turn = Mathf.Sin(world.Time * .7f + particle.Phase) * 18f;
            DrawRotated(fluff, PixelRect(particle.X, particle.Y, size), turn,
                new Color(1f, .99f, .9f, alpha * .92f));
            Vector2 point = Point(particle.X, particle.Y);
            Line(point, point + new Vector2(-2f, 7f), .8f, new Color(.8f, .72f, .48f, alpha * .55f));
        }
    }

    Rect R(float x, float y, float width, float height)
    {
        return new Rect(
            viewport.x + x * viewport.width,
            viewport.y + y * viewport.height,
            width * viewport.width,
            height * viewport.height);
    }

    Vector2 Point(float x, float y)
    {
        return new Vector2(viewport.x + x * viewport.width, viewport.y + y * viewport.height);
    }

    Rect PixelRect(float x, float y, float normalizedWidth)
    {
        float normalizedHeight = normalizedWidth * 16f / 9f;
        return R(x - normalizedWidth * .5f, y - normalizedHeight * .5f, normalizedWidth, normalizedHeight);
    }

    void DrawCentered(Texture2D texture, float x, float y, float normalizedWidth, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(PixelRect(x, y, normalizedWidth), texture);
        GUI.color = old;
    }

    void Halo(float x, float y, float normalizedWidth, Color color)
    {
        DrawCentered(glow, x, y, normalizedWidth, color);
    }

    static void Fill(Rect rectangle, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rectangle, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void DrawRotated(Texture2D texture, Rect rectangle, float degrees, Color color)
    {
        Matrix4x4 matrix = GUI.matrix;
        Color old = GUI.color;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(degrees, rectangle.center);
        GUI.DrawTexture(rectangle, texture);
        GUI.matrix = matrix;
        GUI.color = old;
    }

    static void Line(Vector2 start, Vector2 end, float width, Color color)
    {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude < .01f) return;
        Matrix4x4 matrix = GUI.matrix;
        Color old = GUI.color;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
        GUI.DrawTexture(new Rect(start.x, start.y - width * .5f, delta.magnitude, width), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = old;
    }

    GUIStyle Label(int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = size,
            alignment = alignment,
            wordWrap = true
        };
        style.normal.textColor = color;
        return style;
    }

    static Texture2D CreateGlowTexture(int size)
    {
        Texture2D texture = NewGeneratedTexture("Soft glow", size, size);
        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * .5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = new Vector2((x - center) / center, (y - center) / center).magnitude;
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Pow(Mathf.Clamp01(1f - distance), 2f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static Texture2D CreateRingTexture(int size)
    {
        Texture2D texture = NewGeneratedTexture("Water ring", size, size);
        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * .5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = new Vector2((x - center) / center, (y - center) / center).magnitude;
                float alpha = Mathf.Clamp01(1f - Mathf.Abs(distance - .72f) / .11f);
                alpha *= Mathf.Clamp01((1f - distance) * 4f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static Texture2D CreateLeafTexture(int width, int height)
    {
        Texture2D texture = NewGeneratedTexture("Falling leaf", width, height);
        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = ((x + .5f) / width) * 2f - 1f;
                float ny = ((y + .5f) / height) * 2f - 1f;
                float shape = Mathf.Pow(Mathf.Abs(nx), 1.35f) + ny * ny;
                float alpha = Mathf.Clamp01((1f - shape) * 8f);
                if (Mathf.Abs(ny) < .075f && Mathf.Abs(nx) < .82f) alpha = Mathf.Max(alpha, .92f);
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static Texture2D CreateFluffTexture(int size)
    {
        Texture2D texture = NewGeneratedTexture("Dandelion fluff", size, size);
        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * .5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - center) / center;
                float ny = (y - center) / center;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx);
                float spokes = Mathf.Abs(Mathf.Sin(angle * 6f));
                float spokeAlpha = radius >= .1f && radius <= .9f
                    ? Mathf.Clamp01((.105f / Mathf.Max(.25f, radius) - spokes) * 18f)
                    : 0f;
                float crown = Mathf.Clamp01(1f - Mathf.Abs(radius - .82f) / .12f);
                float centerAlpha = Mathf.Clamp01(1f - radius / .18f);
                float alpha = Mathf.Max(centerAlpha, Mathf.Max(spokeAlpha * .78f, crown * .72f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static Texture2D CreateRainbowTexture(int width, int height)
    {
        Texture2D texture = NewGeneratedTexture("Spring rainbow", width, height);
        Color32[] pixels = new Color32[width * height];
        Vector2 center = new Vector2((width - 1) * .5f, height * 1.08f);
        float inner = height * .52f;
        float outer = height * .88f;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                if (distance < inner || distance > outer) continue;
                float band = Mathf.InverseLerp(inner, outer, distance);
                Color color = RainbowColor(band);
                float edge = Mathf.Clamp01((distance - inner) / 5f)
                    * Mathf.Clamp01((outer - distance) / 5f);
                color.a = edge * .82f;
                pixels[y * width + x] = color;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static Color RainbowColor(float value)
    {
        Color[] colors =
        {
            new Color(.54f, .35f, .92f),
            new Color(.34f, .48f, 1f),
            new Color(.25f, .78f, 1f),
            new Color(.36f, .9f, .48f),
            new Color(1f, .92f, .3f),
            new Color(1f, .58f, .25f),
            new Color(1f, .35f, .3f)
        };
        float scaled = Mathf.Clamp01(value) * (colors.Length - 1);
        int first = Mathf.Min(colors.Length - 2, Mathf.FloorToInt(scaled));
        return Color.Lerp(colors[first], colors[first + 1], scaled - first);
    }

    static Texture2D NewGeneratedTexture(string name, int width, int height)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused) return;
        mouseDown = false;
        hands[0].Active = hands[1].Active = false;
    }

    void OnDestroy()
    {
        if (silhouette != null) Destroy(silhouette);
        if (glow != null) Destroy(glow);
        if (ring != null) Destroy(ring);
        if (leaf != null) Destroy(leaf);
        if (fluff != null) Destroy(fluff);
        if (rainbow != null) Destroy(rainbow);
        if (font != null) Destroy(font);
    }
}
