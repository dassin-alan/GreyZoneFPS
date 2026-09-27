// GreyZone - hand rolled multi touch controls (Input.touches only, IMGUI for drawing).
// Hit tests are normalised rectangles / circles so the IMGUI layer never owns the input.
using UnityEngine;

namespace GreyZone.Game
{
    public class TouchControls : MonoBehaviour
    {
        public static TouchControls I;

        private const int MaxTouches = 10;
        private const float RefHeight = 1080f;

        private struct TouchInfo
        {
            public int Id;
            public Vector2 Pos;
            public bool Began;
            public bool Ended;
        }

        // ---- public state consumed by PlayerActor
        public float MoveForward;
        public float MoveRight;
        public float LookDeltaX;
        public float LookDeltaY;
        public bool JumpDown;
        public bool ReloadDown;
        public bool SwapDown;
        public bool ScopeDown;
        public bool FireHeld;
        public bool UseHeld;
        public bool CrouchHeld;
        public bool WalkHeld;
        public bool BuyDown;

        // ---- stick
        private int _stickFinger = -1;
        private Vector2 _stickCenter;
        private Vector2 _stickValue;
        private float _stickRadius;

        // ---- look
        private int _lookFinger = -1;
        private Vector2 _lookLast;
        private float _lookAccumX;
        private float _lookAccumY;

        // ---- buttons
        private const int BtnCount = 10;
        private const int BtnFire = 0, BtnScope = 1, BtnJump = 2, BtnCrouch = 3, BtnWalk = 4,
                          BtnReload = 5, BtnUse = 6, BtnBuy = 7, BtnSwap = 8, BtnPause = 9;
        private readonly Vector2[] _btnPos = new Vector2[BtnCount];
        private readonly float[] _btnRadius = new float[BtnCount];
        private readonly int[] _btnFinger = new int[BtnCount];
        private readonly bool[] _btnHeld = new bool[BtnCount];
        private readonly string[] _btnLabel = new string[BtnCount];

        private bool _walkToggle;
        private bool _crouchToggle;
        private readonly TouchInfo[] _touches = new TouchInfo[MaxTouches];
        private int _touchCount;
        private float _scale;
        private int _lastW, _lastH;
        private bool _simMouse;
        private int _simFingerId;
        private Vector2 _simPos;

        private GUIStyle _btnStyle;
        private bool _stylesReady;

        public Vector2 LookDelta
        {
            get { return new Vector2(_lookAccumX, _lookAccumY); }
        }

        public void Init()
        {
            I = this;
            _btnLabel[BtnFire] = "FIRE";
            _btnLabel[BtnScope] = "SCOPE";
            _btnLabel[BtnJump] = "JUMP";
            _btnLabel[BtnCrouch] = "DUCK";
            _btnLabel[BtnWalk] = "WALK";
            _btnLabel[BtnReload] = "RELOAD";
            _btnLabel[BtnUse] = "USE";
            _btnLabel[BtnBuy] = "BUY";
            _btnLabel[BtnSwap] = "SWAP";
            _btnLabel[BtnPause] = "II";
            for (int i = 0; i < BtnCount; i++) _btnFinger[i] = -1;
            Layout();
        }

        private void Layout()
        {
            _lastW = Screen.width;
            _lastH = Screen.height;
            _scale = Mathf.Clamp(_lastH / RefHeight, 0.55f, 2.6f);
            float s = _scale;
            float w = _lastW;
            float h = _lastH;
            bool mirror = GameSettings.LeftHanded;
            for (int i = 0; i < BtnCount; i++)
            {
                Vector2 p = Vector2.zero;
                float r = 44f * s;
                switch (i)
                {
                    case BtnFire: p = new Vector2(w - 170f * s, 210f * s); r = 105f * s; break;
                    case BtnScope: p = new Vector2(w - 175f * s, 430f * s); r = 52f * s; break;
                    case BtnJump: p = new Vector2(w - 380f * s, 120f * s); r = 52f * s; break;
                    case BtnCrouch: p = new Vector2(w - 380f * s, 260f * s); r = 50f * s; break;
                    case BtnWalk: p = new Vector2(w - 380f * s, 390f * s); r = 46f * s; break;
                    case BtnReload: p = new Vector2(w - 610f * s, 270f * s); r = 46f * s; break;
                    case BtnUse: p = new Vector2(w - 610f * s, 130f * s); r = 52f * s; break;
                    case BtnBuy: p = new Vector2(w - 610f * s, 410f * s); r = 42f * s; break;
                    case BtnSwap: p = new Vector2(w - 610f * s, 540f * s); r = 44f * s; break;
                    case BtnPause: p = new Vector2(58f * s, h - 58f * s); r = 38f * s; break;
                }
                if (mirror) p.x = w - p.x;
                _btnPos[i] = p;
                _btnRadius[i] = r;
            }
            _stickCenter = mirror ? new Vector2(w * 0.84f, h * 0.20f) : new Vector2(w * 0.16f, h * 0.20f);
            _stickRadius = 90f * s;
        }

        public void ClearState()
        {
            MoveForward = 0f;
            MoveRight = 0f;
            _lookAccumX = 0f;
            _lookAccumY = 0f;
            FireHeld = false;
            UseHeld = false;
            JumpDown = false;
            ReloadDown = false;
            SwapDown = false;
            ScopeDown = false;
            BuyDown = false;
            _stickFinger = -1;
            _lookFinger = -1;
            for (int i = 0; i < BtnCount; i++) { _btnFinger[i] = -1; _btnHeld[i] = false; }
        }

        // ------------------------------------------------------------------ input
        /// <summary>Reads the touch stream once per frame and updates all public state.</summary>
        public void Tick(float dt)
        {
            if (Screen.width != _lastW || Screen.height != _lastH) Layout();

            JumpDown = false;
            ReloadDown = false;
            SwapDown = false;
            ScopeDown = false;
            BuyDown = false;
            _lookAccumX = 0f;
            _lookAccumY = 0f;

            GatherTouches();

            for (int i = 0; i < _touchCount; i++)
            {
                TouchInfo t = _touches[i];
                if (t.Began)
                {
                    int btn = HitButton(t.Pos);
                    if (btn >= 0)
                    {
                        _btnFinger[btn] = t.Id;
                        if (!_btnHeld[btn]) PressButton(btn);
                        _btnHeld[btn] = true;
                        continue;
                    }
                    if (_stickFinger < 0 && InStickZone(t.Pos))
                    {
                        _stickFinger = t.Id;
                        _stickValue = Vector2.zero;
                        continue;
                    }
                    if (_lookFinger < 0)
                    {
                        _lookFinger = t.Id;
                        _lookLast = t.Pos;
                        continue;
                    }
                }
                if (t.Began || !t.Ended)
                {
                    if (t.Id == _stickFinger)
                    {
                        Vector2 d = t.Pos - _stickCenter;
                        float len = d.magnitude;
                        float dead = 0.12f;
                        float n = Mathf.Clamp01(len / _stickRadius);
                        if (n < dead) { _stickValue = Vector2.zero; }
                        else
                        {
                            Vector2 dir = d / Mathf.Max(0.001f, len);
                            float mag = (n - dead) / (1f - dead);
                            _stickValue = dir * Mathf.Clamp01(mag);
                        }
                        continue;
                    }
                    if (t.Id == _lookFinger)
                    {
                        Vector2 d = t.Pos - _lookLast;
                        _lookLast = t.Pos;
                        _lookAccumX += d.x * GameSettings.TouchSens;
                        _lookAccumY += -d.y * GameSettings.TouchSens;
                        continue;
                    }
                }
                if (t.Ended)
                {
                    if (t.Id == _stickFinger)
                    {
                        _stickFinger = -1;
                        _stickValue = Vector2.zero;
                    }
                    if (t.Id == _lookFinger) _lookFinger = -1;
                    for (int b = 0; b < BtnCount; b++)
                    {
                        if (_btnFinger[b] == t.Id)
                        {
                            _btnFinger[b] = -1;
                            _btnHeld[b] = false;
                            if (b == BtnFire) FireHeld = false;
                            if (b == BtnUse) UseHeld = false;
                        }
                    }
                }
            }

            // held state follows the finger id
            for (int b = 0; b < BtnCount; b++)
            {
                bool held = _btnFinger[b] >= 0;
                if (b == BtnFire) FireHeld = held;
                else if (b == BtnUse) UseHeld = held;
            }

            MoveForward = _stickValue.y;
            MoveRight = _stickValue.x;
        }

        private void PressButton(int b)
        {
            switch (b)
            {
                case BtnJump: JumpDown = true; break;
                case BtnReload: ReloadDown = true; break;
                case BtnSwap: SwapDown = true; break;
                case BtnScope: ScopeDown = true; break;
                case BtnCrouch:
                    _crouchToggle = !_crouchToggle;
                    CrouchHeld = _crouchToggle;
                    break;
                case BtnWalk:
                    _walkToggle = !_walkToggle;
                    WalkHeld = _walkToggle;
                    break;
                case BtnBuy: BuyDown = true; break;
                case BtnPause:
                    if (GameDirector.I != null) GameDirector.I.TogglePause();
                    break;
            }
        }

        private int HitButton(Vector2 p)
        {
            for (int i = 0; i < BtnCount; i++)
            {
                if (_btnFinger[i] >= 0) continue;
                float r = _btnRadius[i] * 1.14f;
                if ((p - _btnPos[i]).sqrMagnitude <= r * r) return i;
            }
            return -1;
        }

        private bool InStickZone(Vector2 p)
        {
            bool mirrored = GameSettings.LeftHanded;
            float x0 = mirrored ? Screen.width * 0.55f : 0f;
            float x1 = mirrored ? Screen.width : Screen.width * 0.45f;
            return p.x >= x0 && p.x <= x1 && p.y <= Screen.height * 0.5f;
        }

        private void GatherTouches()
        {
            _touchCount = 0;
            int tc = Input.touchCount;
            for (int i = 0; i < tc && _touchCount < MaxTouches; i++)
            {
                Touch t = Input.GetTouch(i);
                TouchInfo info = new TouchInfo();
                info.Id = t.fingerId;
                info.Pos = t.position;
                info.Began = t.phase == TouchPhase.Began;
                info.Ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                _touches[_touchCount++] = info;
            }

            // editor / desktop preview: the mouse acts as one finger when touch mode is forced
            if (tc == 0 && GameSettings.ForceTouch)
            {
                bool down = Input.GetMouseButton(0);
                if (down)
                {
                    Vector2 p = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                    TouchInfo info = new TouchInfo();
                    info.Id = 99;
                    info.Pos = p;
                    info.Began = !_simMouse || (p - _simPos).sqrMagnitude > 400f * 400f;
                    info.Ended = false;
                    _simMouse = true;
                    _simPos = p;
                    _touches[_touchCount++] = info;
                }
                else if (_simMouse)
                {
                    TouchInfo info = new TouchInfo();
                    info.Id = 99;
                    info.Pos = _simPos;
                    info.Began = false;
                    info.Ended = true;
                    _touches[_touchCount++] = info;
                    _simMouse = false;
                }
            }
        }

        // ------------------------------------------------------------------ drawing
        public void Draw()
        {
            if (!_stylesReady)
            {
                _btnStyle = new GUIStyle(GUI.skin.label);
                _btnStyle.alignment = TextAnchor.MiddleCenter;
                _btnStyle.fontSize = Mathf.RoundToInt(15f * _scale);
                _btnStyle.fontStyle = FontStyle.Bold;
                _stylesReady = true;
            }

            if (Screen.width != _lastW || Screen.height != _lastH) Layout();

            // stick
            DrawCircle(_stickCenter, _stickRadius, new Color(1f, 1f, 1f, 0.10f));
            DrawCircle(_stickCenter, _stickRadius * 0.42f, new Color(1f, 1f, 1f, 0.14f));
            DrawCircle(_stickCenter + _stickValue * _stickRadius * 0.75f, _stickRadius * 0.30f, new Color(0.9f, 0.95f, 1f, 0.35f));

            for (int i = 0; i < BtnCount; i++)
            {
                bool held = _btnFinger[i] >= 0 || (i == BtnCrouch && _crouchToggle) || (i == BtnWalk && _walkToggle);
                Color baseCol = held ? new Color(1f, 0.85f, 0.4f, 0.34f) : new Color(1f, 1f, 1f, 0.16f);
                DrawCircle(_btnPos[i], _btnRadius[i], baseCol);
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, held ? 0.95f : 0.75f);
                Rect r = TouchRect(new Vector2(_btnPos[i].x - _btnRadius[i], _btnPos[i].y - _btnRadius[i]),
                                   new Vector2(_btnRadius[i] * 2f, _btnRadius[i] * 2f));
                GUI.Label(r, _btnLabel[i], _btnStyle);
                GUI.color = prev;
            }
        }

        private void DrawCircle(Vector2 center, float radius, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(TouchRect(center - new Vector2(radius, radius), new Vector2(radius * 2f, radius * 2f)), ProcAssets.Dot);
            GUI.color = prev;
        }

        private static Rect TouchRect(Vector2 pos, Vector2 size)
        {
            return new Rect(pos.x, Screen.height - pos.y - size.y, size.x, size.y);
        }
    }
}
