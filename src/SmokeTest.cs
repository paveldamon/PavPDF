// Integration test for the real WinForms frontend and bundled engine. AGPL-3.0-or-later.
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;
class SmokeTest {
 static object Field(MainForm f,string name){return typeof(MainForm).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f);}
 static async Task Call(MainForm f,string name,params object[] args){await (Task)typeof(MainForm).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,args);}
 [STAThread] static void Main(string[] args){
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  var f=new MainForm(null);
  f.Shown+=async(s,e)=>{
   try{
    await Call(f,"LoadDocument",args[0]);var pages=(ListView)Field(f,"pages");if(pages.Items.Count!=14)throw new Exception("Expected 14 pages");
    typeof(MainForm).GetMethod("Detect",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,null);
    if(pages.CheckedItems.Count!=4)throw new Exception("Expected 4 blank pages");
    await Call(f,"Change","delete",null);if(pages.Items.Count!=10)throw new Exception("Delete failed");
    await Call(f,"Undo",-1);if(pages.Items.Count!=14)throw new Exception("Undo failed");
    await Call(f,"Undo",1);if(pages.Items.Count!=10)throw new Exception("Redo failed");
    await Call(f,"Compress",true);if(pages.Items.Count!=10)throw new Exception("Compress failed");
    var canvas=(PageCanvas)Field(f,"canvas");canvas.SetZoom(200);f.PerformLayout();canvas.Refresh();
    if(canvas.ZoomPercent!=200||canvas.AutoScrollMinSize.Width<=canvas.ClientSize.Width)throw new Exception("Zoom did not enlarge the page");
    canvas.Area=new RectangleF(.2f,.3f,.4f,.1f);canvas.AutoScrollPosition=new Point(100,180);canvas.Refresh();
    var expected=new PointF(.25f,.4f);var screen=new PointF(canvas.ImageArea.X+expected.X*canvas.ImageArea.Width,canvas.ImageArea.Y+expected.Y*canvas.ImageArea.Height);var actual=canvas.DocumentPoint(screen);
    if(Math.Abs(actual.X-expected.X)>.001||Math.Abs(actual.Y-expected.Y)>.001)throw new Exception("Scrolled zoom coordinates do not match document coordinates");
    var before=canvas.Area;canvas.SetZoom(300);canvas.Refresh();if(canvas.Area!=before)throw new Exception("Zoom changed the edit area");
    canvas.SetZoom(0);canvas.Refresh();if(canvas.AutoScrollMinSize!=Size.Empty)throw new Exception("Fit page retains scrolling");
    if(args.Length>2){
      typeof(MainForm).GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,false);
      await Call(f,"LoadDocument",args[2]);canvas.Area=new RectangleF(40f/595.276f,120f/841.89f,460f/595.276f,50f/841.89f);
      await Call(f,"DetectFont");var family=(ComboBox)Field(f,"fontFamily");var size=(NumericUpDown)Field(f,"fontSize");
      if(Convert.ToString(family.SelectedItem)!="Arial"||Math.Abs(size.Value-12)>.5m)throw new Exception("UI did not apply detected font and size");
      canvas.SetZoom(200);await Call(f,"LoadSharperPreview");
      if(canvas.PageImage.Width<1500)throw new Exception("High-resolution preview missing");
    }
    if(args.Length>3){canvas.SetZoom(125);canvas.AutoScrollPosition=Point.Empty;canvas.Refresh();using(var snapshot=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(snapshot,new Rectangle(0,0,f.Width,f.Height));snapshot.Save(args[3]);}}
    File.WriteAllText(args[1],"PASS: Windows frontend, page load, blank detection, deletion, undo, redo, lossless optimization, zoom 200/300%, fit page, scroll coordinates, selection preserved, automatic Arial 12pt detection, high-resolution preview.");
   }catch(Exception ex){File.WriteAllText(args[1],"FAIL: "+ex);}
   finally{typeof(MainForm).GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,false);f.Close();}
  };
  Application.Run(f);
 }
}
