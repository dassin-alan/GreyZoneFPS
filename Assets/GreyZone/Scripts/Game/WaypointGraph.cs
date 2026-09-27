// GreyZone - waypoint graph with adjacency built from capsule sweeps + A* pathfinding (zero GC at runtime).
using System.Collections.Generic;
using UnityEngine;

namespace GreyZone.Game
{
    public static class WpArea
    {
        public const byte None = 0;
        public const byte TSpawn = 1;
        public const byte Tunnel = 2;
        public const byte Mid = 3;
        public const byte Long = 4;
        public const byte Byard = 5;
        public const byte Center = 6;
        public const byte Awarehouse = 7;
        public const byte CTspawn = 8;
        public const byte Count = 9;
    }

    public class WaypointGraph
    {
        public struct Node
        {
            public Vector3 Pos;
            public byte Area;
            public int First;
            public int Count;
        }

        public Node[] Nodes;
        public int[] Edges;          // flattened adjacency indices

        private float[] _g;
        private int[] _from;
        private bool[] _closed;
        private int[] _queue;
        private int[] _pathScratch;

        public int Count { get { return Nodes == null ? 0 : Nodes.Length; } }

        private const float LinkRange = 14f;
        private static readonly RaycastHit[] ProbeBuf = new RaycastHit[8];

        public void Build(Vector3[] pts, byte[] areas)
        {
            int n = pts.Length;
            Nodes = new Node[n];
            for (int i = 0; i < n; i++)
            {
                Nodes[i].Pos = pts[i];
                Nodes[i].Area = areas[i];
            }

            List<int>[] adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>(8);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float d = Vector3.Distance(pts[i], pts[j]);
                    if (d > LinkRange) continue;
                    if (!Clear(pts[i], pts[j])) continue;
                    adj[i].Add(j);
                    adj[j].Add(i);
                }
            }

            EnsureConnected(pts, adj);

            int total = 0;
            for (int i = 0; i < n; i++) { adj[i].Sort(); total += adj[i].Count; }
            Edges = new int[total];
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                Nodes[i].First = k;
                Nodes[i].Count = adj[i].Count;
                for (int e = 0; e < adj[i].Count; e++) Edges[k++] = adj[i][e];
            }

            _g = new float[n];
            _from = new int[n];
            _closed = new bool[n];
            _queue = new int[n];
            _pathScratch = new int[64];
        }

        private static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float dist = d.magnitude;
            if (dist < 0.001f) return true;
            Vector3 dir = d / dist;
            Vector3 p1 = new Vector3(a.x, a.y + 0.45f, a.z);
            Vector3 p2 = new Vector3(a.x, a.y + 1.35f, a.z);
            Vector3 q1 = new Vector3(b.x, b.y + 0.45f, b.z);
            Vector3 q2 = new Vector3(b.x, b.y + 1.35f, b.z);
            RaycastHit[] buf = ProbeBuf;

            // first check the end position is free, then sweep
            if (Physics.CheckCapsule(q1, q2, 0.34f, GZ.SolidMask, QueryTriggerInteraction.Ignore))
                return false;
            int hitCount = Physics.CapsuleCastNonAlloc(p1, p2, 0.34f, dir, buf, dist - 0.05f, GZ.SolidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                if (buf[i].distance <= 0.02f) continue;   // starting overlap = self/adjacent, ignore
                return false;
            }
            return true;
        }

        private static void EnsureConnected(Vector3[] pts, List<int>[] adj)
        {
            int n = pts.Length;
            bool[] visited = new bool[n];
            int[] stack = new int[n];
            int sp = 0;
            visited[0] = true;
            stack[sp++] = 0;
            int visitedCount = 1;
            while (sp > 0)
            {
                int cur = stack[--sp];
                List<int> l = adj[cur];
                for (int e = 0; e < l.Count; e++)
                {
                    int nb = l[e];
                    if (visited[nb]) continue;
                    visited[nb] = true;
                    stack[sp++] = nb;
                    visitedCount++;
                }
            }
            if (visitedCount == n) return;

            Debug.LogWarning("[GreyZone] waypoint graph not connected (" + visitedCount + "/" + n + "), forcing links.");
            while (visitedCount < n)
            {
                int orphan = -1;
                float best = float.MaxValue;
                int bestVisited = -1;
                for (int i = 0; i < n; i++)
                {
                    if (visited[i]) continue;
                    for (int j = 0; j < n; j++)
                    {
                        if (!visited[j]) continue;
                        float d = Vector3.Distance(pts[i], pts[j]);
                        if (d < best && d < 22f)
                        {
                            best = d; orphan = i; bestVisited = j;
                        }
                    }
                    if (orphan >= 0) break;
                }
                if (orphan < 0)
                {
                    // nothing close by: hard-wire to node 0
                    orphan = 0;
                    for (int i = 0; i < n; i++) { if (!visited[i]) { orphan = i; break; } }
                    bestVisited = 0;
                }
                adj[orphan].Add(bestVisited);
                adj[bestVisited].Add(orphan);
                visited[orphan] = true;
                visitedCount++;
            }
        }

        public int NearestNode(Vector3 p, byte area)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                if (area != 255 && Nodes[i].Area != area) continue;
                float d = (Nodes[i].Pos - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0)
            {
                for (int i = 0; i < Count; i++)
                {
                    float d = (Nodes[i].Pos - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            return best;
        }

        /// <summary>A* (uniform cost, small graph). Returns path length in outPath (start..goal) or 0.</summary>
        public int FindPath(int start, int goal, int[] outPath)
        {
            int n = Count;
            if (n == 0 || start < 0 || goal < 0 || start >= n || goal >= n) return 0;
            if (start == goal)
            {
                if (outPath.Length > 0) outPath[0] = goal;
                return 1;
            }

            for (int i = 0; i < n; i++)
            {
                _g[i] = float.MaxValue;
                _from[i] = -1;
                _closed[i] = false;
            }
            _g[start] = 0f;

            int cur = start;
            int guard = 0;
            bool found = false;
            while (guard++ < 4096)
            {
                _closed[cur] = true;
                if (cur == goal) { found = true; break; }

                Node c = Nodes[cur];
                for (int e = 0; e < c.Count; e++)
                {
                    int nb = Edges[c.First + e];
                    if (_closed[nb]) continue;
                    float ng = _g[cur] + Vector3.Distance(c.Pos, Nodes[nb].Pos);
                    if (ng < _g[nb]) { _g[nb] = ng; _from[nb] = cur; }
                }

                cur = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (_closed[i]) continue;
                    if (_g[i] < best) { best = _g[i]; cur = i; }
                }
                if (cur < 0) break;
            }

            if (!found) return 0;

            int len = 0;
            int node = goal;
            while (node >= 0 && len < _pathScratch.Length)
            {
                _pathScratch[len++] = node;
                if (node == start) break;
                node = _from[node];
            }
            if (len == 0 || _pathScratch[len - 1] != start) return 0;

            int outLen = Mathf.Min(len, outPath.Length);
            for (int i = 0; i < outLen; i++) outPath[i] = _pathScratch[len - 1 - i];
            return outLen;
        }
    }
}
