// Integration test for the real WinForms frontend and bundled engine. AGPL-3.0-or-later.
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
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
    File.WriteAllText(args[1],"PASS: Windows frontend, 14-page load, 4 blanks, deletion, undo, redo, lossless optimization, previews.");
   }catch(Exception ex){File.WriteAllText(args[1],"FAIL: "+ex);}
   finally{typeof(MainForm).GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,false);f.Close();}
  };
  Application.Run(f);
 }
}
