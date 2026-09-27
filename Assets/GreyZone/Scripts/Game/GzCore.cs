// GreyZone - Unity lane shared helpers: layers, unit conversion, cached labels, tiny structs.
// Pure glue code: no allocation in steady state, no LINQ, C# 7.3 only.
using System;
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    /// <summary>Layers used by the prototype (layer names are irrelevant, masks are not).</summary>
    public static class GZ
    {
        public const int ActorLayer = 8;    // capsule + hitboxes
        public const int WorldLayer = 9;    // static map colliders

        public const int ActorMask = 1 << ActorLayer;
        public const int WorldMask = 1 << WorldLayer;
        public const int SolidMask = ActorMask | WorldMask;

        public const float FixedStep = 1f / 64f;
        public const float EyeOffset = 0.16f;      // Core: eye = feet.y + CurrentHeight - 0.16
        public const float MaxShotRange = 200f;

        public static Vector3 V3(Vec3 v) { return new Vector3(v.x, v.y, v.z); }
        public static Vec3 C3(Vector3 v) { return new Vec3(v.x, v.y, v.z); }
        public static Vec3 C3(float x, float y, float z) { return new Vec3(x, y, z); }

        /// <summary>Yaw in degrees for a world direction, 0 = +Z (matches Core convention).</summary>
        public static float YawOf(Vector3 dir)
        {
            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        /// <summary>Pitch in degrees for a world direction, + = up.</summary>
        public static float PitchOf(Vector3 dir)
        {
            float len = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
            return Mathf.Atan2(dir.y, len) * Mathf.Rad2Deg;
        }

        public static Vector3 DirOf(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad;
            float p = pitchDeg * Mathf.Deg2Rad;
            float cp = Mathf.Cos(p);
            return new Vector3(Mathf.Sin(y) * cp, Mathf.Sin(p), Mathf.Cos(y) * cp);
        }

        public static float WrapAngle(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

        public static float MoveTowardsAngle(float current, float target, float maxDelta)
        {
            float d = WrapAngle(target - current);
            if (d > maxDelta) d = maxDelta;
            if (d < -maxDelta) d = -maxDelta;
            return current + d;
        }

        public static Color Col(int r, int g, int b) { return new Color(r / 255f, g / 255f, b / 255f, 1f); }
        public static Color Col(int r, int g, int b, float a) { return new Color(r / 255f, g / 255f, b / 255f, a); }

        // SPEC 4.2 palette.
        public static readonly Color CFloor = Col(0x6B, 0x6E, 0x6A);
        public static readonly Color CWall = Col(0x7A, 0x7D, 0x78);
        public static readonly Color CCrate = Col(0x6E, 0x5A, 0x3F);
        public static readonly Color CSteel = Col(0x5C, 0x5F, 0x5A);
        public static readonly Color CDark = Col(0x4E, 0x51, 0x4C);
        public static readonly Color CSite = Col(0x7A, 0x3B, 0x36);

        private static Transform _root;
        public static Transform Root
        {
            get { return _root; }
            set { _root = value; }
        }
    }

    /// <summary>Shared game clock (advanced by GameDirector, frozen while paused / in menus).</summary>
    public static class GameClock
    {
        public static float Now;
        public static float Delta;
    }

    /// <summary>Team / naming helpers that are GC-free.</summary>
    public static class GzNames
    {
        public static string TeamShort(Team t) { return t == Team.Terrorists ? "T" : "CT"; }
        private static readonly string[] _botNames =
        {
            "Falcon", "Viper", "Rook", "Dagger", "Nomad", "Cobra", "Kestrel", "Wolf"
        };
        public static string BotName(int index)
        {
            if (index < 0) index = 0;
            return _botNames[index % _botNames.Length];
        }
    }

    /// <summary>Int label that only re-formats when the value changes (zero GC in steady state).</summary>
    public struct IntLabel
    {
        private int _last;
        private string _text;
        public string Get(int value)
        {
            if (_text == null || value != _last)
            {
                _last = value;
                _text = value.ToString();
            }
            return _text;
        }
        public string GetSigned(int value)
        {
            if (_text == null || value != _last)
            {
                _last = value;
                _text = value >= 0 ? "+" + value.ToString() : value.ToString();
            }
            return _text;
        }
    }

    /// <summary>Float label rounded to a step; re-formats only when the rounded value changes.</summary>
    public struct FloatLabel
    {
        private float _last;
        private string _text;
        private int _decimals;

        public string Get(float value, int decimals)
        {
            _decimals = decimals;
            float q = Mathf.Round(value * 100f) / 100f;
            if (_text == null || !Mathf.Approximately(q, _last))
            {
                _last = q;
                _text = q.ToString(decimals == 0 ? "0" : (decimals == 1 ? "0.0" : "0.00"));
            }
            return _text;
        }
        public string GetPct(float value01)
        {
            return Get(value01 * 100f, 0);
        }
    }

    /// <summary>Cached "prefix + int" label (money, HP, scores ...) - no allocation while unchanged.</summary>
    public struct PInt
    {
        private int _last;
        private string _prefix;
        private string _text;
        public string Get(string prefix, int value)
        {
            if (_text == null || value != _last || prefix != _prefix)
            {
                _prefix = prefix;
                _last = value;
                _text = prefix + value.ToString();
            }
            return _text;
        }
    }

    /// <summary>Cached "prefix + a + mid + b + post" label (score boards, "T 3 : 5 CT").</summary>
    public struct PInt2
    {
        private int _a;
        private int _b;
        private bool _has;
        private string _text;
        public string Get(string pre, int a, string mid, int b, string post)
        {
            if (!_has || a != _a || b != _b)
            {
                _has = true;
                _a = a;
                _b = b;
                _text = pre + a.ToString() + mid + b.ToString() + post;
            }
            return _text;
        }
    }

    /// <summary>Cached "prefix + int + optional suffix flags" (armour / helmet / kit line).</summary>
    public struct PIntBits
    {
        private int _value;
        private int _bits;
        private string _prefix;
        private string _text;
        private bool _has;
        public string Get(string prefix, int value, int bits, string flag1, string flag2)
        {
            if (_has && value == _value && bits == _bits && prefix == _prefix) return _text;
            _has = true;
            _value = value;
            _bits = bits;
            _prefix = prefix;
            _text = prefix + value.ToString() + ((bits & 1) != 0 ? flag1 : "") + ((bits & 2) != 0 ? flag2 : "");
            return _text;
        }
    }

    /// <summary>Cached "prefix + ON/OFF".</summary>
    public struct BoolLabel
    {
        private bool _last;
        private string _prefix;
        private string _text;
        public string Get(string prefix, bool on)
        {
            if (_text == null || on != _last || prefix != _prefix)
            {
                _prefix = prefix;
                _last = on;
                _text = prefix + (on ? "ON" : "OFF");
            }
            return _text;
        }
    }

    /// <summary>Cached "prefix + already cached body" (timers).</summary>
    public struct PStr
    {
        private string _body;
        private string _prefix;
        private string _text;
        public string Get(string prefix, string body)
        {
            if (_text == null || !ReferenceEquals(body, _body) || prefix != _prefix)
            {
                _prefix = prefix;
                _body = body;
                _text = prefix + body;
            }
            return _text;
        }
    }

    /// <summary>Cached "prefix + body + suffix".</summary>
    public struct PStr2
    {
        private string _body;
        private string _prefix;
        private string _suffix;
        private string _text;
        public string Get(string prefix, string body, string suffix)
        {
            if (_text == null || !ReferenceEquals(body, _body) || prefix != _prefix || suffix != _suffix)
            {
                _prefix = prefix;
                _body = body;
                _suffix = suffix;
                _text = prefix + body + suffix;
            }
            return _text;
        }
    }

    /// <summary>Time formatter (mm:ss / ss.t) with caching.</summary>
    public struct TimeLabel
    {
        private int _last;
        private string _text;
        public string Get(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            if (_text == null || s != _last)
            {
                _last = s;
                int m = s / 60;
                int r = s % 60;
                _text = m > 0 ? m + ":" + (r < 10 ? "0" + r : r.ToString()) : s.ToString();
            }
            return _text;
        }
    }
}
