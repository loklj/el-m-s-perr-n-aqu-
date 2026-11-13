using System;
using System.IO;
using System.Reflection;
using FolioXR.Reading;
using UnityEngine;
class Test {
 static int n; static void Check(bool x){if(!x)throw new Exception("Prueba "+n);n++;}
 static void Main(){
  string[] ext={"pdf","epub","txt","mobi","azw","azw3","cbz","zip","cbr","docx","odt","rtf"};
  string[] type={"Pdf","Epub","Text","Text","Text","Text","Comic","Comic","Cbr","Text","Text","Text"};
  for(int i=0;i<ext.Length;i++){
   AndroidJavaClass.Created.Clear();using(var renderer=new PdfPageRenderer("libro."+ext[i])){}
   Check(AndroidJavaClass.Created[0]=="com.loklj.folioxr.Folio"+type[i]+"Renderer");
  }
  using(var renderer=new PdfPageRenderer("libro.epub")){
   SearchList found=null;string error=null;
   AndroidJavaClass.Handler=(method,args)=>method=="getStatus"?"OK:{\"hits\":[{\"page\":7,\"text\":\"coincidencia\"}],\"next\":\"2:5\"}":null;
   var search=renderer.Search("niño","",20,(list,e)=>{found=list;error=e;});
   Check(search.MoveNext() && AndroidJavaClass.Calls[AndroidJavaClass.Calls.Count-1].method=="requestSearch");
   var waiting=(System.Collections.IEnumerator)search.Current;while(waiting.MoveNext()){}
   while(search.MoveNext()){}
   Check(error==null && found!=null && found.hits[0].page==7 && found.next=="2:5");
   bool callback=false;
   search=renderer.Search("otra","",20,(list,e)=>callback=true);search.MoveNext();waiting=(System.Collections.IEnumerator)search.Current;
   renderer.CancelSearch();while(waiting.MoveNext()){}while(search.MoveNext()){}
   Check(!callback);
   AndroidJavaClass.Handler=null;
  }
  Check(PdfPageRenderer.IsReflow("LIBRO.AZW3"));Check(!PdfPageRenderer.IsReflow("a.pdf"));
  var method=typeof(PdfPageRenderer).GetMethod("LoadRaw",BindingFlags.NonPublic|BindingFlags.Static);
  string file=Path.GetTempFileName();try{
   using(var s=new BinaryWriter(File.Create(file))){s.Write(new byte[]{70,88,82,49});s.Write(2);s.Write(1);s.Write(0);s.Write(new byte[]{1,2,3,4,5,6,7,8});}
   var texture=(Texture2D)method.Invoke(null,new object[]{file,false});Check(texture.width==2 && texture.height==1);Check(texture.Pixels[0]==1 && texture.Pixels[7]==8);
   using(var s=new BinaryWriter(File.Create(file))){s.Write(new byte[]{70,88,82,49});s.Write(2);s.Write(1);s.Write(0);s.Write(new byte[]{255,255,255,255,0,0,0,128});}
   var night=(Texture2D)method.Invoke(null,new object[]{file,true});
   Check(night.Pixels[0]==30 && night.Pixels[1]==29 && night.Pixels[2]==28 && night.Pixels[3]==255);
   Check(night.Pixels[4]==222 && night.Pixels[5]==214 && night.Pixels[6]==198 && night.Pixels[7]==128);
   PageTheme.Set(PageThemeMode.Night);
   Check(PageTheme.NeedsRemap("a.docx") && PageTheme.NeedsRemap("a.odt") && PageTheme.NeedsRemap("a.rtf"));
   Check(!PageTheme.NeedsRemap("a.cbz") && !PageTheme.NeedsRemap("a.cbr"));
   PageTheme.Set(PageThemeMode.Day);Check(!PageTheme.NeedsRemap("a.docx"));
   File.WriteAllBytes(file,new byte[]{137,80,78,71,13,10,26,10});Check(method.Invoke(null,new object[]{file,false})==null);
   using(var s=new BinaryWriter(File.Create(file))){s.Write(new byte[]{70,88,82,49});s.Write(2);s.Write(1);s.Write(0);}
   try{method.Invoke(null,new object[]{file,false});throw new Exception("Acepto FXR1 truncado");}catch(TargetInvocationException e){Check(e.InnerException is IOException);}
   using(var s=new FileStream(file,FileMode.Create)){s.SetLength(8L<<30);}
   try{method.Invoke(null,new object[]{file,false});throw new Exception("Acepto imagen de 8 GiB");}catch(TargetInvocationException e){Check(e.InnerException is IOException);}
   using(var s=new BinaryWriter(File.Create(file))){s.Write(new byte[]{70,88,82,49});s.Write(8192);s.Write(8192);s.Write(0);}
   try{method.Invoke(null,new object[]{file,false});throw new Exception("Acepto exceso de pixeles");}catch(TargetInvocationException e){Check(e.InnerException is IOException);}
  }finally{File.Delete(file);Directory.Delete(Application.temporaryCachePath,true);}
  Console.WriteLine("OK: "+n+" comprobaciones del puente C# con stubs (no Unity)");
 }
}
