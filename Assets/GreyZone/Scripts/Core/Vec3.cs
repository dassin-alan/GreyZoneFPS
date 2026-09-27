// GreyZone Core - minimal vector math. Y-up, same axis order as Unity. 1 unit = 1 meter.
using System;

namespace GreyZone.Core
{
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vec3 Zero { get { return new Vec3(0f, 0f, 0f); } }
        public static Vec3 Up { get { return new Vec3(0f, 1f, 0f); } }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vec3 operator -(Vec3 a) { return new Vec3(-a.x, -a.y, -a.z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.x * s, a.y * s, a.z * s); }
        public static Vec3 operator *(float s, Vec3 a) { return new Vec3(a.x * s, a.y * s, a.z * s); }
        public static Vec3 operator /(Vec3 a, float s) { return new Vec3(a.x / s, a.y / s, a.z / s); }

        public static float Dot(Vec3 a, Vec3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        public static float Distance(Vec3 a, Vec3 b) { return (a - b).Length(); }
        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) { return a + (b - a) * t; }

        public float Length() { return (float)Math.Sqrt(x * x + y * y + z * z); }
        public float LengthSq() { return x * x + y * y + z * z; }

        public Vec3 Normalized()
        {
            float len = Length();
            if (len <= 1e-6f) return Zero;
            float inv = 1f / len;
            return new Vec3(x * inv, y * inv, z * inv);
        }

        public override string ToString()
        {
            return "(" + x.ToString("0.###") + ", " + y.ToString("0.###") + ", " + z.ToString("0.###") + ")";
        }
    }
}
