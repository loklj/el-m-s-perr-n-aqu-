// Contratos nuevos de la base integrada; no sustituyen las pruebas del encargo 18.
using System;
using System.Collections.Generic;
namespace FolioXR.Accessibility {
 public static class FolioAccessibility {
  public static event Action Changed;
  public static bool LeftHanded,HighContrast,ReduceMotion,Captions;public static float MenuSize=1;
  public static void SetLeftHanded(bool b){}public static void SetHighContrast(bool b){}public static void SetReduceMotion(bool b){}public static void SetCaptions(bool b){}public static void SetMenuSize(float f){}
 }
 public class LicenseViewer {
  public static event Action Changed;public static LicenseViewer Instance;public static string Version="test";
  public bool Showing,Loading;public string Title;public int Page,PageCount;public List<string> Files=new List<string>();
  public void NextPage(){}public void PreviousPage(){}public void Close(){}public void Open(string f){}public void LoadIndex(){}
 }
}
namespace FolioXR.Books {public static class BookShelf {public static bool FreeStanding;public static void SetFreeStanding(bool b){}}}
