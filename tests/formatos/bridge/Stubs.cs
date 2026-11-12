// Stubs de contratos: no simulan el motor de dibujo ni sustituyen Unity.
using System;
using System.Collections.Generic;
namespace Unity.Collections { public struct NativeArray<T> { public T[] Data; public Span<T> AsSpan()=>Data; } }
namespace UnityEngine {
 public class Object { public static void Destroy(object value){} }
 public enum TextureFormat{RGBA32,RGB24,ARGB32} public enum TextureWrapMode{Clamp} public enum FilterMode{Trilinear}
 public struct Color {public Color(float r,float g,float b){}public static Color white=>new Color(1,1,1);}
 public static class JsonUtility {public static T FromJson<T>(string s)=>System.Text.Json.JsonSerializer.Deserialize<T>(s,new System.Text.Json.JsonSerializerOptions{IncludeFields=true});}
 public static class Debug {public static void Log(string s){}public static void LogWarning(string s){}}
 public struct Color32 {public Color32(byte r,byte g,byte b,byte a){} }
 public class Texture2D {public TextureFormat format;public int width,height,anisoLevel; public float mipMapBias; public TextureWrapMode wrapMode;public FilterMode filterMode;public byte[] Pixels;
 public Texture2D(int w,int h,TextureFormat f,bool mip){width=w;height=h;format=f;Pixels=new byte[w*h*4];}
 public Unity.Collections.NativeArray<T> GetPixelData<T>(int m)=>new Unity.Collections.NativeArray<T>{Data=(T[])(object)Pixels};
 public bool LoadImage(byte[] b,bool unreadable)=>true;public void SetPixels32(Color32[] p){}public void Apply(bool a=false,bool b=false){} }
 public static class Application{public static string persistentDataPath="/tmp";public static string temporaryCachePath=System.IO.Path.Combine(System.IO.Path.GetTempPath(), "folio-bridge-"+Guid.NewGuid().ToString("N"));}
 public static class Time{public static int frameCount; public static float realtimeSinceStartup,unscaledTime;public static double realtimeSinceStartupAsDouble;}
 public static class PlayerPrefs{static Dictionary<string,int> ints=new Dictionary<string,int>();public static int GetInt(string k,int d)=>ints.TryGetValue(k,out int v)?v:d;public static void SetInt(string k,int v){ints[k]=v;}public static void Save(){}static Dictionary<string,float> floats=new Dictionary<string,float>();static Dictionary<string,string> strings=new Dictionary<string,string>();public static void SetFloat(string k,float v){floats[k]=v;}public static float GetFloat(string k,float d)=>floats.TryGetValue(k,out float v)?v:d;public static void SetString(string k,string v){strings[k]=v;}public static string GetString(string k,string d)=>strings.TryGetValue(k,out string v)?v:d;}
 public static class Mathf{public static int Abs(int n)=>Math.Abs(n);public static int Min(int a,int b)=>Math.Min(a,b);public static int Max(int a,int b)=>Math.Max(a,b);public static bool Approximately(float a,float b)=>Math.Abs(a-b)<0.00001;public static int Clamp(int x,int a,int b)=>Math.Min(b,Math.Max(a,x));public static float Clamp(float x,float a,float b)=>Math.Min(b,Math.Max(a,x));public static int RoundToInt(float x)=>(int)Math.Round(x);}
 public class AndroidJavaObject:IDisposable {public void Dispose(){} }
 public class AndroidJavaClass:IDisposable {public static Func<string,object[],object> Handler;public static List<(string method,object[] args)> Calls=new List<(string,object[])>();public static List<string> Created=new List<string>();public AndroidJavaClass(string n){Created.Add(n);}public T GetStatic<T>(string n)=> (T)(object)new AndroidJavaObject();public void CallStatic(string n,params object[] a){Calls.Add((n,a));Handler?.Invoke(n,a);}public T CallStatic<T>(string n,params object[] a){Calls.Add((n,a));object r=Handler?.Invoke(n,a);return r==null?default:(T)r;}public void Dispose(){} }
}
