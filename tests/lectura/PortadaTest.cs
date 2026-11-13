using System;using System.Collections.Generic;using System.Linq;using FolioXR.Reading;using UnityEngine;
class PortadaTest {
 static int checks;static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static void Slots(FolioBook b,int left,int right){Check(b.LeftSlotPage==left&&b.RightSlotPage==right,"Ranuras");Check(b.leftPage.enabled&&b.rightPage.enabled,"Ambas hojas visibles");Check(b.leftPage.material.mainTexture.Page==left&&b.rightPage.material.mainTexture.Page==right,"Texturas en sus ranuras");Check(b.Progress==Math.Min(left<0?int.MaxValue:left,right<0?int.MaxValue:right),"Progreso real");}
 static void Main(){
  // Propiedad: cada página aparece una vez al avanzar, y todo salto contiene la página pedida.
  foreach(bool cover in new[]{false,true})foreach(bool rtl in new[]{false,true})foreach(int count in new[]{0,1,2,3,4,5,10,101}){
   var seen=new List<int>();var spread=new BookSpread(0,count,cover,rtl);
   for(int i=0;i<count+1&&spread.First>=0;i++){
    if(spread.Left>=0)seen.Add(spread.Left);if(spread.Right>=0)seen.Add(spread.Right);
    int next=spread.Next(count);if(next==spread.First)break;
    var following=new BookSpread(next,count,cover,rtl);Check(following.Previous==spread.First,"Avanzar/retroceder simétrico");spread=following;
   }
   Check(seen.OrderBy(x=>x).SequenceEqual(Enumerable.Range(0,count)),"Sin páginas omitidas ni repetidas");
   for(int p=0;p<count;p++){var s=new BookSpread(p,count,cover,rtl);Check(s.Left==p||s.Right==p,"El salto incluye el destino");}
  }
  Check(new BookSpread(int.MaxValue,int.MaxValue,true,false).First==int.MaxValue-2,"Índice límite sin desbordar");
  var b=new FolioBook("portada",5);Check(!FolioBook.GetSingleCover("portada"),"Desactivado por defecto");b.GoToPage(0);Slots(b,0,1);
  b.SetSingleCoverFor("portada",true);Slots(b,-1,0);Check(b.Requests.Last()==0,"No solicita página 1 para portada");
  b.NextSpread();Slots(b,1,2);b.NextSpread();Slots(b,3,4);int requests=b.Requests.Count;b.NextSpread();Check(b.Requests.Count==requests,"Fin del libro");
  b.PreviousSpread();Slots(b,1,2);b.PreviousSpread();Slots(b,-1,0);Check(PageTurnEffect.Turns>=4,"Animación desde portada y regreso");
  b.GoToPage(2);Slots(b,1,2);b.GoToPage(3);Slots(b,3,4);b.SetRightToLeft(true);Slots(b,4,3);b.GoToPage(0);Slots(b,0,-1);b.NextSpread();Slots(b,2,1);
  b.SetSingleCoverFor("otro",true);Slots(b,2,1);Check(FolioBook.GetSingleCover("otro"),"Preferencia por libro cerrado");
  var reopened=new FolioBook("portada",5);reopened.GoToPage(0);Slots(reopened,-1,0);
  var even=new FolioBook("par",4);even.SetSingleCoverFor("par",true);even.GoToPage(3);Slots(even,3,-1);even.SetRightToLeft(true);Slots(even,-1,3);
  var one=new FolioBook("único",1);one.SetSingleCoverFor("único",true);one.GoToPage(0);Slots(one,-1,0);one.NextSpread();one.PreviousSpread();Slots(one,-1,0);
  b.SetRightToLeft(false);b.GoToPage(2);b.Inject(()=>b.SetSingleCoverFor("portada",false));b.GoToPage(3);Slots(b,3,4);b.RefreshPending();Slots(b,2,3);Check(!b.Busy,"Cambio diferido termina");
  PageTheme.Night=true;b.SetSingleCoverFor("portada",true);b.GoToPage(0);Check(b.leftPage.material.color.r<.2f,"Papel vacío sin brillo nocturno");
  b.FailPage(1);b.NextSpread();Slots(b,-1,0);Check(!b.Busy,"Fallo no cambia el progreso");
  Console.WriteLine("OK: "+checks+" comprobaciones de portada y miembros reales de FolioBook (sin Unity)");
 }
}
