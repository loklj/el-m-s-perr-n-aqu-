using System;
using System.IO;
using System.Reflection;
using FolioXR.Reading;
using UnityEngine;
class Test {
 static int n; static void Check(bool x){if(!x)throw new Exception("Prueba "+n);n++;}
 static void Main(){
  string[] ext={"pdf","epub","txt","mobi","azw","azw3","cbz","zip","cbr"};
  string[] type={"Pdf","Epub","Text","Text","Text","Text","Comic","Comic","Cbr"};
  for(int i=0;i<ext.Length;i++){
   AndroidJavaClass.Created.Clear();using(var renderer=new PdfPageRenderer("libro."+ext[i])){}
   Check(AndroidJavaClass.Created[0]=="com.loklj.folioxr.Folio"+type[i]+"Renderer");
  }
  Check(PdfPageRenderer.IsReflow("LIBRO.AZW3"));Check(!PdfPageRenderer.IsReflow("a.pdf"));
  var method=typeof(PdfPageRenderer).GetMethod("LoadRaw",BindingFlags.NonPublic|BindingFlags.Static);
  string file=Path.GetTempFileName();try{
   using(var s=new BinaryWriter(File.Create(file))){s.Write(new byte[]{70,88,82,49});s.Write(2);s.Write(1);s.Write(0);s.Write(new byte[]{1,2,3,4,5,6,7,8});}
   var texture=(Texture2D)method.Invoke(null,new object[]{file});Check(texture.width==2 && texture.height==1);Check(texture.Pixels[0]==1 && texture.Pixels[7]==8);
   File.WriteAllBytes(file,new byte[]{137,80,78,71,13,10,26,10});Check(method.Invoke(null,new object[]{file})==null);
   using(var s=new BinaryWriter(File.Create(file))){s.Write(new byte[]{70,88,82,49});s.Write(2);s.Write(1);s.Write(0);}
   try{method.Invoke(null,new object[]{file});throw new Exception("Acepto FXR1 truncado");}catch(TargetInvocationException e){Check(e.InnerException is IOException);}
  }finally{File.Delete(file);Directory.Delete(Application.temporaryCachePath,true);}
  Console.WriteLine("OK: "+n+" comprobaciones del puente C# con stubs (no Unity)");
 }
}
