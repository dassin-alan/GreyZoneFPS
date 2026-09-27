// GreyZone - the C4 objective: procedural crate mesh, blinking indicator, accelerating beeps.
using UnityEngine;

namespace GreyZone.Game
{
    public class BombEntity : MonoBehaviour
    {
        public Vector3 Pos;
        public bool Planted;
        public bool Done;

        private Transform _visual;
        private MeshRenderer _ledRend;
        private MaterialPropertyBlock _mpb;
        private float _blink;
        private float _beepTimer;
        private Mesh _mesh;

        public void Build(Transform parent)
        {
            gameObject.layer = GZ.WorldLayer;
            transform.SetParent(parent, false);

            MeshBuilder b = new MeshBuilder();
            Color body = GZ.Col(0x35, 0x35, 0x38);
            Color strap = GZ.Col(0x22, 0x22, 0x24);
            b.AddBox(new Vector3(0f, 0.13f, 0f), new Vector3(0.34f, 0.20f, 0.46f), body, 0f);
            b.AddBox(new Vector3(0f, 0.24f, 0f), new Vector3(0.30f, 0.04f, 0.40f), strap, 0f);
            b.AddBox(new Vector3(0f, 0.13f, 0f), new Vector3(0.36f, 0.05f, 0.48f), strap, 0f);
            _mesh = b.Build("gz_bomb");

            GameObject go = new GameObject("bomb");
            go.layer = GZ.WorldLayer;
            go.transform.SetParent(transform, false);
            _visual = go.transform;
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            ProcAssets.SetupRenderer(mr, ProcAssets.BombMat);

            GameObject led = new GameObject("led");
            led.layer = GZ.WorldLayer;
            led.transform.SetParent(_visual, false);
            led.transform.localPosition = new Vector3(0f, 0.225f, 0f);
            led.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            led.transform.localScale = new Vector3(0.18f, 0.18f, 1f);
            MeshFilter lm = led.AddComponent<MeshFilter>();
            lm.sharedMesh = ProcAssets.Quad;
            _ledRend = led.AddComponent<MeshRenderer>();
            ProcAssets.SetupRenderer(_ledRend, ProcAssets.SparkMat);
            _mpb = new MaterialPropertyBlock();

            SetVisible(false);
        }

        public void SetVisible(bool on)
        {
            if (_visual != null) _visual.gameObject.SetActive(on);
        }

        public void Place(Vector3 pos)
        {
            Pos = pos;
            Planted = true;
            Done = false;
            transform.position = new Vector3(pos.x, pos.y, pos.z);
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            SetVisible(true);
            _blink = 0f;
            _beepTimer = 0.6f;
        }

        public void Reset()
        {
            Planted = false;
            Done = false;
            SetVisible(false);
        }

        /// <summary>timer01 = remaining / total. Drives the LED pulse and the tick rate.</summary>
        public void Tick(float dt, float timer01)
        {
            if (!Planted || Done) return;

            _blink += dt;
            float rate = Mathf.Lerp(10f, 1.6f, Mathf.Clamp01(timer01));
            float a = 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(_blink * rate));
            Color c = new Color(1f, 0.25f, 0.2f, a);
            _mpb.SetColor("_Color", c);
            if (_ledRend != null) _ledRend.SetPropertyBlock(_mpb);

            _beepTimer -= dt;
            if (_beepTimer <= 0f)
            {
                float interval = Mathf.Lerp(0.14f, 0.95f, Mathf.Clamp01(timer01));
                _beepTimer = interval;
                if (GzAudio.I != null) GzAudio.I.Play(GzAudio.I.BombTick, Pos + Vector3.up * 0.3f, 0.65f, 1f);
            }
        }

        public void Exploded()
        {
            Done = true;
            SetVisible(false);
        }
    }
}
