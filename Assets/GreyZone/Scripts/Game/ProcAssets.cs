// GreyZone - all art assets are generated at runtime: shader fallbacks, materials, textures, meshes.
using UnityEngine;
using UnityEngine.Rendering;

namespace GreyZone.Game
{
    public static class ProcAssets
    {
        public static Shader LitShader;
        public static Shader AlphaShader;
        public static Shader AdditiveShader;

        public static Material MapMat;
        public static Material ActorMat;
        public static Material DecalMat;
        public static Material TracerMat;
        public static Material SparkMat;
        public static Material MuzzleMat;
        public static Material BloodMat;
        public static Material ShadowMat;
        public static Material SmokeMat;
        public static Material BombMat;

        public static Texture2D White;
        public static Texture2D Grain;
        public static Texture2D Spark;
        public static Texture2D Muzzle;
        public static Texture2D Hole;
        public static Texture2D Blood;
        public static Texture2D Shadow;
        public static Texture2D Dot;
        public static Texture2D Smoke;

        public static Mesh Quad;
        public static Mesh Sphere;
        public static Mesh Cube;
        public static Mesh Plane;
        public static Mesh ActorLegsT;
        public static Mesh ActorUpperT;
        public static Mesh ActorLegsCT;
        public static Mesh ActorUpperCT;

        private static bool _ready;

        public static void EnsureInit()
        {
            if (_ready) return;
            _ready = true;

            LitShader = FindShader("GreyZone/VertexColorLit", "Legacy Shaders/Diffuse", "Mobile/Diffuse", "Unlit/Color", "Hidden/Internal-Colored");
            AlphaShader = FindShader("GreyZone/VertexColorAlpha", "Sprites/Default", "Unlit/Transparent", "Hidden/Internal-Colored");
            AdditiveShader = FindShader("GreyZone/VertexColorAdditive", "Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default", "Hidden/Internal-Colored");

            White = MakeSolid(2, 2, Color.white);
            Grain = MakeGrain(64);
            Spark = MakeSpark(32);
            Muzzle = MakeMuzzle(32);
            Hole = MakeHole(32);
            Blood = MakeBlood(32);
            Shadow = MakeBlob(48, 0.55f);
            Dot = MakeBlob(16, 0.9f);
            Smoke = MakeBlob(32, 0.35f);

            MapMat = MakeLit(LitShader, Grain, Color.white);
            ActorMat = MakeLit(LitShader, Grain, Color.white);
            BombMat = MakeLit(LitShader, Grain, GZ.Col(0x3A, 0x3A, 0x3E));

            DecalMat = MakeBlend(AlphaShader, Hole, Color.white);
            BloodMat = MakeBlend(AlphaShader, Blood, Color.white);
            ShadowMat = MakeBlend(AlphaShader, Shadow, new Color(0f, 0f, 0f, 0.45f));
            SmokeMat = MakeBlend(AlphaShader, Smoke, new Color(0.42f, 0.42f, 0.40f, 0.55f));
            TracerMat = MakeBlend(AdditiveShader, White, new Color(1f, 0.92f, 0.55f, 0.85f));
            SparkMat = MakeBlend(AdditiveShader, Spark, new Color(1f, 0.85f, 0.45f, 1f));
            MuzzleMat = MakeBlend(AdditiveShader, Muzzle, new Color(1f, 0.88f, 0.55f, 1f));

            Quad = MeshBuilder.MakeUnitQuad();
            Sphere = MeshBuilder.MakeSphere(12, 8);
            Cube = MakeBoxMesh(Vector3.one);
            Plane = MakeBoxMesh(new Vector3(1f, 0.02f, 1f));
            ActorLegsT = BuildActorMesh(true, false);
            ActorUpperT = BuildActorMesh(true, true);
            ActorLegsCT = BuildActorMesh(false, false);
            ActorUpperCT = BuildActorMesh(false, true);
        }

        /// <summary>Every renderer we create is unlit-ish, shadow free and static (no motion vectors).</summary>
        public static void SetupRenderer(MeshRenderer mr, Material mat)
        {
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            mr.allowOcclusionWhenDynamic = false;
        }

        public static Mesh MakeBoxMesh(Vector3 size)
        {
            MeshBuilder b = new MeshBuilder();
            b.AddBox(Vector3.zero, size, Color.white, 0f);
            return b.Build("gz_box");
        }

        private static Shader FindShader(string name, params string[] fallbacks)
        {
            Shader s = Shader.Find(name);
            if (s != null && s.isSupported) return s;
            for (int i = 0; i < fallbacks.Length; i++)
            {
                s = Shader.Find(fallbacks[i]);
                if (s != null && s.isSupported)
                {
                    Debug.LogWarning("[GreyZone] shader '" + name + "' unavailable, using fallback '" + fallbacks[i] + "'");
                    return s;
                }
            }
            Debug.LogError("[GreyZone] no usable shader found for '" + name + "'.");
            return null;
        }

        private static Material MakeLit(Shader s, Texture2D tex, Color tint)
        {
            Material m = new Material(s);
            m.name = "gz_lit_" + tex.name;
            if (m.HasProperty("_MainTex")) m.mainTexture = tex;
            if (m.HasProperty("_Tint")) m.SetColor("_Tint", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            m.enableInstancing = false;
            return m;
        }

        private static Material MakeBlend(Shader s, Texture2D tex, Color tint)
        {
            Material m = new Material(s);
            m.name = "gz_blend_" + tex.name;
            if (m.HasProperty("_MainTex")) m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            return m;
        }

        // ---------------------------------------------------------------- textures
        private static Texture2D NewTex(int w, int h, bool mips, TextureWrapMode wrap)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, mips);
            t.wrapMode = wrap;
            t.filterMode = FilterMode.Bilinear;
            t.name = "gz_tex";
            return t;
        }

        private static Texture2D MakeSolid(int w, int h, Color c)
        {
            Texture2D t = NewTex(w, h, false, TextureWrapMode.Clamp);
            t.name = "gz_white";
            Color32[] px = new Color32[w * h];
            Color32 c32 = c;
            for (int i = 0; i < px.Length; i++) px[i] = c32;
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static float Hash01(int x, int y)
        {
            int n = x * 374761393 + y * 668265263;
            n = (n ^ (n >> 13)) * 1274126177;
            n = n ^ (n >> 16);
            return (n & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        private static Texture2D MakeGrain(int size)
        {
            Texture2D t = NewTex(size, size, true, TextureWrapMode.Repeat);
            t.name = "gz_grain";
            Color32[] px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Hash01(x, y) * 0.5f + Hash01(x / 4, y / 4) * 0.35f + Hash01(x / 11, y / 11) * 0.15f;
                    float v = 0.84f + 0.16f * n;
                    byte b = (byte)Mathf.Clamp(v * 255f, 0f, 255f);
                    px[y * size + x] = new Color32(b, b, b, 255);
                }
            }
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        private static Texture2D MakeSpark(int size)
        {
            Texture2D t = NewTex(size, size, false, TextureWrapMode.Clamp);
            t.name = "gz_spark";
            Color32[] px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * a;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeMuzzle(int size)
        {
            Texture2D t = NewTex(size, size, false, TextureWrapMode.Clamp);
            t.name = "gz_muzzle";
            Color32[] px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(1f - r * 1.6f);
                    float spike = Mathf.Clamp01(1f - Mathf.Abs(dx) * 9f) + Mathf.Clamp01(1f - Mathf.Abs(dy) * 9f);
                    float a = Mathf.Clamp01(core * core * 1.2f + spike * 0.35f * Mathf.Clamp01(1f - r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeHole(int size)
        {
            Texture2D t = NewTex(size, size, false, TextureWrapMode.Clamp);
            t.name = "gz_hole";
            Color32[] px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.InverseLerp(0.55f, 0.82f, r));
                    float dark = Mathf.Lerp(0.06f, 0.26f, Mathf.InverseLerp(0.2f, 0.8f, r));
                    byte b = (byte)(dark * 255f);
                    px[y * size + x] = new Color32(b, b, b, (byte)(a * 235f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeBlood(int size)
        {
            Texture2D t = NewTex(size, size, false, TextureWrapMode.Clamp);
            t.name = "gz_blood";
            Color32[] px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float wobble = 1f + 0.22f * (Hash01(x / 3, y / 3) - 0.5f);
                    float a = Mathf.Clamp01(1f - Mathf.InverseLerp(0.25f, 0.95f * wobble, r));
                    px[y * size + x] = new Color32((byte)(0.34f * 255f), (byte)(0.05f * 255f), (byte)(0.05f * 255f), (byte)(a * 220f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeBlob(int size, float hardness)
        {
            Texture2D t = NewTex(size, size, false, TextureWrapMode.Clamp);
            t.name = "gz_blob";
            Color32[] px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.InverseLerp(hardness * 0.2f, hardness, r));
                    a *= a;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        // ---------------------------------------------------------------- actor meshes
        /// <summary>Blocky two-part soldier. upper = torso/head/arms/gun, else legs + boots.</summary>
        public static Mesh BuildActorMesh(bool ct, bool upper)
        {
            float pivotY = 1.05f;
            Color cloth = ct ? GZ.Col(0x4A, 0x56, 0x6C) : GZ.Col(0x6B, 0x5F, 0x42);
            Color vest = ct ? GZ.Col(0x39, 0x43, 0x54) : GZ.Col(0x52, 0x49, 0x33);
            Color pants = ct ? GZ.Col(0x3D, 0x46, 0x57) : GZ.Col(0x57, 0x4E, 0x38);
            Color skin = GZ.Col(0xB8, 0x93, 0x72);
            Color dark = GZ.Col(0x2A, 0x2A, 0x2E);

            MeshBuilder b = new MeshBuilder();
            if (!upper)
            {
                b.AddBox(new Vector3(-0.115f, 0.44f, 0f), new Vector3(0.17f, 0.88f, 0.21f), pants, 0f);
                b.AddBox(new Vector3(0.115f, 0.44f, 0f), new Vector3(0.17f, 0.88f, 0.21f), pants, 0f);
                b.AddBox(new Vector3(-0.115f, 0.06f, 0.02f), new Vector3(0.19f, 0.12f, 0.27f), dark, 0f);
                b.AddBox(new Vector3(0.115f, 0.06f, 0.02f), new Vector3(0.19f, 0.12f, 0.27f), dark, 0f);
            }
            else
            {
                // torso + vest
                b.AddBox(new Vector3(0f, 1.12f - pivotY, 0f), new Vector3(0.44f, 0.56f, 0.26f), cloth, 0f);
                b.AddBox(new Vector3(0f, 1.16f - pivotY, 0f), new Vector3(0.47f, 0.36f, 0.30f), vest, 0f);
                // head + helmet
                b.AddBox(new Vector3(0f, 1.585f - pivotY, 0f), new Vector3(0.21f, 0.23f, 0.23f), skin, 0f);
                b.AddBox(new Vector3(0f, 1.70f - pivotY, 0f), new Vector3(0.25f, 0.12f, 0.26f), ct ? GZ.Col(0x2F, 0x36, 0x44) : GZ.Col(0x44, 0x3E, 0x2C), 0f);
                // arms
                b.AddBox(new Vector3(-0.27f, 1.13f - pivotY, 0f), new Vector3(0.12f, 0.48f, 0.14f), cloth, 0f);
                b.AddBox(new Vector3(0.27f, 1.13f - pivotY, 0f), new Vector3(0.12f, 0.48f, 0.14f), cloth, 0f);
                b.AddBox(new Vector3(-0.28f, 0.86f - pivotY, 0.08f), new Vector3(0.11f, 0.10f, 0.12f), dark, 0f);
                b.AddBox(new Vector3(0.28f, 0.86f - pivotY, 0.08f), new Vector3(0.11f, 0.10f, 0.12f), dark, 0f);
                // weapon (held forward-right)
                b.AddBox(new Vector3(0.17f, 1.22f - pivotY, 0.26f), new Vector3(0.07f, 0.13f, 0.52f), dark, 0f);
                b.AddBox(new Vector3(0.17f, 1.26f - pivotY, 0.62f), new Vector3(0.05f, 0.06f, 0.28f), dark, 0f);
                b.AddBox(new Vector3(0.17f, 1.05f - pivotY, 0.18f), new Vector3(0.06f, 0.17f, 0.10f), dark, 0f);
                b.AddBox(new Vector3(0.17f, 1.33f - pivotY, 0.32f), new Vector3(0.04f, 0.05f, 0.14f), GZ.Col(0x1A, 0x1A, 0x1C), 0f);
            }
            Mesh m = b.Build(upper ? (ct ? "gz_actor_upper_ct" : "gz_actor_upper_t") : (ct ? "gz_actor_legs_ct" : "gz_actor_legs_t"));
            return m;
        }
    }
}
