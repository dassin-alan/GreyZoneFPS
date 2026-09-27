// GreyZone - mesh building helper (vertex colours + flat-ish UVs, world-space tiling).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GreyZone.Game
{
    public class MeshBuilder
    {
        private readonly List<Vector3> _verts = new List<Vector3>(2048);
        private readonly List<Vector3> _norms = new List<Vector3>(2048);
        private readonly List<Vector2> _uvs = new List<Vector2>(2048);
        private readonly List<Color32> _cols = new List<Color32>(2048);
        private readonly List<int> _tris = new List<int>(4096);

        public const float Tile = 2f;   // world units per texture repeat

        public int VertexCount { get { return _verts.Count; } }
        public bool IsEmpty { get { return _verts.Count == 0; } }

        public void Clear()
        {
            _verts.Clear(); _norms.Clear(); _uvs.Clear(); _cols.Clear(); _tris.Clear();
        }

        /// <summary>Axis aligned (optionally yaw-rotated) box with per-face UVs scaled by world size.</summary>
        public void AddBox(Vector3 center, Vector3 size, Color color, float yawDeg)
        {
            Color32 c = color;
            float hx = Mathf.Max(0.001f, size.x * 0.5f);
            float hy = Mathf.Max(0.001f, size.y * 0.5f);
            float hz = Mathf.Max(0.001f, size.z * 0.5f);

            Vector3[] p = new Vector3[8];
            p[0] = new Vector3(-hx, -hy, -hz);
            p[1] = new Vector3(hx, -hy, -hz);
            p[2] = new Vector3(hx, -hy, hz);
            p[3] = new Vector3(-hx, -hy, hz);
            p[4] = new Vector3(-hx, hy, -hz);
            p[5] = new Vector3(hx, hy, -hz);
            p[6] = new Vector3(hx, hy, hz);
            p[7] = new Vector3(-hx, hy, hz);

            Quaternion q = yawDeg == 0f ? Quaternion.identity : Quaternion.Euler(0f, yawDeg, 0f);

            Vector2[] uv = BuildFaceUV(p, hx, hy, hz);

            AddFace(center, q, c, p[4], p[7], p[6], p[5], Vector3.up, uv[4], uv[7], uv[6], uv[5]);      // +Y
            AddFace(center, q, c, p[0], p[1], p[2], p[3], Vector3.down, uv[0], uv[1], uv[2], uv[3]);    // -Y
            AddFace(center, q, c, p[0], p[4], p[5], p[1], Vector3.back, uv[0], uv[4], uv[5], uv[1]);    // -Z
            AddFace(center, q, c, p[3], p[2], p[6], p[7], Vector3.forward, uv[3], uv[2], uv[6], uv[7]); // +Z
            AddFace(center, q, c, p[1], p[5], p[6], p[2], Vector3.right, uv[1], uv[5], uv[6], uv[2]);   // +X
            AddFace(center, q, c, p[0], p[3], p[7], p[4], Vector3.left, uv[0], uv[3], uv[7], uv[4]);    // -X
        }

        private Vector2[] BuildFaceUV(Vector3[] p, float hx, float hy, float hz)
        {
            Vector2[] uv = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                // project on the dominant plane of the vertex: cheap and continuous enough
                float u = (p[i].x + hx) / Tile;
                float v = (p[i].y + hy) / Tile;
                if (Mathf.Abs(p[i].y) > hy - 0.0001f) v = (p[i].z + hz) / Tile;
                uv[i] = new Vector2(u, v);
            }
            return uv;
        }

        private void AddFace(Vector3 center, Quaternion rot, Color32 c, Vector3 a, Vector3 b, Vector3 d, Vector3 e,
                             Vector3 n, Vector2 ua, Vector2 ub, Vector2 ud, Vector2 ue)
        {
            int baseIndex = _verts.Count;
            _verts.Add(center + rot * a); _norms.Add(rot * n); _uvs.Add(ua); _cols.Add(c);
            _verts.Add(center + rot * b); _norms.Add(rot * n); _uvs.Add(ub); _cols.Add(c);
            _verts.Add(center + rot * d); _norms.Add(rot * n); _uvs.Add(ud); _cols.Add(c);
            _verts.Add(center + rot * e); _norms.Add(rot * n); _uvs.Add(ue); _cols.Add(c);
            _tris.Add(baseIndex); _tris.Add(baseIndex + 1); _tris.Add(baseIndex + 2);
            _tris.Add(baseIndex); _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 3);
        }

        /// <summary>Double sided quad in world space (used for crude detail planes).</summary>
        public void AddQuad(Vector3 origin, Vector3 right, Vector3 up, Color color)
        {
            Color32 c = color;
            int baseIndex = _verts.Count;
            Vector3 n = Vector3.Cross(up, right).normalized;
            _verts.Add(origin); _norms.Add(n); _uvs.Add(new Vector2(0, 0)); _cols.Add(c);
            _verts.Add(origin + right); _norms.Add(n); _uvs.Add(new Vector2(1, 0)); _cols.Add(c);
            _verts.Add(origin + right + up); _norms.Add(n); _uvs.Add(new Vector2(1, 1)); _cols.Add(c);
            _verts.Add(origin + up); _norms.Add(n); _uvs.Add(new Vector2(0, 1)); _cols.Add(c);
            _tris.Add(baseIndex); _tris.Add(baseIndex + 1); _tris.Add(baseIndex + 2);
            _tris.Add(baseIndex); _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 3);
        }

        public Mesh Build(string name)
        {
            Mesh m = new Mesh();
            m.name = name;
            if (_verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(_verts);
            m.SetNormals(_norms);
            m.SetUVs(0, _uvs);
            m.SetColors(_cols);
            m.SetTriangles(_tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Vertex coloured primitives that are not built by MeshBuilder (used for props).</summary>
        public static Mesh MakeUnitQuad()
        {
            Vector3[] v = new Vector3[4];
            v[0] = new Vector3(-0.5f, -0.5f, 0f);
            v[1] = new Vector3(0.5f, -0.5f, 0f);
            v[2] = new Vector3(0.5f, 0.5f, 0f);
            v[3] = new Vector3(-0.5f, 0.5f, 0f);
            Vector2[] uv = new Vector2[4];
            uv[0] = new Vector2(0, 0); uv[1] = new Vector2(1, 0); uv[2] = new Vector2(1, 1); uv[3] = new Vector2(0, 1);
            Color32[] col = new Color32[4];
            col[0] = col[1] = col[2] = col[3] = new Color32(255, 255, 255, 255);
            int[] t = new int[] { 0, 1, 2, 0, 2, 3 };
            Mesh m = new Mesh();
            m.name = "gz_quad";
            m.vertices = v; m.uv = uv; m.colors32 = col; m.triangles = t;
            m.normals = new Vector3[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            m.RecalculateBounds();
            return m;
        }

        public static Mesh MakeSphere(int segments, int rings)
        {
            segments = Mathf.Max(4, segments);
            rings = Mathf.Max(3, rings);
            Mesh m = new Mesh();
            m.name = "gz_sphere";
            int vCount = (rings + 1) * (segments + 1);
            Vector3[] verts = new Vector3[vCount];
            Vector2[] uvs = new Vector2[vCount];
            Color32[] cols = new Color32[vCount];
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                float y = Mathf.Cos(phi) * 0.5f;
                float rad = Mathf.Sin(phi) * 0.5f;
                for (int s = 0; s <= segments; s++)
                {
                    float theta = Mathf.PI * 2f * s / segments;
                    int i = r * (segments + 1) + s;
                    verts[i] = new Vector3(Mathf.Cos(theta) * rad, y, Mathf.Sin(theta) * rad);
                    uvs[i] = new Vector2((float)s / segments, (float)r / rings);
                    cols[i] = new Color32(255, 255, 255, 255);
                }
            }
            int[] tris = new int[rings * segments * 6];
            int ti = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s;
                    int b = a + segments + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            }
            m.vertices = verts; m.uv = uvs; m.colors32 = cols; m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
