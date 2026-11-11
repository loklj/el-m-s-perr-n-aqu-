// Stubs de contratos: no simulan el motor de dibujo ni sustituyen Unity.
using System;
using System.Collections.Generic;
namespace Unity.Collections { public struct NativeArray<T> { public T[] Data; public Span<T> AsSpan()=>Data; } }
namespace UnityEngine {
 public class Object { public static void Destroy(object value){} }
 public enum TextureFormat{RGBA32} public enum TextureWrapMode{Clamp} public enum FilterMode{Trilinear}
 public struct Color32 {public Color32(byte r,byte g,byte b,byte a){} }
 public class Texture2D {public int width,height,anisoLevel; public float mipMapBias; public TextureWrapMode wrapMode;public FilterMode filterMode;public byte[] Pixels;
 public Texture2D(int w,int h,TextureFormat f,bool mip){width=w;height=h;Pixels=new byte[w*h*4];}
 public Unity.Collections.NativeArray<T> GetPixelData<T>(int m)=>new Unity.Collections.NativeArray<T>{Data=(T[])(object)Pixels};
 public bool LoadImage(byte[] b,bool unreadable)=>true;public void SetPixels32(Color32[] p){}public void Apply(bool a=false,bool b=false){} }
 public static class Application{public static string temporaryCachePath=System.IO.Path.Combine(System.IO.Path.GetTempPath(), "folio-bridge-"+Guid.NewGuid().ToString("N"));}
 public static class Time{public static int frameCount; public static float realtimeSinceStartup;}
 public static class PlayerPrefs{public static float GetFloat(string k,float d)=>d;public static string GetString(string k,string d)=>d;}
 public static class Mathf{public static int Clamp(int x,int a,int b)=>Math.Min(b,Math.Max(a,x));public static float Clamp(float x,float a,float b)=>Math.Min(b,Math.Max(a,x));public static int RoundToInt(float x)=>(int)Math.Round(x);}
 public class AndroidJavaObject:IDisposable {public void Dispose(){} }
 public class AndroidJavaClass:IDisposable {public static List<string> Created=new List<string>();public AndroidJavaClass(string n){Created.Add(n);}public T GetStatic<T>(string n)=> (T)(object)new AndroidJavaObject();public void CallStatic(string n,params object[] a){}public T CallStatic<T>(string n,params object[] a)=>default;public void Dispose(){} }
}
