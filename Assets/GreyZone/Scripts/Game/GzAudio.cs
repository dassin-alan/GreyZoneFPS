// GreyZone - fully procedural audio: every clip is synthesised at load, played through a small
// pooled set of 3D AudioSources (<= 24 concurrent voices, oldest voice is stolen first).
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public sealed class GzAudio : MonoBehaviour
    {
        public static GzAudio I;

        public AudioClip ShotPistolLight;
        public AudioClip ShotPistolHeavy;
        public AudioClip ShotRifle;
        public AudioClip ShotSniper;
        public AudioClip Reload;
        public AudioClip HitMarker;
        public AudioClip Headshot;
        public AudioClip Footstep;
        public AudioClip BombTick;
        public AudioClip Plant;
        public AudioClip Defuse;
        public AudioClip Explosion;
        public AudioClip Win;
        public AudioClip Lose;
        public AudioClip Death;
        public AudioClip Click;
        public AudioClip Scope;

        private const int Voices = 24;
        private AudioSource[] _src;
        private float[] _started;
        private int _cursor;

        public void Init(Transform parent)
        {
            I = this;

            ShotPistolLight = MakeGun("gz_shot_pistol_light", 0.20f, 210f, 0.55f, 26f, 0.55f);
            ShotPistolHeavy = MakeGun("gz_shot_pistol_heavy", 0.26f, 150f, 0.62f, 20f, 0.75f);
            ShotRifle = MakeGun("gz_shot_rifle", 0.34f, 110f, 0.68f, 15f, 0.9f);
            ShotSniper = MakeGun("gz_shot_sniper", 0.52f, 78f, 0.75f, 10f, 1f);
            Reload = MakeClick("gz_reload", 0.11f, 0.55f, 1400f);
            Click = MakeClick("gz_click", 0.05f, 0.35f, 2200f);
            HitMarker = MakeTone("gz_hit", 1250f, 0.055f, 45f, 0.5f);
            Headshot = MakeTone("gz_hit_head", 1850f, 0.07f, 38f, 0.55f);
            Footstep = MakeClick("gz_step", 0.09f, 0.30f, 520f);
            BombTick = MakeTone("gz_tick", 1750f, 0.06f, 55f, 0.45f);
            Plant = MakeTone("gz_plant", 900f, 0.10f, 26f, 0.4f);
            Defuse = MakeTone("gz_defuse", 640f, 0.12f, 22f, 0.42f);
            Explosion = MakeExplosion("gz_boom");
            Win = MakeSequence("gz_win", new float[] { 523f, 659f, 784f }, 0.11f);
            Lose = MakeSequence("gz_lose", new float[] { 392f, 330f, 262f }, 0.13f);
            Death = MakeDeath("gz_death");
            Scope = MakeClick("gz_scope", 0.07f, 0.25f, 900f);

            _src = new AudioSource[Voices];
            _started = new float[Voices];
            GameObject holder = new GameObject("GzVoices");
            holder.transform.SetParent(parent, false);
            for (int i = 0; i < Voices; i++)
            {
                GameObject go = new GameObject("voice" + i);
                go.transform.SetParent(holder.transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 3f;
                s.maxDistance = 60f;
                s.dopplerLevel = 0f;
                s.spread = 0f;
                s.priority = 128;
                s.bypassEffects = true;
                s.bypassListenerEffects = true;
                _src[i] = s;
                _started[i] = -100f;
            }
        }

        public void Play(AudioClip clip, Vector3 pos, float volume, float pitch)
        {
            if (clip == null || _src == null) return;
            int ix = -1;
            for (int i = 0; i < Voices; i++)
            {
                int k = (_cursor + i) % Voices;
                if (!_src[k].isPlaying) { ix = k; break; }
            }
            if (ix < 0)
            {
                float oldest = float.MaxValue;
                ix = 0;
                for (int i = 0; i < Voices; i++)
                {
                    if (_started[i] < oldest) { oldest = _started[i]; ix = i; }
                }
                _src[ix].Stop();
            }
            _cursor = (ix + 1) % Voices;
            AudioSource s = _src[ix];
            s.transform.position = pos;
            s.clip = clip;
            s.volume = volume;
            s.pitch = pitch;
            s.Play();
            _started[ix] = Time.unscaledTime;
        }

        /// <summary>Weapon shot with a little per-shot pitch variation (no allocation).</summary>
        public void PlayShot(WeaponDef def, Vector3 pos, bool localPlayer)
        {
            AudioClip c = ShotPistolLight;
            if (def != null)
            {
                switch (def.Kind)
                {
                    case WeaponKind.Sniper: c = ShotSniper; break;
                    case WeaponKind.Rifle: c = ShotRifle; break;
                    default:
                        c = def.Damage >= 45 ? ShotPistolHeavy : ShotPistolLight;
                        break;
                }
            }
            float v = localPlayer ? 0.85f : 0.65f;
            Play(c, pos, v, Random.Range(0.96f, 1.04f));
        }

        // ------------------------------------------------------------------ synthesis
        private const int Rate = 22050;

        private static float Noise(ref uint seed)
        {
            seed = seed * 1664525u + 1013904223u;
            return ((seed >> 9) & 0x7FFFFF) / 4194304f - 1f;
        }

        private static AudioClip MakeGun(string name, float seconds, float bodyHz, float noiseAmt, float decay, float crack)
        {
            int n = Mathf.Max(64, (int)(seconds * Rate));
            float[] d = new float[n];
            uint seed = 987654321u;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * decay);
                float noise = Noise(ref seed);
                lp += (noise - lp) * 0.35f;
                float body = Mathf.Sin(t * bodyHz * 6.2831853f) * Mathf.Exp(-t * decay * 0.5f);
                float tl = Mathf.Clamp01(t / 0.0015f);
                float v = (lp * noiseAmt + body * 0.6f + noise * crack * Mathf.Exp(-t * 95f)) * env * tl;
                d[i] = Mathf.Clamp(v * 0.85f, -1f, 1f);
            }
            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        private static AudioClip MakeClick(string name, float seconds, float gain, float toneHz)
        {
            int n = Mathf.Max(32, (int)(seconds * Rate));
            float[] d = new float[n];
            uint seed = 13579u;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 85f);
                float noise = Noise(ref seed) * 0.6f;
                float tone = Mathf.Sin(t * toneHz * 6.2831853f) * 0.4f;
                d[i] = Mathf.Clamp((noise + tone) * env * gain, -1f, 1f);
            }
            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        private static AudioClip MakeTone(string name, float hz, float seconds, float decay, float gain)
        {
            int n = Mathf.Max(32, (int)(seconds * Rate));
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(t / 0.002f);
                d[i] = Mathf.Clamp(Mathf.Sin(t * hz * 6.2831853f) * env * gain, -1f, 1f);
            }
            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        private static AudioClip MakeSequence(string name, float[] hz, float step)
        {
            int per = Mathf.Max(32, (int)(step * Rate));
            int n = per * hz.Length;
            float[] d = new float[n];
            for (int k = 0; k < hz.Length; k++)
            {
                for (int i = 0; i < per; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-t * 9f) * Mathf.Clamp01(t / 0.004f) * (1f - (float)i / per * 0.4f);
                    d[k * per + i] = Mathf.Clamp(Mathf.Sin(t * hz[k] * 6.2831853f) * env * 0.42f, -1f, 1f);
                }
            }
            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        private static AudioClip MakeExplosion(string name)
        {
            int n = (int)(1.6f * Rate);
            float[] d = new float[n];
            uint seed = 24681357u;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = Noise(ref seed);
                lp += (noise - lp) * 0.08f;
                float boom = Mathf.Sin(t * 46f * 6.2831853f) * Mathf.Exp(-t * 3.2f);
                float crack = noise * Mathf.Exp(-t * 42f);
                d[i] = Mathf.Clamp((lp * 1.1f + boom * 0.7f + crack * 0.5f) * Mathf.Exp(-t * 1.1f), -1f, 1f) * 0.95f;
            }
            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        private static AudioClip MakeDeath(string name)
        {
            int n = (int)(0.4f * Rate);
            float[] d = new float[n];
            uint seed = 555777u;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = Noise(ref seed) * 0.35f;
                float thud = Mathf.Sin(t * 88f * 6.2831853f) * 0.6f;
                d[i] = Mathf.Clamp((thud + noise) * Mathf.Exp(-t * 12f), -1f, 1f) * 0.8f;
            }
            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
