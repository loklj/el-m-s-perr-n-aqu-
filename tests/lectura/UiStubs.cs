// Tipos externos al modelo real: no simulan Unity ni SQLite.
using System;
using System.Collections;
using System.Collections.Generic;
namespace UnityEngine {
 public enum FindObjectsInactive{Include}public enum RuntimeInitializeLoadType{AfterSceneLoad}
 public class DefaultExecutionOrder:Attribute{public DefaultExecutionOrder(int i){}}
 public class RuntimeInitializeOnLoadMethodAttribute:Attribute{public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){}}
 public class Transform{}
 public class Coroutine{public IEnumerator iterator;}
 public class MonoBehaviour{public static void DontDestroyOnLoad(object value){}public static T FindFirstObjectByType<T>() where T:class=>null;public static T FindFirstObjectByType<T>(FindObjectsInactive f)where T:class=>null;public Coroutine StartCoroutine(IEnumerator e)=>new Coroutine{iterator=e};public void StopCoroutine(Coroutine c){} }
 public class GameObject{public GameObject(string n){}public T AddComponent<T>()where T:new()=>new T();}
 public enum TouchScreenKeyboardType{Default}
 public class TouchScreenKeyboard{public enum Status{Visible,Done,Canceled,LostFocus}public static bool isSupported=true;public static TouchScreenKeyboard Latest;public Status status;public string text;public static TouchScreenKeyboard Open(string text,TouchScreenKeyboardType t,bool a,bool b,bool c,bool d,string placeholder,int limit=0){return Latest=new TouchScreenKeyboard{text=text};}}
}
namespace FolioXR.Reading {
 public class BookCover{public static string[] LeatherNames={"a","b","c","d","e","f","g"};public void Apply(bool a,int b,string c,bool d){} }
 public class FolioBook:UnityEngine.MonoBehaviour{
  public const string UserPdfName="libro.pdf",BundledPdfName="demo.pdf";public const float MinRenderScale=1.4f,MaxRenderScale=2f,RenderScaleStep=.1f;
  public static bool LyingMode;public static void SetLyingMode(bool v){LyingMode=v;}public float RenderScale=1.4f;public bool RightToLeft,RealisticPhysics=true;public string DocumentId;public int Destination=-1,Reloads;public BookCover Cover=new BookCover();
  public event Action<int,int> SpreadShown;public event Action<string,string> DocumentFailed;
  public const string SingleCoverPrefsPrefix="folioxr.singleCover.";public static bool GetSingleCover(string id)=>UnityEngine.PlayerPrefs.GetInt(SingleCoverPrefsPrefix+id,0)!=0;public void SetSingleCoverFor(string id,bool value){UnityEngine.PlayerPrefs.SetInt(SingleCoverPrefsPrefix+id,value?1:0);}public static bool GetRightToLeft(string id)=>false;public void SetRightToLeftFor(string id,bool v){}public void SetRightToLeft(bool v){RightToLeft=v;}public void SetRenderScale(float v){RenderScale=v;}public void SetRealisticPhysics(bool v){RealisticPhysics=v;}public void ResetBookScale(){}public void PlaceInFrontOfView(){}public void RequestDocument(string p,string id,int page){Reloads++;}public void OpenDefaultDocument(){}public bool GoToPage(int n){Destination=n;return true;}public void CancelContents(){}public void CancelSearch(){}public IEnumerator Search(string query,string cursor,int limit,Action<SearchList,string> done){done(new SearchList(),null);yield break;}public IEnumerator ReadContents(Action<ContentsList,string> cb){cb(new ContentsList(),null);yield break;}
 }
}
namespace FolioXR.Environments {public class FolioEnvironmentController {public bool IsActive;public int ThemeIndex;public List<string> PointNames=new List<string>();public void Leave(){}public void SetTheme(int n){}public void GoToPoint(int n){}}public class EnvironmentTheme{public string Name;}public static class EnvironmentThemes{public static List<EnvironmentTheme> All=new List<EnvironmentTheme>();}}
namespace FolioXR.Tools {public class ToolRegistry{public static ToolRegistry Instance;public static void StoreAll(){}}}
namespace FolioXR.Tools.Ink {public class InkManager{public static InkManager Instance;public static bool HasHouse;public static void ClearHouse(){}public void ClearCurrentPage(){}public static void ClearVisibleBookPages(){}}}
namespace FolioXR.Locomotion {public static class FolioLocomotion{public static bool Jog,VignetteEnabled,SmoothTurn;public static void SetJog(bool b){Jog=b;}public static void SetVignette(bool b){VignetteEnabled=b;}public static void SetSmoothTurn(bool b){SmoothTurn=b;}}}
namespace FolioXR.Books {
 public class OpdsServer{public string id,name;}public class OpdsBook{public string FormatName;}public class OpdsEntry{public string title,nav;public bool IsNavigation;public OpdsBook Best;}public class OpdsFeed{public string title,next;public List<OpdsEntry> entries=new List<OpdsEntry>();}
 public class OpdsBrowser{public static OpdsBrowser Instance;public static event Action StateChanged;public bool Available,Downloading,Browsing,Loading;public string DownloadLine,Error;public int Depth,LocalPage;public const int EntriesPerPage=6;public List<OpdsServer> Servers=new List<OpdsServer>();public OpdsFeed Feed;public void OpenServer(OpdsServer s){}public void RemoveServer(OpdsServer s){}public void Back(){}public void OpenNav(string s){}public void Download(OpdsEntry e){}public void NextLocalPage(){}public void NextRemotePage(){}}
}
