// GreyZone - procedural first person weapon model: boxes only, no prefabs, no animation assets.
// Sway / bob / recoil kick are all procedural offsets applied to the model root.
using UnityEngine;
using GreyZone.Core;

namespace GreyZone.Game
{
    public class ViewModel : MonoBehaviour
    {
        public Transform Root;
        public Transform Muzzle;

        private MeshFilter _mf;
        private MeshRenderer _mr;
        private Mesh _mesh;
        private WeaponDef _def;

        private float _kick;
        private float _kickVel;
        private float _swayX;
        private float _swayY;
        private float _bob;
        private readonly Vector3 _basePos = new Vector3(0.185f, -0.185f, 0.42f);
        private readonly Vector3 _baseRot = new Vector3(0f, -3.2f, 0f);

        public void Init(Transform camTf)
        {
            GameObject root = new GameObject("ViewModel");
            root.transform.SetParent(camTf, false);
            root.transform.localPosition = _basePos;
            root.transform.localRotation = Quaternion.Euler(_baseRot);
            Root = root.transform;

            GameObject model = new GameObject("Model");
            model.layer = 0;
            model.transform.SetParent(Root, false);
            _mf = model.AddComponent<MeshFilter>();
            _mr = model.AddComponent<MeshRenderer>();
            ProcAssets.SetupRenderer(_mr, ProcAssets.ActorMat);

            GameObject muzzle = new GameObject("Muzzle");
            muzzle.layer = 0;
            muzzle.transform.SetParent(model.transform, false);
            Muzzle = muzzle.transform;
        }

        public WeaponDef Def { get { return _def; } }

        public void SetWeapon(WeaponDef def)
        {
            if (_def == def && _mesh != null) return;
            _def = def;
            if (_mesh != null) Destroy(_mesh);
            _mesh = BuildModel(def);
            _mf.sharedMesh = _mesh;
            if (Muzzle != null) Muzzle.localPosition = MuzzleLocal;
            Root.localPosition = _basePos;
            Root.localRotation = Quaternion.Euler(_baseRot);
            _kick = 0f;
            _kickVel = 0f;
        }

        public void Hide(bool hidden)
        {
            if (Root != null) Root.gameObject.SetActive(!hidden);
        }

        public void Kick()
        {
            _kickVel += 1f;
        }

        public Vector3 MuzzlePos { get { return Muzzle.position; } }

        /// <summary>Per-frame procedural animation. yawDelta/pitchDelta are the view deltas of this frame.</summary>
        public void Tick(float dt, float speed01, float yawDelta, float pitchDelta, bool onGround, bool scoped)
        {
            if (Root == null) return;
            if (Root.gameObject.activeSelf == scoped) Hide(scoped);

            // sway follows the view motion with a lag
            _swayX = Mathf.Lerp(_swayX, Mathf.Clamp(yawDelta * 0.35f, -3.5f, 3.5f), dt * 9f);
            _swayY = Mathf.Lerp(_swayY, Mathf.Clamp(-pitchDelta * 0.35f, -3.0f, 3.0f), dt * 9f);

            // bob from horizontal speed
            float amp = onGround ? speed01 : 0f;
            _bob += dt * (4.5f + speed01 * 6f);
            float bobX = Mathf.Sin(_bob) * 0.012f * amp;
            float bobY = Mathf.Abs(Mathf.Cos(_bob)) * 0.010f * amp;

            // recoil kick spring
            _kick += _kickVel * 0.02f;
            _kickVel += -_kick * 60f * dt - _kickVel * 9f * dt;
            _kick = Mathf.Clamp(_kick, 0f, 3f);

            Vector3 p = _basePos;
            p.x += bobX + _swayX * 0.006f - _kick * 0.006f;
            p.y += bobY + _swayY * 0.006f - _kick * 0.004f;
            p.z += -_kick * 0.022f + Mathf.Abs(_swayY) * 0.002f;
            Root.localPosition = p;

            float pitchKick = -_kick * 3.2f + _swayY * 0.9f;
            float yawKick = _swayX * 0.8f + Mathf.Sin(_bob) * 0.4f * amp;
            Root.localRotation = Quaternion.Euler(_baseRot.x + pitchKick, _baseRot.y + yawKick, _swayX * 0.6f);
        }

        // ------------------------------------------------------------------ meshes
        private static Mesh BuildModel(WeaponDef def)
        {
            MeshBuilder b = new MeshBuilder();
            Color metal = GZ.Col(0x2C, 0x2D, 0x30);
            Color dark = GZ.Col(0x1A, 0x1B, 0x1D);
            Color wood = GZ.Col(0x6B, 0x4A, 0x2A);
            Color skin = GZ.Col(0xB8, 0x93, 0x72);
            Color accent = GZ.Col(0x44, 0x46, 0x4A);

            bool rifle = false;
            bool sniper = false;
            if (def != null)
            {
                rifle = def.Kind == WeaponKind.Rifle;
                sniper = def.Kind == WeaponKind.Sniper;
            }

            if (sniper)
            {
                b.AddBox(new Vector3(0f, 0f, 0.10f), new Vector3(0.055f, 0.075f, 0.52f), metal, 0f);
                b.AddBox(new Vector3(0f, 0.005f, 0.44f), new Vector3(0.028f, 0.028f, 0.34f), dark, 0f);
                b.AddBox(new Vector3(0f, -0.055f, 0.10f), new Vector3(0.045f, 0.16f, 0.09f), dark, 0f);
                b.AddBox(new Vector3(0f, -0.045f, -0.22f), new Vector3(0.05f, 0.075f, 0.26f), wood, 0f);
                b.AddBox(new Vector3(0f, 0.058f, 0.12f), new Vector3(0.045f, 0.045f, 0.22f), dark, 0f);
                b.AddBox(new Vector3(0f, 0.058f, 0.24f), new Vector3(0.055f, 0.055f, 0.06f), accent, 0f);
                b.AddBox(new Vector3(0.01f, -0.052f, 0.02f), new Vector3(0.05f, 0.10f, 0.07f), skin, 0f);
                MuzzleLocal = new Vector3(0f, 0.005f, 0.62f);
            }
            else if (rifle)
            {
                bool ak = def != null && def.Id == "ak47";
                Color body = ak ? wood : dark;
                b.AddBox(new Vector3(0f, 0.005f, 0.08f), new Vector3(0.055f, 0.075f, 0.46f), metal, 0f);
                b.AddBox(new Vector3(0f, 0.005f, 0.36f), new Vector3(0.024f, 0.024f, 0.26f), dark, 0f);
                b.AddBox(new Vector3(0f, 0.012f, 0.20f), new Vector3(0.045f, 0.045f, 0.20f), body, 0f);
                b.AddBox(new Vector3(0f, -0.075f, 0.11f), new Vector3(0.042f, 0.19f, 0.085f), dark, 0f);
                b.AddBox(new Vector3(0f, -0.05f, -0.16f), new Vector3(0.045f, 0.07f, 0.20f), body, 0f);
                b.AddBox(new Vector3(0f, 0.03f, -0.28f), new Vector3(0.045f, 0.075f, 0.10f), metal, 0f);
                b.AddBox(new Vector3(0.012f, -0.075f, 0.02f), new Vector3(0.05f, 0.10f, 0.07f), skin, 0f);
                MuzzleLocal = new Vector3(0f, 0.005f, 0.50f);
            }
            else
            {
                b.AddBox(new Vector3(0f, 0.015f, 0.06f), new Vector3(0.045f, 0.062f, 0.26f), metal, 0f);
                b.AddBox(new Vector3(0f, 0.048f, 0.06f), new Vector3(0.040f, 0.020f, 0.26f), accent, 0f);
                b.AddBox(new Vector3(0f, -0.055f, -0.02f), new Vector3(0.040f, 0.145f, 0.075f), dark, 0f);
                b.AddBox(new Vector3(0f, -0.075f, 0.03f), new Vector3(0.036f, 0.09f, 0.06f), skin, 0f);
                b.AddBox(new Vector3(0f, 0.005f, 0.20f), new Vector3(0.020f, 0.020f, 0.05f), dark, 0f);
                b.AddBox(new Vector3(0f, -0.03f, -0.09f), new Vector3(0.038f, 0.115f, 0.055f), skin, 0f);
                MuzzleLocal = new Vector3(0f, 0.015f, 0.22f);
            }

            return b.Build("gz_viewmodel");
        }

        public static Vector3 MuzzleLocal;
    }
}
