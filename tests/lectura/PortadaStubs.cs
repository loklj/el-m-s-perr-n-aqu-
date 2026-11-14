// Contratos de presentación: el emparejamiento, navegación y corutinas se toman del código real.
using System;using System.Collections;using System.Collections.Generic;
namespace UnityEngine {
 public class ContextMenu:Attribute {public ContextMenu(string s){}}
 public struct Color {public float r,g,b;public Color(float r,float g,float b){this.r=r;this.g=g;this.b=b;}}
 public class Texture2D {public int width=100,height=140,Page;public bool Destroyed;public static Texture2D whiteTexture=new Texture2D{Page=-1};}
 public class Material {public Texture2D mainTexture;public Color color;}
 public class Transform {public Scale localScale=new Scale();}public class Scale {public float x=1,y=1;}
 public class Renderer {public bool enabled;public Material material=new Material();public Transform transform=new Transform();}
 public static class PlayerPrefs {static Dictionary<string,int> values=new Dictionary<string,int>();public static int GetInt(string k,int d)=>values.TryGetValue(k,out var v)?v:d;public static void SetInt(string k,int v){values[k]=v;}public static void Save(){}}
 public static class Time {public static float realtimeSinceStartup;}
 public static class Debug {public static void Log(string s){}}
}
namespace FolioXR.Accessibility {public static class FolioAccessibility {public static bool ReduceMotion;}}
namespace FolioXR.Reading {
 public class PdfResult {public bool Ok;}
 public class PdfPageRenderer {public string PdfPath="test.pdf";public static bool IsRealRendererAvailable=true;public List<int> Requests=new List<int>();public int Fail=-1;public Action DuringRender;public IEnumerator RenderPage(int page,int width,Action<UnityEngine.Texture2D,PdfResult> done){Requests.Add(page);var once=DuringRender;DuringRender=null;once?.Invoke();done(page==Fail?null:new UnityEngine.Texture2D{Page=page},new PdfResult{Ok=page!=Fail});yield break;}}
 public class PageTurnEffect {public static int Turns;public static PageTurnEffect Create(UnityEngine.Renderer r)=>new PageTurnEffect();public IEnumerator Play(bool left,UnityEngine.Texture2D first,UnityEngine.Texture2D second,float w,float h,float gutter,float tilt=0f){Turns++;yield break;}}
 public class BookBody {public float TiltRadians=>0.157f;}
 public class BookCover {public void SetRightToLeft(bool b){}}
 public static class ReadingDiagnostics {public static void Record(string key,double value){}}
 public static class PageTheme {public static bool Night;public static bool NeedsRemap(string p)=>Night;public static UnityEngine.Color Tint(string p)=>new UnityEngine.Color(1,1,1);}
 public sealed partial class FolioBook {
  private const string RtlPrefsPrefix="folioxr.rtl.";
  public bool SingleCover{get;private set;}public bool RightToLeft{get;private set;}public string DocumentId{get;private set;}
  private BookSpread _shownSpread;private bool _spreadReady,_spreadLayoutDirty,_busy,_resolutionDirty;
  private int _leftIndex,_pageCount,_renderedWidth;private float _aspectHeightOverWidth,gutter;
  private string _lastPageStatus;private UnityEngine.Texture2D _leftTexture,_rightTexture;private PageTurnEffect _turn;
  public BookBody Body;  public bool Closed;public void OpenBook(){Closed=false;}public void CloseBook(){Closed=true;}private const string ClosedStatus="";
  public float BookScale=1;public BookCover Cover;private PdfPageRenderer _renderer=new PdfPageRenderer();
  public UnityEngine.Renderer leftPage=new UnityEngine.Renderer(),rightPage=new UnityEngine.Renderer();
  public event Action<int,int> SpreadShown;public int Progress=-1;public List<int> Requests=>_renderer.Requests;
  public bool Busy=>_busy;public void Inject(Action a){_renderer.DuringRender=a;}public void FailPage(int p){_renderer.Fail=p;}
  public FolioBook(string id,int count,bool rtl=false){DocumentId=id;_pageCount=count;SingleCover=GetSingleCover(id);RightToLeft=rtl;SpreadShown+=(p,n)=>Progress=p;}
  private int DesiredPageWidth()=>100;private void ApplyLayout(){}private void SetStatus(string s){}private void ShowError(PdfResult r){}
  private void Destroy(UnityEngine.Texture2D t){if(t==UnityEngine.Texture2D.whiteTexture)throw new Exception("Destruyo textura global");t.Destroyed=true;}
  private void StartCoroutine(IEnumerator e){Run(e);}private static void Run(IEnumerator e){while(e.MoveNext())if(e.Current is IEnumerator nested)Run(nested);}
  public void RefreshPending(){if(_spreadLayoutDirty&&!_busy&&_spreadReady)RefreshSpreadLayout();}
 }
}
