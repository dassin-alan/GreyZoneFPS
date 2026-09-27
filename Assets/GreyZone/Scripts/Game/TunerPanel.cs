// GreyZone - F1 tuning panel (SPEC 4.11): every stop/friction/recoil parameter is live and
// persisted to PlayerPrefs under "gz.*". IMGUI only, cached value labels, no per frame garbage.
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public class TunerPanel : MonoBehaviour
    {
        public static bool IsOpen;
        private static TunerPanel _instance;

        private GUIStyle _title;
        private GUIStyle _label;
        private GUIStyle _value;
        private GUIStyle _panel;
        private GUIStyle _btn;
        private bool _ready;

        private readonly FloatLabel[] _fl = new FloatLabel[24];
        private BoolLabel _autoStop = new BoolLabel();
        private PInt _enemies = new PInt();
        private PInt _fps = new PInt();
        private bool _dirty;

        public static void Toggle()
        {
            IsOpen = !IsOpen;
            if (!IsOpen && _instance != null) _instance.Flush();
        }

        /// <summary>Applies the quality tier to the engine + live camera (pause panel also uses this).</summary>
        public static void ApplyQualityNow()
        {
            GameBootstrap.ApplyEngineSettings();
            GameBootstrap.ApplyCameraQuality(Camera.main);
            if (FxSystem.I != null) FxSystem.I.ApplyQualityLimits();
        }

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            GameSettings.Save();
        }

        private void BuildStyles()
        {
            _ready = true;
            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 18;
            _title.fontStyle = FontStyle.Bold;
            _title.normal.textColor = Color.white;

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 13;
            _label.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);

            _value = new GUIStyle(GUI.skin.label);
            _value.fontSize = 13;
            _value.alignment = TextAnchor.MiddleRight;
            _value.normal.textColor = new Color(1f, 0.9f, 0.5f, 1f);

            _panel = new GUIStyle();
            _panel.normal.background = ProcAssets.White;

            _btn = new GUIStyle(GUI.skin.button);
            _btn.fontSize = 13;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            if (!_ready) BuildStyles();
            if (GameDirector.I == null) return;

            float s = Mathf.Clamp(Screen.height / 1080f, 0.55f, 1.6f);
            float w = 430f * s;
            float rowH = 22f * s;

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(new Rect(8f * s, 8f * s, w, Screen.height - 16f * s), ProcAssets.White);
            GUI.color = prev;

            float x = 18f * s;
            float y = 14f * s;
            float sx = x + 158f * s;
            float sw = 168f * s;
            float vx = sx + sw + 6f * s;
            float vw = 70f * s;

            GUI.Label(new Rect(x, y, w - 20f * s, 26f * s), "TUNING  (F1 to close)", _title);
            y += 30f * s;

            MoveConfig m = GameSettings.Move;
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 0, "Max Speed", ref m.MaxSpeed, 3f, 12f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 1, "Accel", ref m.Accel, 0.5f, 20f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 2, "Friction", ref m.Friction, 0.2f, 20f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 3, "Stop Speed", ref m.StopSpeed, 0.2f, 8f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 4, "Jump Impulse", ref m.JumpImpulse, 2f, 12f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 5, "Gravity", ref m.Gravity, 5f, 40f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 6, "Air Accel", ref m.AirAccel, 0f, 30f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 7, "Air Max Wish", ref m.AirMaxWishSpeed, 0f, 4f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 8, "Walk Mult", ref m.WalkMultiplier, 0.1f, 1f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 9, "Duck Mult", ref m.DuckMultiplier, 0.1f, 1f);
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 10, "Max Speed Cap", ref m.MaxSpeedCap, 4f, 16f);
            GameSettings.Move = m;

            float recoil = GameSettings.RecoilScale;
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 11, "Recoil Scale", ref recoil, 0f, 2f);
            GameSettings.RecoilScale = recoil;

            float spread = GameSettings.SpreadScale;
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 12, "Spread Scale", ref spread, 0f, 2f);
            GameSettings.SpreadScale = spread;

            float sens = GameSettings.MouseSens;
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 13, "Mouse Sens", ref sens, 0.2f, 12f);
            GameSettings.MouseSens = sens;

            float tsens = GameSettings.TouchSens;
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 14, "Touch Sens", ref tsens, 0.03f, 1.2f);
            GameSettings.TouchSens = tsens;

            float fov = GameSettings.Fov;
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 15, "FOV", ref fov, 55f, 110f);
            GameSettings.Fov = fov;

            float aim = GameSettings.AimAssist;
            y = Slider(x, y, sx, sw, vx, vw, rowH, s, 16, "Aim Assist %", ref aim, 0f, 1f);
            GameSettings.AimAssist = aim;

            // ---- toggles / segments
            y += 6f * s;
            if (GUI.Button(new Rect(x, y, (w - 30f * s), rowH + 4f * s), _autoStop.Get("Fire Auto-Stop: ", GameSettings.AutoStop), _btn))
            {
                GameSettings.AutoStop = !GameSettings.AutoStop;
                _dirty = true;
            }
            y += rowH + 10f * s;

            GUI.Label(new Rect(x, y, 140f * s, rowH), "Bot Difficulty", _label);
            int d = Seg(x + 158f * s, y, sw + 80f * s, rowH + 2f * s, s, _diff, (int)GameSettings.Difficulty);
            if (d >= 0)
            {
                GameSettings.Difficulty = d == 0 ? BotDifficulty.Easy : (d == 1 ? BotDifficulty.Normal : BotDifficulty.Hard);
                _dirty = true;
            }
            y += rowH + 6f * s;

            GUI.Label(new Rect(x, y, 140f * s, rowH), "Quality", _label);
            int q = Seg(x + 158f * s, y, sw + 80f * s, rowH + 2f * s, s, _qual, (int)GameSettings.Quality);
            if (q >= 0)
            {
                GameSettings.Quality = q == 0 ? QualityTier.Low : (q == 1 ? QualityTier.Medium : QualityTier.High);
                GameSettings.ApplyQuality();
                ApplyQualityNow();
                _dirty = true;
            }
            y += rowH + 6f * s;

            GUI.Label(new Rect(x, y, 140f * s, rowH), "My Team", _label);
            int t = Seg(x + 158f * s, y, sw + 80f * s, rowH + 2f * s, s, _team, GameSettings.PlayerTeam == Team.Terrorists ? 0 : 1);
            if (t >= 0)
            {
                GameSettings.PlayerTeam = t == 0 ? Team.Terrorists : Team.CTs;
                _dirty = true;
            }
            y += rowH + 8f * s;

            GUI.Label(new Rect(x, y, 140f * s, rowH), "Enemies (next round)", _label);
            if (GUI.Button(new Rect(x + 158f * s, y, 30f * s, rowH), "-", _btn))
            {
                GameSettings.EnemyCount = Mathf.Clamp(GameSettings.EnemyCount - 1, 2, 5);
                _dirty = true;
            }
            if (GUI.Button(new Rect(x + 192f * s, y, 30f * s, rowH), "+", _btn))
            {
                GameSettings.EnemyCount = Mathf.Clamp(GameSettings.EnemyCount + 1, 2, 5);
                _dirty = true;
            }
            GUI.Label(new Rect(x + 228f * s, y, 90f * s, rowH), _enemies.Get("", GameSettings.EnemyCount), _label);
            y += rowH + 8f * s;

            if (GUI.Button(new Rect(x, y, (w - 30f * s) * 0.48f, rowH + 6f * s), "RESTART MATCH", _btn))
                GameDirector.I.RestartMatch();
            if (GUI.Button(new Rect(x + (w - 30f * s) * 0.52f, y, (w - 30f * s) * 0.48f, rowH + 6f * s), "RESET DEFAULTS", _btn))
            {
                GameSettings.ResetDefaults();
                GameSettings.ApplyQuality();
                ApplyQualityNow();
                _dirty = false;
            }
            y += rowH + 12f * s;

            GUI.Label(new Rect(x, y, w - 30f * s, rowH), _fps.Get("", Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime))), _label);

            Event e = Event.current;
            if (e != null && e.type == EventType.MouseUp) Flush();
        }

        private static readonly string[] _diff = new string[] { "Easy", "Normal", "Hard" };
        private static readonly string[] _qual = new string[] { "Low", "Med", "High" };
        private static readonly string[] _team = new string[] { "T", "CT" };

        private float Slider(float x, float y, float sx, float sw, float vx, float vw, float rowH, float s,
                             int labelIx, string name, ref float value, float min, float max)
        {
            GUI.Label(new Rect(x, y, 156f * s, rowH), name, _label);
            float v = GUI.HorizontalSlider(new Rect(sx, y + rowH * 0.35f, sw, rowH), value, min, max);
            if (!Mathf.Approximately(v, value))
            {
                value = v;
                _dirty = true;
            }
            GUI.Label(new Rect(vx, y, vw, rowH), _fl[labelIx].Get(v, 2), _value);
            return y + rowH + 2f * s;
        }

        private int Seg(float x, float y, float w, float h, float s, string[] labels, int selected)
        {
            int picked = -1;
            float bw = w / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                bool on = i == selected;
                Color prev = GUI.color;
                GUI.color = on ? new Color(0.4f, 0.8f, 0.45f, 1f) : Color.white;
                if (GUI.Button(new Rect(x + bw * i, y, bw - 3f * s, h), labels[i], _btn) && !on) picked = i;
                GUI.color = prev;
            }
            return picked;
        }
    }
}
