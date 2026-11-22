using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

// ===========================================================
//  GreenHud.cs  -  only two things:
//   1) on foot : health bar + armor bar (bottom left)
//   2) in a car: speed, gear, engine bar, throttle light, tyres
//  Pictures: scripts\GreenHud\*.png   (text is drawn by the game)
// ===========================================================
public class GreenHud : Script
{
    // ===== draw space 1280x720 =====
    const float DW = 1280f, DH = 720f;
    static float SW { get { return GTA.UI.Screen.ScaledWidth; } }
    static float NX(float x) { return ((SW - DW) / 2f + x) / SW; }

    Dictionary<string, GTA.UI.CustomSprite> sprs =
        new Dictionary<string, GTA.UI.CustomSprite>();
    bool sprOff;
    bool reported;
    float throttle;

    public GreenHud()
    {
        Tick += OnTick;
    }

    // ---------- draw helpers ----------
    static void PR(float x, float y, float w, float h,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.DRAW_RECT, NX(x + w / 2f), (y + h / 2f) / DH,
            w / SW, h / DH, r, g, b, a);
    }

    // align: 0 left, 1 center, 2 right
    static void PT(string s, float x, float y, float scale,
        int r, int g, int b, int align)
    {
        Function.Call(Hash.SET_TEXT_FONT, 4);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        float nx = NX(x);
        if (align == 1) Function.Call(Hash.SET_TEXT_CENTRE, true);
        else if (align == 2)
        {
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, 2);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, nx);
            nx = 0f;
        }
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, nx, y / DH, 0);
    }

    // ---------- pictures (looked up in a few folders) ----------
    static readonly string[] Dirs = {
        "scripts\\GreenHud\\", "scripts/GreenHud/",
        "scripts\\GreenHud\\hud\\", "scripts/GreenHud/hud/"
    };
    Dictionary<string, string> found = new Dictionary<string, string>();
    List<string> missing = new List<string>();

    string Find(string name)
    {
        string p;
        if (found.TryGetValue(name, out p)) return p;
        foreach (string d in Dirs)
        {
            try
            {
                if (File.Exists(d + name)) { found[name] = d + name; return d + name; }
            }
            catch (Exception) { }
        }
        found[name] = null;
        missing.Add(name);
        return null;
    }

    // PNG (always drawn on top of rects and text)
    bool Spr(string name, float x, float y, float w, float h)
    {
        if (sprOff) return false;
        try
        {
            string path = Find(name);
            if (path == null) return false;
            GTA.UI.CustomSprite s;
            if (!sprs.TryGetValue(path, out s))
            {
                s = new GTA.UI.CustomSprite(path,
                    new System.Drawing.SizeF(w, h),
                    new System.Drawing.PointF(0f, 0f));
                sprs[path] = s;
            }
            s.Size = new System.Drawing.SizeF(w * DW / SW, h);
            s.Position = new System.Drawing.PointF(NX(x) * DW, y);
            s.Draw();
            return true;
        }
        catch (Exception ex)
        {
            sprOff = true;
            WriteLog("Sprite error: " + ex.Message);
            return false;
        }
    }

    void WriteLog(string extra)
    {
        try
        {
            File.WriteAllText("scripts\\GreenHud.log",
                (extra ?? "") + "\r\nmissing pictures (" + missing.Count + "):\r\n"
                + string.Join("\r\n", missing.ToArray()) + "\r\n");
        }
        catch (Exception) { }
    }

    // panel PNGs have transparent windows (holes); the dark fill of the
    // windows is drawn here so text / bars show through them
    void Panel(string spr, float x, float y, float w, float h, float m,
        float[][] holes)
    {
        foreach (float[] hl in holes)
            PR(x + hl[0], y + hl[1], hl[2], hl[3], 8, 12, 10, 255);
        if (!Spr(spr, x - m, y - m, w + 2f * m, h + 2f * m))
        {
            PR(x - 1.5f, y - 1.5f, w + 3f, h + 3f, 50, 230, 70, 255);
            PR(x, y, w, h, 8, 12, 10, 255);          // no PNG: plain box
        }
    }

    // smooth horizontal bar (dark track + gradient fill + shine)
    static void HBar(float x, float y, float w, float h, float frac,
        int r1, int g1, int b1, int r2, int g2, int b2)
    {
        if (frac < 0f) frac = 0f; if (frac > 1f) frac = 1f;
        PR(x - 1f, y - 1f, w + 2f, h + 2f, 40, 70, 52, 255);   // rim
        PR(x, y, w, h, 14, 22, 18, 255);                       // track
        const int N = 28;
        float sw = w / N;
        for (int i = 0; i < N; i++)
        {
            float t = i / (float)(N - 1);
            float lit = frac * N - i;
            if (lit <= 0f) break;
            if (lit > 1f) lit = 1f;
            int r = (int)(r1 + (r2 - r1) * t);
            int g = (int)(g1 + (g2 - g1) * t);
            int b = (int)(b1 + (b2 - b1) * t);
            PR(x + i * sw, y, sw * lit + 0.6f, h, r, g, b, 255);
        }
        PR(x, y, w * frac, h * 0.32f, 255, 255, 255, 38);       // shine
    }

    // ===========================================================
    void OnTick(object sender, EventArgs e)
    {
        Ped p = Game.Player.Character;
        if (p == null || !p.Exists() || p.IsDead) return;

        float dt = Game.LastFrameTime;
        if (dt <= 0f || dt > 0.25f) dt = 0.033f;

        // after 4 s: tell what is missing (scripts\GreenHud.log)
        if (!reported && Game.GameTime > 4000)
        {
            reported = true;
            Find("st_panel.png"); Find("v_left.png"); Find("car.png");
            WriteLog(sprOff ? "sprites switched off" : "");
            if (missing.Count > 0)
                GTA.UI.Screen.ShowSubtitle(
                    "GreenHud pictures not found. Put the GreenHud folder in scripts", 6000);
        }

        DrawBars(p);
        if (p.IsInVehicle()) DrawVehicle(p, p.CurrentVehicle, dt);
    }

    // ---------- on foot: health + armor (bottom left) ----------
    void DrawBars(Ped p)
    {
        float px = 18f, py = 648f, pw = 250f, ph = 50f;

        float hp = (p.Health - 100f) / (float)(p.MaxHealth - 100);
        if (hp < 0f) hp = 0f; if (hp > 1f) hp = 1f;
        float ar = p.Armor / 100f;
        if (ar < 0f) ar = 0f; if (ar > 1f) ar = 1f;

        Panel("st_panel.png", px, py, pw, ph, 6f,
            new float[][] { new float[] { 38f, 6f, 202f, 38f } });

        float bx = px + 42f, bw = 194f;

        // health (red when low)
        if (hp < 0.25f) HBar(bx, py + 8f, bw, 14f, hp, 200, 40, 40, 255, 90, 80);
        else HBar(bx, py + 8f, bw, 14f, hp, 30, 190, 50, 110, 255, 90);
        PT((int)Math.Round(hp * 100f) + "%", bx + bw - 5f, py + 6f, 0.27f,
            255, 255, 255, 2);

        // armor
        HBar(bx, py + 27f, bw, 14f, ar, 0, 190, 200, 40, 255, 235);
        PT((int)Math.Round(ar * 100f) + "%", bx + bw - 5f, py + 25f, 0.27f,
            255, 255, 255, 2);

        Spr("ic_heart.png", px + 19f, py + 8f, 14f, 14f);
        Spr("ic_shield.png", px + 19f, py + 27f, 14f, 14f);
    }

    // ---------- in a vehicle (bottom centre) ----------
    // chevron made of stripes, dark until the accelerator lights it up
    // from the bottom to the top ('lit' 0..1). k = size (1 = 50x118)
    void Chevron(float x, float y, float k, bool flip, float lit)
    {
        const float W = 50f, H = 118f;
        const int N = 36;
        float sh = H / N;
        for (int i = 0; i < N; i++)
        {
            float ym = (i + 0.5f) * sh;
            float xl, xr;
            if (ym <= 59f) { xl = 30f * ym / 59f; xr = 16f + 32f * ym / 59f; }
            else
            {
                float kk = H - ym;
                xl = 30f * kk / 59f; xr = 16f + 32f * kk / 59f;
            }
            float t = 1f - ym / H;                        // 0 bottom .. 1 top
            float a = (lit - t) * 14f + 0.5f;
            if (a < 0f) a = 0f; if (a > 1f) a = 1f;

            float gx = flip ? W - xr : xl;
            float px = x + gx * k, pw = (xr - xl) * k;
            float py = y + i * sh * k, ph = sh * k + 0.7f;

            PR(px, py, pw, ph, 18, 56, 32, 255);          // dark base
            if (a > 0f)
            {
                PR(px - 1.2f, py, pw + 2.4f, ph, 70, 255, 110, (int)(60 * a));
                bool head = lit > 0.02f && Math.Abs(t - lit) < 0.06f;
                int r = head ? 215 : 60 + (int)(100 * t);
                int g = head ? 255 : 190 + (int)(65 * t);
                int b = head ? 225 : 100;
                PR(px, py, pw, ph, r, g, b, (int)(255 * a));
            }
        }
    }

    // gear guessed from the speed (the game's own gear is not reliable)
    Dictionary<int, float> topSpeed = new Dictionary<int, float>();
    static readonly float[] GearAt = { 0.10f, 0.22f, 0.36f, 0.52f, 0.72f };
    int shownGear = 1;
    int gearVeh;

    int EstimateGear(Vehicle v)
    {
        int key = v.Model.Hash;
        float sp = v.Speed;
        float top;
        if (!topSpeed.TryGetValue(key, out top)) top = 45f;   // ~160 km/h
        if (sp > top) top = sp;
        topSpeed[key] = top;

        if (gearVeh != v.Handle) { gearVeh = v.Handle; shownGear = 1; }
        float ratio = sp / top;
        if (sp < 1f) { shownGear = 1; return shownGear; }

        while (shownGear < 6 && ratio > GearAt[shownGear - 1] + 0.02f)
            shownGear++;
        while (shownGear > 1 && ratio < GearAt[shownGear - 2] - 0.02f)
            shownGear--;
        return shownGear;
    }

    void DrawVehicle(Ped p, Vehicle v, float dt)
    {
        if (v == null || !v.Exists()) return;

        // throttle (smoothed so the light runs up / falls back)
        float target = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 71);
        if (target < 0f) target = 0f; if (target > 1f) target = 1f;
        float k = target > throttle ? 5f : 3.5f;
        throttle += (target - throttle) * Math.Min(1f, k * dt);

        // layout: left panel | centre hex | right panel
        float x0 = 401f;
        float lw = 190f, lh = 72f, cw = 110f, ch = 88f;
        float ly = 698f - lh, cyy = 698f - ch;
        float lx = x0, cx = x0 + 184f, rx = cx + cw - 6f;

        // ---------- left: speed + engine health ----------
        Panel("v_left.png", lx, ly, lw, lh, 6f,
            new float[][] { new float[] { 52f, 8f, 124f, 56f } });
        int kmh = (int)Math.Round(v.Speed * 3.6f);
        PT(kmh.ToString(), lx + 114f, ly + 7f, 0.85f, 70, 235, 100, 1);
        PT("KM/H", lx + 172f, ly + 36f, 0.26f, 235, 245, 238, 2);
        float eng = Function.Call<float>(Hash.GET_VEHICLE_ENGINE_HEALTH, v) / 1000f;
        if (eng < 0f) eng = 0f; if (eng > 1f) eng = 1f;
        if (eng < 0.3f) HBar(lx + 72f, ly + 50f, 96f, 6f, eng, 200, 40, 40, 255, 90, 80);
        else HBar(lx + 72f, ly + 50f, 96f, 6f, eng, 30, 190, 50, 110, 255, 90);
        Spr("ic_bolt.png", lx + 56f, ly + 47f, 12f, 12f);

        // ---------- right: gear + throttle ----------
        Panel("v_right.png", rx, ly, lw, lh, 6f,
            new float[][] { new float[] { 14f, 8f, 124f, 56f } });
        int gear = EstimateGear(v);
        string cur;
        bool reversing = Vector3.Dot(v.Velocity, v.ForwardVector) < -0.5f;
        if (reversing) cur = "R";
        else if (gear <= 1 && kmh < 2) cur = "N";
        else cur = gear.ToString();
        PT(cur, rx + 76f, ly + 4f, 1.0f, 255, 255, 255, 1);
        if (!reversing && kmh >= 2)
        {
            if (gear > 1)
                PT((gear - 1).ToString(), rx + 34f, ly + 22f, 0.5f, 120, 140, 125, 1);
            if (gear < 6)
                PT((gear + 1).ToString(), rx + 118f, ly + 22f, 0.5f, 120, 140, 125, 1);
        }
        HBar(rx + 22f, ly + 50f, 108f, 6f, throttle, 30, 160, 60, 150, 255, 100);
        Spr("ic_speedo.png", rx + lw - 34f, ly + 12f, 14f, 14f);

        // ---------- centre: chevrons + car ----------
        Panel("v_center.png", cx, cyy, cw, ch, 6f,
            new float[][] { new float[] { 14f, 16f, 82f, 60f },
                            new float[] { 33f, 2.5f, 44f, 9f } });
        PT("GEAR", cx + 55f, cyy + 1.5f, 0.2f, 235, 245, 238, 1);

        float kc = 0.5f;
        float chY = cyy + 46f - 118f * kc / 2f;
        Chevron(cx + 18f, chY, kc, true, throttle);          // <
        Chevron(cx + 67f, chY, kc, false, throttle);         // >

        int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v);
        if (cls != 14 && cls != 15 && cls != 16)    // not boat / heli / plane
        {
            float carW = 26f, carH = 48f, sc = carW / 54f;
            float carX = cx + 55f - carW / 2f, carY = cyy + 22f;
            Spr("car.png", carX, carY, carW, carH);

            // tyre index: 0 front left, 1 front right, 4 rear left, 5 rear right
            int[] idx = { 0, 1, 4, 5 };
            float[] wx = { 1f, 45f, 1f, 45f };
            float[] wy = { 14f, 14f, 64f, 64f };
            for (int i = 0; i < 4; i++)
            {
                if (Function.Call<bool>(Hash.IS_VEHICLE_TYRE_BURST,
                    v, idx[i], false))
                    Spr("tire_red.png", carX + (wx[i] - 3f) * sc,
                        carY + (wy[i] - 3f) * sc, 14f * sc, 28f * sc);
            }
        }
    }
}
