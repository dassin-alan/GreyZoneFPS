// GreyZone - procedural greybox map "de_compound": merged chunk meshes + standalone box colliders,
// spawn points, bombsites, waypoint graph. All geometry is generated at runtime.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GreyZone.Game
{
    public class BombSite : MonoBehaviour
    {
        public string SiteName = "A";
        public Bounds Area;

        public bool Contains(Vector3 p)
        {
            return Area.Contains(p);
        }
    }

    public class MapData
    {
        public Transform Root;
        public Vector3[] SpawnsT = new Vector3[5];
        public Vector3[] SpawnsCT = new Vector3[5];
        public BombSite SiteA;
        public BombSite SiteB;
        public WaypointGraph Graph;
        public Vector3[] HoldA;      // bot anchor points around A
        public Vector3[] HoldB;      // bot anchor points around B
        public Vector3[] HoldMid;    // mid anchors
        public int SolidCount;
        public int ChunkCount;
        public Bounds Bounds;

        public BombSite SiteOf(Vector3 p)
        {
            if (SiteA != null && SiteA.Contains(p)) return SiteA;
            if (SiteB != null && SiteB.Contains(p)) return SiteB;
            return null;
        }
    }

    /// <summary>Destroys runtime generated meshes when the map root goes away.</summary>
    public class MapCleanup : MonoBehaviour
    {
        public Mesh[] Meshes;
        private void OnDestroy()
        {
            if (Meshes == null) return;
            for (int i = 0; i < Meshes.Length; i++)
            {
                if (Meshes[i] != null) Destroy(Meshes[i]);
            }
        }
    }

    public static class MapBuilder
    {
        private const float MinX = -33f, MaxX = 33f, MinZ = -23f, MaxZ = 23f;
        private static MeshBuilder[] _chunks;
        private static List<GameObject> _solids;
        private static Transform _root;
        private static Mesh[] _meshes;

        // ------------------------------------------------------------------ entry point
        public static MapData Build(Transform parent)
        {
            _root = parent;
            _chunks = new MeshBuilder[12];
            for (int i = 0; i < 12; i++) _chunks[i] = new MeshBuilder();
            _solids = new List<GameObject>(160);

            BuildFloor();
            BuildPerimeter();
            BuildTHall();
            BuildWestTunnel();
            BuildEastLong();
            BuildMid();
            BuildBYard();
            BuildCenter();
            BuildAWarehouse();
            BuildCtSpawn();

            MapData data = new MapData();
            data.Root = _root;
            data.Bounds = new Bounds(new Vector3(0f, 10f, 0f), new Vector3(66f, 20f, 46f));

            FinishChunks();

            // ---- bombsites
            data.SiteA = MakeSite("A", new Vector3(20f, 0f, 11f), new Vector3(12f, 4f, 9f));
            data.SiteB = MakeSite("B", new Vector3(-22f, 0f, 11f), new Vector3(13f, 4f, 9f));

            // ---- spawn points
            data.SpawnsT[0] = new Vector3(-14f, 0f, -19f);
            data.SpawnsT[1] = new Vector3(-7f, 0f, -19f);
            data.SpawnsT[2] = new Vector3(0f, 0f, -19.5f);
            data.SpawnsT[3] = new Vector3(7f, 0f, -19f);
            data.SpawnsT[4] = new Vector3(14f, 0f, -19f);
            data.SpawnsCT[0] = new Vector3(-14f, 0f, 20.5f);
            data.SpawnsCT[1] = new Vector3(-7f, 0f, 20.5f);
            data.SpawnsCT[2] = new Vector3(0f, 0f, 20.5f);
            data.SpawnsCT[3] = new Vector3(7f, 0f, 20.5f);
            data.SpawnsCT[4] = new Vector3(14f, 0f, 20.5f);

            // ---- waypoints (53 nodes)
            Vector3[] pts = Waypoints();
            byte[] areas = WaypointAreas();
            data.Graph = new WaypointGraph();
            data.Graph.Build(pts, areas);

            data.HoldA = new Vector3[] { new Vector3(20f, 0f, 11f), new Vector3(14f, 0f, 8f), new Vector3(27f, 0f, 8f), new Vector3(20f, 0f, 16f) };
            data.HoldB = new Vector3[] { new Vector3(-22f, 0f, 11f), new Vector3(-27f, 0f, 7f), new Vector3(-17f, 0f, 15f), new Vector3(-26f, 0f, 16f) };
            data.HoldMid = new Vector3[] { new Vector3(0f, 0f, 0f), new Vector3(-6f, 0f, 10f), new Vector3(6f, 0f, 10f), new Vector3(20f, 0f, 0f), new Vector3(-20f, 0f, 0f) };

            data.SolidCount = _solids.Count;
            data.ChunkCount = 0;
            for (int i = 0; i < 12; i++) if (!_chunks[i].IsEmpty) data.ChunkCount++;

            Debug.Log("[GreyZone] map built: " + data.ChunkCount + " chunks, " + data.SolidCount + " colliders, " + data.Graph.Count + " waypoints.");
            return data;
        }

        // ------------------------------------------------------------------ helpers
        private static int ChunkIndex(Vector3 c)
        {
            float w = (MaxX - MinX) / 4f;
            float d = (MaxZ - MinZ) / 3f;
            int cx = Mathf.Clamp((int)((c.x - MinX) / w), 0, 3);
            int cz = Mathf.Clamp((int)((c.z - MinZ) / d), 0, 2);
            return cz * 4 + cx;
        }

        private static void Box(Vector3 center, Vector3 size, Color col, float yaw, bool solid, bool visual)
        {
            if (visual)
            {
                _chunks[ChunkIndex(center)].AddBox(center, size, col, yaw);
            }
            if (solid)
            {
                GameObject go = new GameObject("solid");
                go.layer = GZ.WorldLayer;
                go.transform.SetParent(_root, false);
                go.transform.position = center;
                if (yaw != 0f) go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                BoxCollider bc = go.AddComponent<BoxCollider>();
                bc.size = size;
                bc.isTrigger = false;
                _solids.Add(go);
            }
        }

        private static void Box(Vector3 center, Vector3 size, Color col, float yaw)
        {
            Box(center, size, col, yaw, true, true);
        }

        private static void WallZ(float z, float x0, float x1, float h, float th, Color c)
        {
            float a = Mathf.Min(x0, x1), b = Mathf.Max(x0, x1);
            Box(new Vector3((a + b) * 0.5f, h * 0.5f, z), new Vector3(b - a, h, th), c, 0f);
        }

        private static void WallX(float x, float z0, float z1, float h, float th, Color c)
        {
            float a = Mathf.Min(z0, z1), b = Mathf.Max(z0, z1);
            Box(new Vector3(x, h * 0.5f, (a + b) * 0.5f), new Vector3(th, h, b - a), c, 0f);
        }

        private static void Crate(float x, float z, float size, float y0)
        {
            Box(new Vector3(x, y0 + size * 0.5f, z), new Vector3(size, size, size), GZ.CCrate, 0f);
        }

        private static void CrateYaw(float x, float z, float size, float y0, float yaw)
        {
            Box(new Vector3(x, y0 + size * 0.5f, z), new Vector3(size, size, size), GZ.CCrate, yaw);
        }

        private static void Container(float x, float z, float w, float d, float h, float yaw)
        {
            Box(new Vector3(x, h * 0.5f, z), new Vector3(w, h, d), GZ.CSteel, yaw);
        }

        private static void Pillar(float x, float z, float h)
        {
            Box(new Vector3(x, h * 0.5f, z), new Vector3(1.0f, h, 1.0f), GZ.CSteel, 0f);
        }

        private static void Marker(Vector3 center, Vector3 size, Color c)
        {
            Box(new Vector3(center.x, 0.02f, center.z), new Vector3(size.x, 0.04f, size.z), c, 0f, false, true);
        }

        // ------------------------------------------------------------------ geometry
        private static void BuildFloor()
        {
            float w = (MaxX - MinX) / 4f;
            float d = (MaxZ - MinZ) / 3f;
            for (int cx = 0; cx < 4; cx++)
            {
                for (int cz = 0; cz < 3; cz++)
                {
                    Vector3 c = new Vector3(MinX + w * (cx + 0.5f), -0.2f, MinZ + d * (cz + 0.5f));
                    Box(c, new Vector3(w, 0.4f, d), GZ.CFloor, 0f, false, true);
                }
            }
            Box(new Vector3(0f, -0.6f, 0f), new Vector3(68f, 1.2f, 48f), GZ.CFloor, 0f, true, false);
        }

        private static void BuildPerimeter()
        {
            WallZ(-22.5f, MinX, MaxX, 4.5f, 1.0f, GZ.CWall);
            WallZ(22.5f, MinX, MaxX, 4.5f, 1.0f, GZ.CWall);
            WallX(-32.5f, MinZ, MaxZ, 4.5f, 1.0f, GZ.CWall);
            WallX(32.5f, MinZ, MaxZ, 4.5f, 1.0f, GZ.CWall);

            // low trim / visual ledge for silhouette interest (non solid)
            Box(new Vector3(0f, 4.6f, -22.5f), new Vector3(68f, 0.3f, 1.4f), GZ.CDark, 0f, false, true);
            Box(new Vector3(0f, 4.6f, 22.5f), new Vector3(68f, 0.3f, 1.4f), GZ.CDark, 0f, false, true);
        }

        private static void BuildTHall()
        {
            // south hall: X in [-28,28], Z in [-22,-15]; three exits
            WallZ(-15f, -32f, -24f, 4f, 0.6f, GZ.CWall);
            WallZ(-15f, -20f, -3f, 4f, 0.6f, GZ.CWall);
            WallZ(-15f, 3f, 20f, 4f, 0.6f, GZ.CWall);
            WallZ(-15f, 24f, 32f, 4f, 0.6f, GZ.CWall);

            Crate(-24f, -18f, 1.2f, 0f);
            Crate(-23.4f, -18.8f, 0.9f, 0f);
            Crate(24f, -18f, 1.8f, 0f);
            Crate(25.2f, -17.2f, 0.9f, 0f);
            Marker(new Vector3(-27f, 0f, -19f), new Vector3(3f, 1f, 3f), GZ.CDark);
            Marker(new Vector3(27f, 0f, -19f), new Vector3(3f, 1f, 3f), GZ.CDark);
        }

        private static void BuildWestTunnel()
        {
            // dark covered passage X in [-32,-19], Z in [-15,3]
            WallX(-19f, -15f, -9f, 3.2f, 0.6f, GZ.CDark);
            WallX(-19f, -6f, 3f, 3.2f, 0.6f, GZ.CDark);
            Box(new Vector3(-25.5f, 3.35f, -6f), new Vector3(13f, 0.3f, 18.2f), GZ.CDark, 0f, true, true);
            Marker(new Vector3(-25.5f, 0f, -6f), new Vector3(13f, 1f, 18f), GZ.CDark);

            Crate(-30f, -10f, 1.8f, 0f);
            Crate(-29.2f, -8.9f, 1.2f, 0f);
            Crate(-21f, -11f, 1.2f, 0f);
            Crate(-21.5f, 0.5f, 1.8f, 0f);
            Crate(-20.7f, -0.4f, 0.9f, 0f);
            Pillar(-25.5f, -3f, 3.2f);
        }

        private static void BuildEastLong()
        {
            // long lane X in [19,32], Z in [-15,3]
            WallX(19f, -15f, -9f, 4f, 0.6f, GZ.CWall);
            WallX(19f, -6f, 3f, 4f, 0.6f, GZ.CWall);

            Crate(29f, -12f, 1.8f, 0f);
            Crate(28.1f, -11.1f, 0.9f, 0f);
            Crate(21.5f, -5f, 1.2f, 0f);
            Crate(21.5f, 1f, 1.8f, 0f);
            Marker(new Vector3(25f, 0f, -8f), new Vector3(4f, 1f, 3f), GZ.CDark);
        }

        private static void BuildMid()
        {
            // central hall X in [-19,19], Z in [-15,3]
            Pillar(-11f, -4f, 4f);
            Pillar(11f, -4f, 4f);
            Pillar(-11f, -11f, 4f);
            Pillar(11f, -11f, 4f);

            Crate(-4f, -10f, 1.8f, 0f);
            Crate(-4f, -10f, 1.2f, 1.8f);
            Crate(4f, -10f, 1.8f, 0f);
            Crate(4.8f, -10.8f, 0.9f, 0f);
            Crate(0f, -3f, 1.2f, 0f);
            Crate(-1.0f, -3.4f, 0.9f, 0f);
            Crate(0.9f, -3.6f, 0.9f, 0f);

            // mid doors wall (Z = 3), gaps at X in [-8,8]
            WallZ(3f, -19f, -8f, 4f, 0.6f, GZ.CWall);
            WallZ(3f, 8f, 19f, 4f, 0.6f, GZ.CWall);
        }

        private static void BuildBYard()
        {
            // B site yard X in [-32,-8], Z in [3,18]
            WallX(-8f, 3f, 9f, 4f, 0.6f, GZ.CWall);
            WallX(-8f, 12f, 18f, 4f, 0.6f, GZ.CWall);
            WallZ(18f, -32f, -20f, 4f, 0.6f, GZ.CWall);
            WallZ(18f, -16f, -8f, 4f, 0.6f, GZ.CWall);

            Container(-28f, 6f, 3.0f, 2.6f, 2.6f, 0f);
            Container(-22f, 15f, 3.0f, 2.6f, 2.6f, 0f);
            Container(-13.5f, 6.5f, 2.6f, 3.0f, 2.6f, 0f);
            Container(-29.5f, 13f, 2.6f, 3.0f, 2.6f, 0f);

            Crate(-24f, 11f, 1.8f, 0f);
            Crate(-24f, 11f, 1.2f, 1.8f);
            Crate(-24.9f, 10.2f, 0.9f, 0f);
            Crate(-20.5f, 8.5f, 1.2f, 0f);
            Crate(-18.5f, 12.5f, 1.8f, 0f);
            Crate(-19.4f, 13.3f, 0.9f, 0f);
            Crate(-26f, 16.5f, 1.2f, 0f);
            Crate(-15f, 16f, 1.2f, 0f);

            Marker(new Vector3(-22f, 0f, 11f), new Vector3(11f, 1f, 8f), GZ.CSite);
        }

        private static void BuildCenter()
        {
            Pillar(-4f, 8f, 4f);
            Pillar(4f, 8f, 4f);
            Crate(0f, 6.5f, 1.8f, 0f);
            Crate(0f, 6.5f, 1.2f, 1.8f);
            Crate(-1f, 13f, 1.2f, 0f);
            Crate(6.5f, 14f, 1.8f, 0f);
            Crate(6.5f, 14f, 0.9f, 1.8f);
            Crate(-6.5f, 6.5f, 1.2f, 0f);
        }

        private static void BuildAWarehouse()
        {
            // A warehouse X in [8,32], Z in [3,18]
            WallX(8f, 3f, 9f, 4.5f, 0.6f, GZ.CWall);
            WallX(8f, 12f, 18f, 4.5f, 0.6f, GZ.CWall);
            WallZ(18f, 8f, 24f, 4.5f, 0.6f, GZ.CWall);
            WallZ(18f, 28f, 32f, 4.5f, 0.6f, GZ.CWall);
            WallZ(3f, 24f, 32f, 4.5f, 0.6f, GZ.CWall);

            Container(29f, 5.5f, 3.0f, 2.6f, 2.6f, 0f);
            Container(11f, 16f, 3.0f, 2.6f, 2.6f, 0f);

            Crate(20f, 11f, 1.8f, 0f);
            Crate(20f, 11f, 1.2f, 1.8f);
            Crate(20.9f, 10.1f, 0.9f, 0f);
            Crate(15f, 8f, 1.2f, 0f);
            Crate(16f, 8.6f, 0.9f, 0f);
            Crate(25f, 8.5f, 1.8f, 0f);
            Crate(25f, 8.5f, 0.9f, 1.8f);
            Crate(13f, 13f, 1.2f, 0f);
            Crate(27f, 14.5f, 1.2f, 0f);
            Crate(17f, 4.5f, 1.2f, 0f);

            // 0.4 m loading platform (two 0.2 m steps, no ramps)
            Box(new Vector3(29f, 0.2f, 12f), new Vector3(6f, 0.4f, 6f), GZ.CDark, 0f, true, true);
            Box(new Vector3(25.6f, 0.1f, 12f), new Vector3(0.8f, 0.2f, 3f), GZ.CDark, 0f, true, true);

            Marker(new Vector3(20f, 0f, 11f), new Vector3(11f, 1f, 8f), GZ.CSite);
        }

        private static void BuildCtSpawn()
        {
            Crate(-6f, 20f, 1.2f, 0f);
            Crate(6f, 20f, 1.2f, 0f);
            Crate(-22f, 20.5f, 1.8f, 0f);
            Crate(26f, 20.5f, 1.8f, 0f);
            Crate(-21.1f, 19.6f, 0.9f, 0f);
            Crate(26.9f, 19.6f, 0.9f, 0f);
            Marker(new Vector3(0f, 0f, 20f), new Vector3(58f, 1f, 2.5f), GZ.CDark);
        }

        // ------------------------------------------------------------------ finish
        private static void FinishChunks()
        {
            _meshes = new Mesh[12];
            GameObject holder = new GameObject("MapChunks");
            holder.layer = GZ.WorldLayer;
            holder.transform.SetParent(_root, false);
            MapCleanup cleanup = holder.AddComponent<MapCleanup>();

            for (int i = 0; i < 12; i++)
            {
                if (_chunks[i].IsEmpty) continue;
                GameObject go = new GameObject("chunk_" + i);
                go.layer = GZ.WorldLayer;
                go.transform.SetParent(holder.transform, false);
                Mesh m = _chunks[i].Build("gz_chunk_" + i);
                _meshes[i] = m;
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = m;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ProcAssets.MapMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            cleanup.Meshes = _meshes;
        }

        private static BombSite MakeSite(string name, Vector3 center, Vector3 size)
        {
            GameObject go = new GameObject("BombSite_" + name);
            go.layer = GZ.WorldLayer;
            go.transform.SetParent(_root, false);
            go.transform.position = center;
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            bc.center = new Vector3(0f, size.y * 0.5f, 0f);
            bc.isTrigger = true;
            BombSite site = go.AddComponent<BombSite>();
            site.SiteName = name;
            site.Area = new Bounds(center + new Vector3(0f, size.y * 0.5f, 0f), size);

            // corner posts for readability
            float hx = size.x * 0.5f - 0.15f;
            float hz = size.z * 0.5f - 0.15f;
            Color c = GZ.CSite;
            Box(new Vector3(center.x - hx, 0.45f, center.z - hz), new Vector3(0.16f, 0.9f, 0.16f), c, 0f, false, true);
            Box(new Vector3(center.x + hx, 0.45f, center.z - hz), new Vector3(0.16f, 0.9f, 0.16f), c, 0f, false, true);
            Box(new Vector3(center.x - hx, 0.45f, center.z + hz), new Vector3(0.16f, 0.9f, 0.16f), c, 0f, false, true);
            Box(new Vector3(center.x + hx, 0.45f, center.z + hz), new Vector3(0.16f, 0.9f, 0.16f), c, 0f, false, true);
            return site;
        }

        // ------------------------------------------------------------------ waypoints
        private static Vector3[] Waypoints()
        {
            Vector3[] p = new Vector3[]
            {
                // T hall (0-4)
                new Vector3(-24f, 0f, -18f), new Vector3(-12f, 0f, -18f), new Vector3(0f, 0f, -18.5f),
                new Vector3(12f, 0f, -18f), new Vector3(24f, 0f, -18f),
                // T hall exits (5-7)
                new Vector3(-22f, 0f, -12f), new Vector3(0f, 0f, -12f), new Vector3(22f, 0f, -12f),
                // west tunnel (8-10)
                new Vector3(-25f, 0f, -8f), new Vector3(-25f, 0f, -2f), new Vector3(-25f, 0f, 1f),
                // east long (11-13)
                new Vector3(25f, 0f, -8f), new Vector3(25f, 0f, -2f), new Vector3(25f, 0f, 1f),
                // mid east-west (14-18)
                new Vector3(-14f, 0f, -8f), new Vector3(-8f, 0f, -8f), new Vector3(0f, 0f, -8f),
                new Vector3(8f, 0f, -8f), new Vector3(14f, 0f, -8f),
                // mid north band (19-21)
                new Vector3(-12f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(12f, 0f, 0f),
                // doorway nodes (22-23)
                new Vector3(-18f, 0f, -7f), new Vector3(18f, 0f, -7f),
                // mid doors (24-25)
                new Vector3(-4f, 0f, 4f), new Vector3(4f, 0f, 4f),
                // B yard (26-31)
                new Vector3(-28f, 0f, 6f), new Vector3(-22f, 0f, 11f), new Vector3(-28f, 0f, 15f),
                new Vector3(-20f, 0f, 16f), new Vector3(-16f, 0f, 8f), new Vector3(-11f, 0f, 12f),
                // B east doorway (32)
                new Vector3(-6f, 0f, 10f),
                // center plaza (33-36)
                new Vector3(0f, 0f, 7f), new Vector3(0f, 0f, 12f), new Vector3(-4f, 0f, 16f), new Vector3(4f, 0f, 16f),
                // A warehouse (37-43)
                new Vector3(10f, 0f, 7f), new Vector3(13f, 0f, 12f), new Vector3(20f, 0f, 11f),
                new Vector3(27f, 0f, 7f), new Vector3(28f, 0f, 15f), new Vector3(20f, 0f, 16f),
                new Vector3(25f, 0f, 5f),
                // A north doorway + CT/B gap (44-45)
                new Vector3(26f, 0f, 19f), new Vector3(-18f, 0f, 19f),
                // CT spawn (46-50)
                new Vector3(-26f, 0f, 20f), new Vector3(-12f, 0f, 20f), new Vector3(0f, 0f, 20f),
                new Vector3(12f, 0f, 20f), new Vector3(24f, 0f, 20f),
                // center north (51), A west doorway (52)
                new Vector3(0f, 0f, 17f), new Vector3(8f, 0f, 10f)
            };
            return p;
        }

        private static byte[] WaypointAreas()
        {
            byte T = WpArea.TSpawn, K = WpArea.Tunnel, M = WpArea.Mid, L = WpArea.Long;
            byte B = WpArea.Byard, C = WpArea.Center, A = WpArea.Awarehouse, S = WpArea.CTspawn;
            return new byte[]
            {
                T, T, T, T, T,
                T, M, T,
                K, K, K,
                L, L, L,
                M, M, M, M, M,
                M, M, M,
                K, L,
                C, C,
                B, B, B, B, B, B,
                C,
                C, C, C, C,
                A, A, A, A, A, A, A,
                S, S,
                S, S, S, S, S,
                C, A
            };
        }
    }
}
