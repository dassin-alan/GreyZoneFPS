// GreyZone - pooled effect system: tracers, sparks, bullet holes, muzzle flashes, blood, smoke,
// explosions and one flat fake shadow per actor. Zero allocation per frame, no lights, no shadows.
using UnityEngine;

namespace GreyZone.Game
{
    /// <summary>Fixed-size pool of quads or cubes sharing one material; per-instance colour via MPB.</summary>
    public sealed class FxPool
    {
        private readonly Transform[] _tf;
        private readonly Renderer[] _rd;
        private readonly MaterialPropertyBlock[] _mpb;
        private readonly float[] _life;
        private readonly float[] _lifeMax;
        private readonly float[] _fade;
        private readonly float[] _grow;
        private readonly Color[] _tint;
        private readonly Vector3[] _baseScale;
        private readonly float[] _lastAlpha;
        private readonly bool[] _on;
        private readonly bool _billboard;
        private readonly int _count;
        private int _cursor;
        private int _active;

        /// <summary>Quality tier cap on simultaneous instances (SPEC 4.10 "effect pool budget").</summary>
        public int Limit = int.MaxValue;

        public int ActiveCount { get { return _active; } }

        public FxPool(Transform parent, string name, Material mat, Mesh mesh, int count, bool billboard)
        {
            _count = count;
            _billboard = billboard;
            _tf = new Transform[count];
            _rd = new Renderer[count];
            _mpb = new MaterialPropertyBlock[count];
            _life = new float[count];
            _lifeMax = new float[count];
            _fade = new float[count];
            _grow = new float[count];
            _tint = new Color[count];
            _baseScale = new Vector3[count];
            _lastAlpha = new float[count];
            _on = new bool[count];

            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);

            for (int i = 0; i < count; i++)
            {
                GameObject go = new GameObject("fx");
                go.layer = GZ.WorldLayer;
                go.transform.SetParent(holder.transform, false);
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                ProcAssets.SetupRenderer(mr, mat);
                mr.enabled = false;
                _tf[i] = go.transform;
                _rd[i] = mr;
                _mpb[i] = new MaterialPropertyBlock();
                _lastAlpha[i] = -1f;
            }
        }

        public void Spawn(Vector3 pos, Quaternion rot, Vector3 scale, Color color, float life, float fade, float grow)
        {
            if (_active >= Limit) return;
            int i = _cursor;
            int guard = 0;
            while (_on[i] && guard++ < _count) i = (i + 1) % _count;
            _cursor = (i + 1) % _count;

            if (!_on[i]) _active++;
            _on[i] = true;
            _life[i] = life;
            _lifeMax[i] = life;
            _fade[i] = fade;
            _grow[i] = grow;
            _tint[i] = color;
            _baseScale[i] = scale;
            _lastAlpha[i] = -1f;

            _tf[i].position = pos;
            _tf[i].rotation = rot;
            _tf[i].localScale = scale;
            _rd[i].enabled = true;
            ApplyTint(i, color.a);
        }

        public void Tick(float dt)
        {
            if (_active <= 0) return;
            Vector3 camPos = FxSystem.CamPos;
            for (int i = 0; i < _count; i++)
            {
                if (!_on[i]) continue;
                _life[i] -= dt;
                if (_life[i] <= 0f)
                {
                    _on[i] = false;
                    _rd[i].enabled = false;
                    _active--;
                    continue;
                }
                float a = _tint[i].a;
                if (_fade[i] > 0f)
                {
                    float f = Mathf.Clamp01(_life[i] / _fade[i]);
                    a *= f;
                }
                ApplyTint(i, a);
                if (_grow[i] != 0f)
                {
                    float t = 1f - Mathf.Clamp01(_life[i] / Mathf.Max(0.0001f, _lifeMax[i]));
                    _tf[i].localScale = _baseScale[i] * (1f + _grow[i] * t);
                }
                if (_billboard)
                {
                    Vector3 dir = _tf[i].position - camPos;
                    if (dir.sqrMagnitude > 0.001f)
                        _tf[i].rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }
        }

        private void ApplyTint(int i, float alpha)
        {
            if (Mathf.Abs(_lastAlpha[i] - alpha) < 0.004f) return;
            _lastAlpha[i] = alpha;
            Color c = _tint[i];
            c.a = alpha;
            _mpb[i].SetColor("_Color", c);
            _rd[i].SetPropertyBlock(_mpb[i]);
        }

        public void ClearAll()
        {
            for (int i = 0; i < _count; i++)
            {
                _on[i] = false;
                _life[i] = 0f;
                if (_rd[i] != null) _rd[i].enabled = false;
            }
            _active = 0;
        }
    }

    public sealed class FxSystem : MonoBehaviour
    {
        public static FxSystem I;

        public static Vector3 CamPos = new Vector3(0f, 1.6f, 0f);

        private FxPool _tracers;
        private FxPool _sparks;
        private FxPool _decals;
        private FxPool _muzzle;
        private FxPool _blood;
        private FxPool _smoke;
        private FxPool _boom;
        private Transform _root;
        private Camera _cam;
        private readonly System.Collections.Generic.List<Transform> _shadows = new System.Collections.Generic.List<Transform>(12);

        public void Init(Transform root)
        {
            I = this;
            _root = root;

            _tracers = new FxPool(root, "FxTracers", ProcAssets.TracerMat, ProcAssets.Cube, 32, false);
            _sparks = new FxPool(root, "FxSparks", ProcAssets.SparkMat, ProcAssets.Quad, 48, true);
            _decals = new FxPool(root, "FxDecals", ProcAssets.DecalMat, ProcAssets.Quad, 64, false);
            _muzzle = new FxPool(root, "FxMuzzle", ProcAssets.MuzzleMat, ProcAssets.Quad, 8, true);
            _blood = new FxPool(root, "FxBlood", ProcAssets.BloodMat, ProcAssets.Quad, 16, true);
            _smoke = new FxPool(root, "FxSmoke", ProcAssets.SmokeMat, ProcAssets.Quad, 16, true);
            _boom = new FxPool(root, "FxBoom", ProcAssets.SparkMat, ProcAssets.Quad, 6, true);
            ApplyQualityLimits();
        }

        /// <summary>Low / medium tiers shrink the live effect budget (pools themselves stay allocated).</summary>
        public void ApplyQualityLimits()
        {
            float k = Mathf.Clamp01(GameSettings.EffectScale);
            _tracers.Limit = Mathf.Max(6, Mathf.RoundToInt(32f * k));
            _sparks.Limit = Mathf.Max(6, Mathf.RoundToInt(48f * k));
            _decals.Limit = Mathf.Max(8, Mathf.RoundToInt(64f * k));
            _muzzle.Limit = Mathf.Max(3, Mathf.RoundToInt(8f * k));
            _blood.Limit = Mathf.Max(3, Mathf.RoundToInt(16f * k));
            _smoke.Limit = Mathf.Max(3, Mathf.RoundToInt(16f * k));
            _boom.Limit = 6;
        }

        public Transform AddShadow(Transform owner)
        {
            GameObject go = new GameObject("shadow");
            go.layer = GZ.WorldLayer;
            go.transform.SetParent(owner, false);
            go.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = ProcAssets.Quad;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            ProcAssets.SetupRenderer(mr, ProcAssets.ShadowMat);
            _shadows.Add(go.transform);
            return go.transform;
        }

        public void RemoveShadow(Transform owner)
        {
            for (int i = _shadows.Count - 1; i >= 0; i--)
            {
                Transform t = _shadows[i];
                if (t == null) { _shadows.RemoveAt(i); continue; }
                if (t.parent == owner)
                {
                    _shadows.RemoveAt(i);
                    Destroy(t.gameObject);
                    return;
                }
            }
        }

        public void Tick(float dt)
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam != null) CamPos = _cam.transform.position;
            _tracers.Tick(dt);
            _sparks.Tick(dt);
            _decals.Tick(dt);
            _muzzle.Tick(dt);
            _blood.Tick(dt);
            _smoke.Tick(dt);
            _boom.Tick(dt);
        }

        public void ClearProjectiles()
        {
            _tracers.ClearAll();
            _muzzle.ClearAll();
            _boom.ClearAll();
        }

        /// <summary>Line of additive geometry between two points (already in world space).</summary>
        public void Tracer(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.2f) return;
            Vector3 dir = d / len;
            Vector3 mid = from + dir * (len * 0.5f);
            _tracers.Spawn(mid, Quaternion.LookRotation(dir, Vector3.up),
                new Vector3(0.022f, 0.022f, len), new Color(1f, 0.94f, 0.68f, 0.7f), 0.055f, 0f, 0f);
        }

        public void Muzzle(Vector3 pos, Vector3 dir, float scale)
        {
            float roll = Random.Range(0f, 360f);
            _muzzle.Spawn(pos, Quaternion.LookRotation(-dir, Vector3.up) * Quaternion.Euler(0f, 0f, roll),
                new Vector3(0.55f * scale, 0.55f * scale, 1f), new Color(1f, 0.9f, 0.6f, 0.95f), 0.05f, 0.05f, 0.35f);
        }

        /// <summary>Bullet impact. flesh = blood puff instead of a concrete spark + hole.</summary>
        public void Impact(Vector3 pos, Vector3 normal, bool flesh)
        {
            if (flesh)
            {
                Vector3 n = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
                _blood.Spawn(pos + n * 0.16f, Quaternion.LookRotation(-n, Vector3.up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)),
                    new Vector3(0.34f, 0.34f, 1f), new Color(0.42f, 0.06f, 0.06f, 0.85f), 0.45f, 0.45f, 0.6f);
                return;
            }
            Vector3 nn = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
            Vector3 roll = Quaternion.AngleAxis(Random.Range(0f, 360f), nn) * -nn;
            _sparks.Spawn(pos + nn * 0.05f, Quaternion.LookRotation(roll, Vector3.up),
                new Vector3(0.26f, 0.26f, 1f), new Color(1f, 0.82f, 0.42f, 1f), 0.13f, 0.13f, 0.5f);
            _decals.Spawn(pos + nn * 0.014f,
                Quaternion.AngleAxis(Random.Range(0f, 360f), nn) * FaceNormal(nn),
                new Vector3(0.16f, 0.16f, 1f), new Color(0.9f, 0.9f, 0.9f, 0.85f), 10f, 2.2f, 0f);
        }

        public void Explosion(Vector3 pos)
        {
            _boom.Spawn(pos, Quaternion.identity, new Vector3(4f, 4f, 1f), new Color(1f, 0.72f, 0.32f, 1f), 0.42f, 0.42f, 1.8f);
            _boom.Spawn(pos + Vector3.up * 0.4f, Quaternion.identity, new Vector3(6f, 6f, 1f), new Color(1f, 0.5f, 0.2f, 0.7f), 0.6f, 0.6f, 1.1f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = pos + new Vector3(Random.Range(-3f, 3f), Random.Range(0.2f, 3.5f), Random.Range(-3f, 3f));
                _smoke.Spawn(p, Quaternion.identity, new Vector3(1.8f, 1.8f, 1f), new Color(0.35f, 0.34f, 0.32f, 0.65f), 1.4f, 1.4f, 1.6f);
            }
        }

        public void Smoke(Vector3 pos, float scale, float life)
        {
            _smoke.Spawn(pos, Quaternion.identity, new Vector3(scale, scale, 1f), new Color(0.55f, 0.55f, 0.52f, 0.5f), life, life, 1.2f);
        }

        public static bool Near(Vector3 p, float range)
        {
            return (p - CamPos).sqrMagnitude <= range * range;
        }

        private static Quaternion FaceNormal(Vector3 n)
        {
            Vector3 fwd = -n;
            Vector3 up = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.97f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(fwd, up);
        }
    }
}
