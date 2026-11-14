// Matemática mínima con la convención de Unity, solo para probar BookGeometry fuera de Unity (tests/libro).
// Matemáticas mínimas con la misma convención de Unity (solo para probar BookGeometry fuera de Unity).
using System;
namespace UnityEngine
{
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0); public static Vector2 one => new Vector2(1, 1);
        public static Vector2 right => new Vector2(1, 0); public static Vector2 up => new Vector2(0, 1);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k); }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0); public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 right => new Vector3(1, 0, 0); public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1); public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator *(float k, Vector3 a) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator /(Vector3 a, float k) => new Vector3(a.x / k, a.y / k, a.z / k);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public float sqrMagnitude => x * x + y * y + z * z; public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-9f ? this / m : zero; } }
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
    public struct Quaternion { public float x, y, z, w; public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        static Quaternion Axis(Vector3 a, float deg) { float r = deg * Mathf.Deg2Rad * 0.5f; float s = (float)Math.Sin(r); return new Quaternion(a.x * s, a.y * s, a.z * s, (float)Math.Cos(r)); }
        // Unity: primero z, luego x, luego y.
        public static Quaternion Euler(float ex, float ey, float ez) => Axis(Vector3.up, ey) * Axis(Vector3.right, ex) * Axis(Vector3.forward, ez);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion q, Vector3 v) { var p = q * new Quaternion(v.x, v.y, v.z, 0) * new Quaternion(-q.x, -q.y, -q.z, q.w); return new Vector3(p.x, p.y, p.z); } }
    public static class Mathf { public const float PI = (float)Math.PI, Deg2Rad = PI / 180f;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v; public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f); public static float Max(float a, float b) => a > b ? a : b; public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b; public static float Abs(float a) => Math.Abs(a);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t); public static float Sin(float a) => (float)Math.Sin(a); public static float Cos(float a) => (float)Math.Cos(a);
        public static float Sqrt(float a) => (float)Math.Sqrt(a);
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); } }
}
