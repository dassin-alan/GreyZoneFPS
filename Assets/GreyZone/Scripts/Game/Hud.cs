// GreyZone - the whole interface is IMGUI: crosshair, hit marker, vitals, radar, kill feed,
// buy menu, pause panel, banners and the scope overlay. Every number/string is cached so a
// steady state frame allocates nothing.
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public class Hud : MonoBehaviour
    {
        private GUIStyle _text;
        private GUIStyle _textRight;
        private GUIStyle _small;
        private GUIStyle _smallRight;
        private GUIStyle _big;
        private GUIStyle _center;
        private GUIStyle _centerBig;
        private GUIStyle _panel;
        private GUIStyle _row;
        private GUIStyle _rowDim;
        private bool _ready;

        private PInt _hpLabel = new PInt();
        private PIntBits _armorBits = new PIntBits();
        private PInt _money = new PInt();
        private PInt2 _moneyBuy = new PInt2();
        private PInt _scoreT = new PInt();
        private PInt _scoreCT = new PInt();
        private PInt2 _scoreLine = new PInt2();
        private PInt _roundLabel = new PInt();
        private IntLabel _roundIx = new IntLabel();
        private PInt _mag = new PInt();
        private PInt _reserve = new PInt();
        private PInt _enemies = new PInt();
        private BoolLabel _touchToggle = new BoolLabel();
        private BoolLabel _handToggle = new BoolLabel();
        private BoolLabel _fpsToggle = new BoolLabel();
        private IntLabel _fpsLabel = new IntLabel();
        private TimeLabel _clock = new TimeLabel();
        private TimeLabel _bomb = new TimeLabel();
        private TimeLabel _freeze = new TimeLabel();
        private TimeLabel _buyLeft = new TimeLabel();
        private PStr _freezeLine = new PStr();
        private PStr _bombLine = new PStr();
        private PStr2 _buyLine = new PStr2();
        private PStr2 _armorLine = new PStr2();

        private float _fpsAcc;
        private int _fpsFrames;
        private int _fps;

        private string[] _buyLabels;
        private string _armorBuy;
        private string _helmetBuy;
        private string _kitBuy;

        private static readonly Color CTeam = new Color(0.32f, 0.62f, 0.88f, 1f);
        private static readonly Color CEnemy = new Color(0.88f, 0.30f, 0.28f, 1f);
        private static readonly Color CBomb = new Color(0.92f, 0.76f, 0.25f, 1f);
        private static readonly Color CSiteCol = new Color(0.72f, 0.36f, 0.32f, 1f);
        private static readonly Color CGood = new Color(0.45f, 0.85f, 0.45f, 1f);
        private static readonly Color CDim = new Color(0.55f, 0.55f, 0.55f, 1f);

        private void BuildStyles()
        {
            _ready = true;

            _text = new GUIStyle(GUI.skin.label);
            _text.fontSize = 16;
            _text.normal.textColor = Color.white;

            _textRight = new GUIStyle(_text);
            _textRight.alignment = TextAnchor.MiddleRight;

            _small = new GUIStyle(GUI.skin.label);
            _small.fontSize = 13;
            _small.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);

            _smallRight = new GUIStyle(_small);
            _smallRight.alignment = TextAnchor.MiddleRight;

            _big = new GUIStyle(GUI.skin.label);
            _big.fontSize = 30;
            _big.fontStyle = FontStyle.Bold;
            _big.normal.textColor = Color.white;

            _center = new GUIStyle(GUI.skin.label);
            _center.fontSize = 16;
            _center.alignment = TextAnchor.MiddleCenter;
            _center.normal.textColor = Color.white;

            _centerBig = new GUIStyle(GUI.skin.label);
            _centerBig.fontSize = 32;
            _centerBig.fontStyle = FontStyle.Bold;
            _centerBig.alignment = TextAnchor.MiddleCenter;
            _centerBig.normal.textColor = Color.white;

            _panel = new GUIStyle();
            _panel.normal.background = ProcAssets.White;

            _row = new GUIStyle(GUI.skin.button);
            _row.fontSize = 15;
            _row.alignment = TextAnchor.MiddleLeft;
            _row.padding.left = 10;

            _rowDim = new GUIStyle(_row);
            _rowDim.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 1f);

            // buy labels never change: build them once
            WeaponDef[] all = WeaponDef.All();
            _buyLabels = new string[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                WeaponDef d = all[i];
                _buyLabels[i] = (i + 1) + ".  " + (d != null ? d.DisplayName : "?") + "   $" + (d != null ? d.Price : 0);
            }
            _armorBuy = "8.  Kevlar Armor   $" + Economy.ArmorPrice;
            _helmetBuy = "9.  Helmet   $" + Economy.HelmetPrice;
            _kitBuy = "0.  Defuse Kit   $" + Economy.DefuseKitPrice;
        }

        private void OnGUI()
        {
            GameDirector gd = GameDirector.I;
            if (gd == null) return;
            if (!_ready) BuildStyles();

            float s = Mathf.Clamp(Screen.height / 1080f, 0.5f, 2.6f);
            float w = Screen.width;
            float h = Screen.height;

            TickFps();

            if (!gd.Paused)
            {
                DrawCrosshair(gd, s, w, h);
                DrawVitals(gd, s, w, h);
                DrawTimers(gd, s, w, h);
                DrawRadar(gd, s);
                DrawKillFeed(gd, s, w);
                DrawBanner(gd, s, w, h);
                DrawUseProgress(gd, s, w, h);
                if (gd.Player != null && gd.Player.Scoped) DrawScope(s, w, h);
                if (gd.Player != null && !gd.Player.Alive) DrawDead(s, w, h);
                if (gd.BuyMenuOpen) DrawBuyMenu(gd, s, h);
                if (GameSettings.TouchMode && TouchControls.I != null) TouchControls.I.Draw();
            }
            else
            {
                DrawPause(gd, s, w, h);
            }

            if (GameSettings.ShowFps)
                Label(new Rect(w - 96f * s, 8f * s, 86f * s, 20f * s), _fpsLabel.Get(_fps), _smallRight, CDim);
        }

        // ------------------------------------------------------------------ helpers
        private void Rect2(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, ProcAssets.White);
            GUI.color = prev;
        }

        private void Label(Rect r, string text, GUIStyle st, Color c)
        {
            Color prev = st.normal.textColor;
            st.normal.textColor = c;
            GUI.Label(r, text, st);
            st.normal.textColor = prev;
        }

        private void TickFps()
        {
            _fpsAcc += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsAcc >= 0.5f)
            {
                _fps = Mathf.RoundToInt(_fpsFrames / _fpsAcc);
                _fpsAcc = 0f;
                _fpsFrames = 0;
            }
        }

        // ------------------------------------------------------------------ crosshair
        private void DrawCrosshair(GameDirector gd, float s, float w, float h)
        {
            PlayerActor p = gd.Player;
            if (p == null || p.Scoped) return;

            float spread = 3f;
            if (p.Def != null)
            {
                float sp = WeaponLogic.ComputeSpreadDeg(p.Def, p.SpeedRatio01, p.Move.OnGround, p.Move.Ducked) * GameSettings.SpreadScale;
                spread = 3f + sp * 5.5f;
            }
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float len = 7f * s;
            float th = Mathf.Max(1f, 1.5f * s);
            float gap = spread * s;
            Color c = new Color(0.15f, 1f, 0.35f, 0.9f);

            Rect2(new Rect(cx - gap - len, cy - th * 0.5f, len, th), c);
            Rect2(new Rect(cx + gap, cy - th * 0.5f, len, th), c);
            Rect2(new Rect(cx - th * 0.5f, cy - gap - len, th, len), c);
            Rect2(new Rect(cx - th * 0.5f, cy + gap, th, len), c);

            float hm = GameClock.Now - gd.HitMarkerTime;
            if (hm >= 0f && hm < 0.28f)
            {
                float a = 1f - hm / 0.28f;
                Color hc = gd.HitMarkerHead ? new Color(1f, 0.35f, 0.3f, a) : new Color(1f, 1f, 1f, a);
                float d = 11f * s;
                float l2 = 9f * s;
                float t2 = Mathf.Max(1.5f, 2f * s);
                Rect2(new Rect(cx - d - l2, cy - d - l2, l2, t2), hc);
                Rect2(new Rect(cx - d - l2, cy - d - l2, t2, l2), hc);
                Rect2(new Rect(cx + d, cy + d - t2, l2, t2), hc);
                Rect2(new Rect(cx + d - t2, cy + d, t2, l2), hc);
            }
        }

        // ------------------------------------------------------------------ vitals
        private void DrawVitals(GameDirector gd, float s, float w, float h)
        {
            PlayerActor p = gd.Player;
            if (p == null) return;

            float bw = 200f * s;
            float bx = 26f * s;
            float by = h - 108f * s;

            Rect2(new Rect(bx - 6f * s, by - 6f * s, bw + 12f * s, 92f * s), new Color(0f, 0f, 0f, 0.45f));
            float hpf = Mathf.Clamp01(p.Health / (float)Ballistics.MaxHealth);
            Rect2(new Rect(bx, by, bw, 26f * s), new Color(0.2f, 0.55f, 0.25f, 0.85f));
            Rect2(new Rect(bx, by, bw * hpf, 26f * s), hpf > 0.35f ? new Color(0.22f, 0.62f, 0.28f, 0.95f) : new Color(0.7f, 0.2f, 0.18f, 0.95f));

            Label(new Rect(bx + 6f * s, by, bw, 26f * s), _hpLabel.Get("HP ", p.Health), _text, Color.white);

            if (p.Armor > 0)
                Label(new Rect(bx, by + 30f * s, bw, 22f * s),
                    _armorBits.Get("ARMOR ", p.Armor, (p.HasHelmet ? 2 : 0) | (p.HasKit ? 1 : 0), "  KIT", "  HELM"),
                    _small, new Color(0.6f, 0.8f, 1f));
            else
                Label(new Rect(bx, by + 30f * s, bw, 22f * s), "NO ARMOR  [8]", _small, CDim);

            Label(new Rect(bx, by + 52f * s, bw, 22f * s), _money.Get("$", gd.Money), _small, new Color(0.45f, 0.95f, 0.45f));

            if (p.HasBomb)
            {
                bool inSite = gd.Map != null && gd.Map.SiteOf(p.FeetPos) != null;
                Label(new Rect(bx, by + 72f * s, bw * 1.6f, 22f * s),
                    inSite ? "BOMB - hold E to plant" : "BOMB - carry to site A / B", _small, CBomb);
            }

            WeaponDef def = p.Def;
            if (def == null) return;
            WeaponState st = p.States[p.Slot];
            float ax = w - 236f * s;
            Rect2(new Rect(ax - 6f * s, by - 6f * s, 236f * s, 62f * s), new Color(0f, 0f, 0f, 0.45f));
            Label(new Rect(ax, by - 2f * s, 230f * s, 22f * s), def.DisplayName, _smallRight, Color.white);
            Label(new Rect(ax, by + 14f * s, 150f * s, 34f * s), _mag.Get("", st.AmmoInMag), _big, Color.white);
            Label(new Rect(ax + 90f * s, by + 20f * s, 140f * s, 24f * s), _reserve.Get("/ ", st.ReserveAmmo), _textRight, CDim);
            if (st.Reloading)
            {
                float need = Mathf.Max(0.01f, def.ReloadTime);
                float left = st.ReloadEndTime - GameClock.Now;
                Rect2(new Rect(ax, by + 50f * s, 230f * s, 6f * s),
                    new Color(0.9f, 0.7f, 0.2f, 0.9f * Mathf.Clamp01(1f - left / need)));
                Label(new Rect(ax, by + 38f * s, 230f * s, 18f * s), "RELOADING", _smallRight, new Color(0.95f, 0.8f, 0.3f));
            }
        }

        // ------------------------------------------------------------------ timers
        private void DrawTimers(GameDirector gd, float s, float w, float h)
        {
            float cx = w * 0.5f;
            float top = 14f * s;

            Rect2(new Rect(cx - 132f * s, top, 264f * s, 46f * s), new Color(0f, 0f, 0f, 0.45f));
            Label(new Rect(cx - 132f * s, top, 132f * s, 30f * s), _scoreT.Get("T ", gd.ScoreT), _center, CTeam);
            Label(new Rect(cx, top, 132f * s, 30f * s), _scoreCT.Get("CT ", gd.ScoreCT), _center, CEnemy);
            Label(new Rect(cx - 132f * s, top + 26f * s, 264f * s, 18f * s), _roundLabel.Get("ROUND ", gd.Round), _center, CDim);

            if (gd.Phase == MatchPhase.Freeze)
            {
                Label(new Rect(cx - 220f * s, h * 0.26f, 440f * s, 48f * s), _freezeLine.Get("FREEZE  ", _freeze.Get(gd.PhaseTimer)), _centerBig, new Color(1f, 0.9f, 0.5f));
                Label(new Rect(cx - 220f * s, h * 0.26f + 48f * s, 440f * s, 24f * s), "BUY PHASE - press B", _center, CDim);
            }
            else if (gd.Phase == MatchPhase.Live)
            {
                if (gd.BombPlanted)
                {
                    float blink = gd.BombTimer < 10f ? (0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8f))) : 1f;
                    Label(new Rect(cx - 220f * s, h * 0.085f, 440f * s, 44f * s),
                        _bombLine.Get("BOMB  ", _bomb.Get(gd.BombTimer)), _centerBig, new Color(1f, 0.25f, 0.2f, blink));
                }
                Color clockCol = gd.RoundClock < 30f ? new Color(1f, 0.85f, 0.4f) : Color.white;
                Label(new Rect(cx - 200f * s, h * 0.16f, 400f * s, 30f * s), _clock.Get(gd.RoundClock), _centerBig, clockCol);

                if (gd.BuyWindow)
                {
                    float left = gd.RoundClock - (MatchRules.RoundTime - MatchRules.BuyTime);
                    Label(new Rect(cx - 220f * s, h * 0.16f + 30f * s, 440f * s, 22f * s),
                        _buyLine.Get("BUY ", _buyLeft.Get(left), "s  [B]"), _center, CGood);
                }
            }
        }

        // ------------------------------------------------------------------ radar
        private void DrawRadar(GameDirector gd, float s)
        {
            PlayerActor p = gd.Player;
            if (p == null) return;
            float size = 96f * s;
            Rect box = new Rect(14f * s, 14f * s, size, size);
            Rect2(box, new Color(0f, 0f, 0f, 0.45f));
            Rect2(new Rect(box.x, box.y, box.width, 2f * s), new Color(1f, 1f, 1f, 0.25f));

            Vector2 c = new Vector2(box.x + size * 0.5f, box.y + size * 0.5f);
            float rad = size * 0.5f - 5f * s;
            const float range = 42f;
            float vy = p.ViewYawDeg * Mathf.Deg2Rad;
            float vx = Mathf.Sin(vy), vz = Mathf.Cos(vy);
            Vector3 feet = p.FeetPos;

            if (gd.Map != null && gd.Map.SiteA != null)
            {
                DrawSite(c, rad, range, vx, vz, gd.Map.SiteA.Area.center, feet, "A", s);
                DrawSite(c, rad, range, vx, vz, gd.Map.SiteB.Area.center, feet, "B", s);
            }

            for (int i = 0; i < gd.ActorCount; i++)
            {
                Actor a = gd.GetActor(i);
                if (a == null || !a.Alive || a == p) continue;
                bool enemy = a.Team != p.Team;
                if (enemy && GameClock.Now - a.SpottedTime > 2f) continue;
                Vector3 rel = a.FeetPos - feet;
                Vector2 blip = RadarPoint(c, rad, range, vx, vz, rel);
                float d = 7f * s;
                Rect2(new Rect(blip.x - d * 0.5f, blip.y - d * 0.5f, d, d), enemy ? CEnemy : CTeam);
            }

            if (gd.BombPlanted)
            {
                Vector3 rel = gd.BombPos - feet;
                Vector2 blip = RadarPoint(c, rad, range, vx, vz, rel);
                float blink = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
                float d = 10f * s;
                Rect2(new Rect(blip.x - d * 0.5f, blip.y - d * 0.5f, d, d), new Color(CBomb.r, CBomb.g, CBomb.b, blink));
            }

            Rect2(new Rect(c.x - 3f * s, c.y - 3f * s, 6f * s, 6f * s), new Color(1f, 1f, 1f, 0.9f));
            Label(new Rect(box.x - 4f * s, box.y + box.height + 2f * s, size + 8f * s, 20f * s),
                _scoreLine.Get("", gd.ScoreT, " : ", gd.ScoreCT, "  T:CT"), _small, CDim);
        }

        private void DrawSite(Vector2 c, float rad, float range, float vx, float vz, Vector3 sitePos, Vector3 playerPos, string name, float s)
        {
            Vector3 rel = sitePos - playerPos;
            Vector2 p = RadarPoint(c, rad, range, vx, vz, rel);
            Label(new Rect(p.x - 8f * s, p.y - 10f * s, 16f * s, 20f * s), name, _small, CSiteCol);
        }

        private static Vector2 RadarPoint(Vector2 c, float rad, float range, float vx, float vz, Vector3 rel)
        {
            float rx = rel.x / range;
            float rz = rel.z / range;
            float sx = rx * vz - rz * vx;
            float sy = -(rx * vx + rz * vz);
            float len = Mathf.Sqrt(sx * sx + sy * sy);
            if (len > 1f) { sx /= len; sy /= len; }
            return new Vector2(c.x + sx * rad, c.y + sy * rad);
        }

        // ------------------------------------------------------------------ kill feed
        private void DrawKillFeed(GameDirector gd, float s, float w)
        {
            float y = 16f * s;
            float rowH = 22f * s;
            for (int i = 0; i < gd.FeedCount; i++)
            {
                FeedEntry e = gd.Feed[i];
                float age = GameClock.Now - e.Time;
                if (age > 5f) continue;
                float a = Mathf.Clamp01((5f - age) / 1.2f);
                Rect2(new Rect(w - 360f * s, y, 346f * s, rowH), new Color(0f, 0f, 0f, 0.35f * a));
                Label(new Rect(w - 356f * s, y, 338f * s, rowH), e.Text, _textRight,
                    e.KillerIsPlayer ? new Color(0.6f, 0.95f, 0.6f, a) : new Color(1f, 1f, 1f, a));
                y += rowH + 2f * s;
            }
        }

        // ------------------------------------------------------------------ banners / progress
        private void DrawBanner(GameDirector gd, float s, float w, float h)
        {
            if (gd.BannerTime <= 0f || gd.Banner == null || gd.Banner.Length == 0) return;
            float a = Mathf.Clamp01(gd.BannerTime);
            Rect2(new Rect(0f, h * 0.34f, w, 56f * s), new Color(0f, 0f, 0f, 0.55f * a));
            Label(new Rect(0f, h * 0.34f, w, 56f * s), gd.Banner, _centerBig, new Color(1f, 1f, 1f, a));

            if (gd.Phase == MatchPhase.MatchOver)
            {
                if (GUI.Button(new Rect(w * 0.5f - 120f * s, h * 0.44f, 240f * s, 48f * s), "RESTART MATCH", _row))
                    gd.RestartMatch();
            }
        }

        private void DrawUseProgress(GameDirector gd, float s, float w, float h)
        {
            PlayerActor p = gd.Player;
            if (p == null || !p.UseActive) return;
            float bw = 300f * s;
            float bx = w * 0.5f - bw * 0.5f;
            float by = h * 0.68f;
            Rect2(new Rect(bx - 3f * s, by - 3f * s, bw + 6f * s, 28f * s), new Color(0f, 0f, 0f, 0.6f));
            Rect2(new Rect(bx, by, bw * Mathf.Clamp01(p.UseProgress01), 22f * s), p.UseIsDefuse ? CTeam : CBomb);
            Label(new Rect(bx, by, bw, 22f * s), p.UseIsDefuse ? "DEFUSING" : "PLANTING", _center, Color.white);

            if (gd.BombPlanted && gd.Player != null && gd.Player.Team == Team.Terrorists)
                Label(new Rect(bx, by - 26f * s, bw, 22f * s), "BOMB IS PLANTED", _center, CEnemy);
        }

        private void DrawScope(float s, float w, float h)
        {
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float r = h * 0.42f;
            Color black = new Color(0f, 0f, 0f, 0.97f);
            Rect2(new Rect(0f, 0f, cx - r, h), black);
            Rect2(new Rect(cx + r, 0f, cx - r + 1f, h), black);
            Rect2(new Rect(cx - r, 0f, r * 2f, cy - r), black);
            Rect2(new Rect(cx - r, cy + r, r * 2f, cy - r + 1f), black);

            Color line = new Color(0.05f, 0.05f, 0.05f, 0.9f);
            Rect2(new Rect(cx - 1f * s, cy - r, 2f * s, r * 2f), line);
            Rect2(new Rect(cx - r, cy - 1f * s, r * 2f, 2f * s), line);
            Rect2(new Rect(cx - 3f * s, cy - 3f * s, 6f * s, 6f * s), new Color(0.1f, 0.1f, 0.1f, 0.95f));
            Rect2(new Rect(cx - r, cy - r * 0.01f, r * 2f, 2f * s), new Color(0.05f, 0.05f, 0.05f, 0.5f));
        }

        private void DrawDead(float s, float w, float h)
        {
            Rect2(new Rect(0f, 0f, w, h), new Color(0.4f, 0f, 0f, 0.16f));
            Label(new Rect(0f, h * 0.42f, w, 44f * s), "ELIMINATED", _centerBig, new Color(1f, 0.75f, 0.7f));
            Label(new Rect(0f, h * 0.42f + 46f * s, w, 24f * s), "free look enabled - respawn next round", _center, CDim);
        }

        // ------------------------------------------------------------------ buy menu
        private void DrawBuyMenu(GameDirector gd, float s, float h)
        {
            float pw = 390f * s;
            float px = 40f * s;
            float py = h * 0.18f;
            Rect2(new Rect(px - 10f * s, py - 46f * s, pw + 20f * s, 470f * s), new Color(0f, 0f, 0f, 0.72f));
            Label(new Rect(px, py - 42f * s, pw, 34f * s), _moneyBuy.Get("BUY   $", gd.Money, "", 0, ""), _big, new Color(0.6f, 1f, 0.6f));

            WeaponDef[] all = WeaponDef.All();
            float rowH = 30f * s;
            float y = py;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponDef def = all[i];
                if (def == null) continue;
                bool owned = gd.Player.Slots[0] == def || gd.Player.Slots[1] == def;
                bool afford = Economy.CanAfford(gd.Money, def.Price);
                if (!owned && afford)
                {
                    if (GUI.Button(new Rect(px, y, pw, rowH), _buyLabels[i], _row)) gd.TryBuyWeapon(def);
                }
                else
                {
                    Label(new Rect(px, y, pw, rowH), _buyLabels[i], _rowDim, owned ? CDim : new Color(0.45f, 0.45f, 0.45f));
                }
                y += rowH;
            }

            y += 8f * s;
            bool armorOwned = gd.Player.Armor > 0;
            if (armorOwned) Label(new Rect(px, y, pw, rowH), _armorBuy, _rowDim, CDim);
            else if (GUI.Button(new Rect(px, y, pw, rowH), _armorBuy, _row)) gd.TryBuyArmor();
            y += rowH;

            if (gd.Player.HasHelmet) Label(new Rect(px, y, pw, rowH), _helmetBuy, _rowDim, CDim);
            else if (GUI.Button(new Rect(px, y, pw, rowH), _helmetBuy, _row)) gd.TryBuyHelmet();
            y += rowH;

            if (gd.Player.Team == Team.CTs)
            {
                if (gd.Player.HasKit) Label(new Rect(px, y, pw, rowH), _kitBuy, _rowDim, CDim);
                else if (GUI.Button(new Rect(px, y, pw, rowH), _kitBuy, _row)) gd.TryBuyKit();
                y += rowH;
            }

            Label(new Rect(px, y + 4f * s, pw, 20f * s), "click a row or press the number key   [B] closes", _small, CDim);

            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                bool used = false;
                if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha7)
                {
                    int idx = e.keyCode - KeyCode.Alpha1;
                    if (idx < all.Length) { gd.TryBuyWeapon(all[idx]); used = true; }
                }
                else if (e.keyCode == KeyCode.Alpha8) { gd.TryBuyArmor(); used = true; }
                else if (e.keyCode == KeyCode.Alpha9) { gd.TryBuyHelmet(); used = true; }
                else if (e.keyCode == KeyCode.Alpha0) { gd.TryBuyKit(); used = true; }
                if (used) e.Use();
            }
        }

        // ------------------------------------------------------------------ pause
        private void DrawPause(GameDirector gd, float s, float w, float h)
        {
            Rect2(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.7f));
            float pw = 430f * s;
            float ph = 486f * s;
            float px = w * 0.5f - pw * 0.5f;
            float py = h * 0.5f - ph * 0.5f;
            Rect2(new Rect(px, py, pw, ph), new Color(0.09f, 0.09f, 0.1f, 0.95f));

            float iw = pw - 32f * s;
            float y = py + 14f * s;
            float rowH = 34f * s;
            Label(new Rect(px, y, pw, 34f * s), "PAUSED", _centerBig, Color.white);
            y += 46f * s;

            if (GUI.Button(new Rect(px + 16f * s, y, iw, rowH), "RESUME", _row)) gd.TogglePause();
            y += rowH + 6f * s;
            if (GUI.Button(new Rect(px + 16f * s, y, iw, rowH), "RESTART MATCH", _row))
                gd.RestartMatch();
            y += rowH + 12f * s;

            Label(new Rect(px + 16f * s, y, iw, 20f * s), "QUALITY (applies immediately)", _small, CDim);
            y += 22f * s;
            int q = SegRow(px + 16f * s, y, iw, rowH, _qLabels, (int)GameSettings.Quality, s);
            if (q >= 0)
            {
                GameSettings.Quality = q == 0 ? QualityTier.Low : (q == 1 ? QualityTier.Medium : QualityTier.High);
                GameSettings.ApplyQuality();
                GameSettings.SaveOptions();
                TunerPanel.ApplyQualityNow();
            }
            y += rowH + 10f * s;

            Label(new Rect(px + 16f * s, y, iw, 20f * s), "BOT DIFFICULTY", _small, CDim);
            y += 22f * s;
            int d = SegRow(px + 16f * s, y, iw, rowH, _dLabels, (int)GameSettings.Difficulty, s);
            if (d >= 0)
            {
                GameSettings.Difficulty = d == 0 ? BotDifficulty.Easy : (d == 1 ? BotDifficulty.Normal : BotDifficulty.Hard);
                GameSettings.SaveOptions();
            }
            y += rowH + 10f * s;

            Label(new Rect(px + 16f * s, y, iw, 20f * s), "MY TEAM (applied next round)", _small, CDim);
            y += 22f * s;
            int tix = GameSettings.PlayerTeam == Team.Terrorists ? 0 : 1;
            int t = SegRow(px + 16f * s, y, iw * 0.56f, rowH, _tLabels, tix, s);
            if (t >= 0)
            {
                GameSettings.PlayerTeam = t == 0 ? Team.Terrorists : Team.CTs;
                GameSettings.SaveOptions();
            }
            if (GUI.Button(new Rect(px + 16f * s + iw * 0.60f, y, iw * 0.18f, rowH), "-", _row))
                ClampEnemies(-1);
            if (GUI.Button(new Rect(px + 16f * s + iw * 0.82f, y, iw * 0.18f, rowH), "+", _row))
                ClampEnemies(1);
            Label(new Rect(px + 16f * s + iw * 0.36f, y, iw * 0.24f, rowH), _enemies.Get("BOTS ", GameSettings.EnemyCount), _small, Color.white);
            y += rowH + 10f * s;

            if (GUI.Button(new Rect(px + 16f * s, y, iw * 0.48f, rowH), _touchToggle.Get("TOUCH: ", GameSettings.ForceTouch), _row))
            {
                GameSettings.ForceTouch = !GameSettings.ForceTouch;
                GameSettings.SaveOptions();
            }
            if (GUI.Button(new Rect(px + 16f * s + iw * 0.52f, y, iw * 0.48f, rowH), _handToggle.Get("LEFT HAND: ", GameSettings.LeftHanded), _row))
            {
                GameSettings.LeftHanded = !GameSettings.LeftHanded;
                GameSettings.SaveOptions();
            }
            y += rowH + 8f * s;

            if (GUI.Button(new Rect(px + 16f * s, y, iw * 0.48f, rowH), "F1 TUNER", _row)) TunerPanel.Toggle();
            if (GUI.Button(new Rect(px + 16f * s + iw * 0.52f, y, iw * 0.48f, rowH),
                _fpsToggle.Get("FPS: ", GameSettings.ShowFps), _row))
            {
                GameSettings.ShowFps = !GameSettings.ShowFps;
                GameSettings.SaveOptions();
            }
            y += rowH + 8f * s;

            if (GUI.Button(new Rect(px + 16f * s, y, iw, rowH), "QUIT", _row)) Application.Quit();
            y += rowH + 8f * s;
            Label(new Rect(px + 16f * s, y, iw, 20f * s), "F1 = tuning panel   ESC = resume", _small, CDim);
        }

        private static readonly string[] _qLabels = new string[] { "LOW", "MED", "HIGH" };
        private static readonly string[] _dLabels = new string[] { "EASY", "NORMAL", "HARD" };
        private static readonly string[] _tLabels = new string[] { "T", "CT" };

        private void ClampEnemies(int d)
        {
            GameSettings.EnemyCount = Mathf.Clamp(GameSettings.EnemyCount + d, 2, 5);
            GameSettings.SaveOptions();
        }

        /// <summary>Segmented control. Returns the newly picked index or -1.</summary>
        private int SegRow(float x, float y, float w, float h, string[] labels, int selected, float s)
        {
            int picked = -1;
            float bw = w / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                bool on = i == selected;
                Color prev = GUI.color;
                GUI.color = on ? new Color(0.4f, 0.8f, 0.45f, 1f) : Color.white;
                if (GUI.Button(new Rect(x + bw * i, y, bw - 3f * s, h), labels[i], _row) && !on) picked = i;
                GUI.color = prev;
            }
            return picked;
        }
    }
}
