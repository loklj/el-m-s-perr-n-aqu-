using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FolioXR.Books;
using FolioXR.Menu;
using FolioXR.Reading;
using UnityEngine;
class UiTest {
 static int checks;
 static void Check(bool b,string s){if(!b)throw new Exception(s);checks++;}
 static void Property(object target,string name,object value)=>target.GetType().GetProperty(name).SetValue(target,value);
 static void Field(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
 static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(target,args);
 static void Main(){
  var library=new BookLibrary();typeof(BookLibrary).GetProperty("Instance").SetValue(null,library);Property(library,"Available",true);
  var entry=new BookEntry{id="uno",format="epub",path="uno.epub",title="Uno",pages=100,position=10};
  Property(library,"Current",entry);Field(library,"_books",new List<BookEntry>{entry});Field(library,"_java",new AndroidJavaClass("library"));
  var book=new FolioBook{DocumentId="uno"};Field(library,"_book",book);library.FinishSetup(entry);
  string json="{\"positions\":[10,20],\"entries\":[{\"position\":10,\"label\":\"Favorito\"},{\"position\":20,\"label\":\"\"}]}";
  AndroidJavaClass.Handler=(name,args)=>name=="bookmarksJson"?(object)json:name=="renameBookmark"||name=="removeBookmark"?(object)true:null;
  Call(library,"LoadBookmarks");Check(library.NamedBookmarks.Count==2,"Lista nominal");Check(library.BookmarkTitle(10)=="Favorito"&&library.BookmarkTitle(20)=="Página 21","Nombres antiguos sin etiqueta");
  var menu=new MenuModel(book,new FolioXR.Environments.FolioEnvironmentController(),()=>false,b=>{});
  menu.Select(MenuCategory.Ajustes);
  Check(menu.Options.Last().Text.StartsWith("Modo diagnóstico:"),"Diagnóstico al final");
  menu.Invoke(menu.Options.Count-1);Check(ReadingDiagnostics.Enabled,"Diagnóstico activado");
  menu.Invoke(menu.Options.Count-1);Check(!ReadingDiagnostics.Enabled,"Diagnóstico desactivado");
  menu.Invoke(menu.Options.ToList().FindIndex(x=>x.Text=="Lectura"));
  string[] old=menu.Options.Select(x=>x.Text).ToArray();Check(old[old.Length-3].StartsWith("Fuente:")&&old[old.Length-1].StartsWith("Márgenes:"),"Opciones anexadas");
  int font=menu.Options.ToList().FindIndex(x=>x.Text.StartsWith("Fuente:"));menu.Invoke(font);
  Check(ReadingLayout.Font==1&&book.Reloads==1&&library.Repaginating,"Preferencia y recarga");
  Check(menu.Options[font].Text.Contains("Atkinson"),"Texto actualizado al cambiar ajuste");
  Property(library,"Repaginating",false);library.SetReadingLayout(0,1.8f,72);Check(ReadingLayout.LineSpacing==1.8f&&ReadingLayout.Margin==72,"Preferencias independientes");Property(library,"Repaginating",false);
  menu.Select(MenuCategory.Biblioteca);menu.Invoke(menu.Options.ToList().FindIndex(x=>x.Text=="Lista de marcadores"));
  Check(menu.Options.Any(x=>x.Text.Contains("Favorito")),"Lista accesible");
  menu.Invoke(menu.Options.ToList().FindIndex(x=>x.Text.StartsWith("Nombrar marcador")));
  Check(TouchScreenKeyboard.Latest.text=="Favorito","Teclado nombre actual");
  TouchScreenKeyboard.Latest.text="Nuevo nombre";TouchScreenKeyboard.Latest.status=TouchScreenKeyboard.Status.Done;menu.Tick();
  var rename=AndroidJavaClass.Calls.Last(x=>x.method=="renameBookmark");Check((string)rename.args[0]=="uno"&&(int)rename.args[1]==10&&(string)rename.args[2]=="Nuevo nombre","Captura estable de libro/pagina");
  menu.Invoke(0);Field(library,"_contents",new List<ContentsEntry>{new ContentsEntry{title="Capítulo",page=23}});
  Call(menu,"OpenReadingList",1);library.CancelContents();Field(library,"_contents",new List<ContentsEntry>{new ContentsEntry{title="Capítulo",page=23}});Call(menu,"RebuildOptions");
  menu.Invoke(menu.Options.ToList().FindIndex(x=>x.Text.StartsWith("Capítulo")));Check(book.Destination==23,"Salto a pagina del indice");
  json="{\"positions\":[6]}";Call(library,"LoadBookmarks");Check(library.NamedBookmarks.Count==1&&library.NamedBookmarks[0].position==6,"Compatibilidad JSON anterior");
  var wifi=JsonUtility.FromJson<WifiStatus>("{\"copied\":4294967297,\"total\":8589934592}");Check(wifi.copied==4294967297L&&wifi.total==8589934592L,"Progreso Wi-Fi de 64 bits");
  Console.WriteLine("OK: "+checks+" comprobaciones C# de biblioteca/menu con stubs");
 }
}
