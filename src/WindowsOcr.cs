// Local Windows OCR bridge. SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;

class WindowsOcr {
 [MTAThread] static void Main(string[] args){
  try{Run(args);}
  catch(Exception ex){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new {ok=false,error=ex.Message}));}
 }
 static T Wait<T>(Windows.Foundation.IAsyncOperation<T> operation){
  var start=DateTime.UtcNow;
  while(operation.Status==Windows.Foundation.AsyncStatus.Started){if((DateTime.UtcNow-start).TotalSeconds>25){operation.Cancel();throw new Exception("El reconocimiento tardó demasiado. Selecciona un área más pequeña.");}System.Threading.Thread.Sleep(15);}
  return operation.GetResults();
 }
 static void Run(string[] args){
  var engine=OcrEngine.TryCreateFromLanguage(new Language("es-ES"))??OcrEngine.TryCreateFromUserProfileLanguages();
  if(engine==null&&OcrEngine.AvailableRecognizerLanguages.Count>0)engine=OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
  if(engine==null)throw new Exception("Windows no tiene un idioma de reconocimiento OCR disponible. Instala un idioma OCR en Windows para estimar fuentes en escaneos.");
  var file=Wait(StorageFile.GetFileFromPathAsync(Path.GetFullPath(args[0])));
  using(var stream=Wait(file.OpenAsync(FileAccessMode.Read))){
   var decoder=Wait(BitmapDecoder.CreateAsync(stream));
   using(var bitmap=Wait(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied))){
    var result=Wait(engine.RecognizeAsync(bitmap));
    var words=new List<object>();
    int lineId=0;
    foreach(var line in result.Lines){foreach(var word in line.Words){var r=word.BoundingRect;words.Add(new {text=word.Text,x=r.X,y=r.Y,width=r.Width,height=r.Height,line=lineId});}lineId++;}
    File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new {ok=true,text=result.Text,words=words,angle=result.TextAngle,language=engine.RecognizerLanguage.LanguageTag}),new System.Text.UTF8Encoding(false));
   }
  }
 }
}
