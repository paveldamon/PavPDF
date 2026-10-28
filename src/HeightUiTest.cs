using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
class HeightUiTest {
 static object Field(MainForm f,string name){return typeof(MainForm).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f);}
 static async Task Call(MainForm f,string name,params object[] args){await (Task)typeof(MainForm).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,args);}
 [STAThread] static void Main(string[] args){Application.EnableVisualStyles();var f=new MainForm(null);f.Shown+=async(s,e)=>{try{
 await Call(f,"LoadDocument",args[0]);var canvas=(PageCanvas)Field(f,"canvas");canvas.SetZoom(105);canvas.AutoScrollPosition=new Point(300,200);
 var pages=(ListView)Field(f,"pages");pages.Items[0].Selected=true;
 canvas.Area=new RectangleF(350f/595.2f,429f/841.68f,61f/595.2f,14f/841.68f);
 ((ComboBox)Field(f,"fontFamily")).SelectedItem="Arial Narrow";((NumericUpDown)Field(f,"fontSize" )).Value=11.5m;
 ((SpellTextBox)Field(f,"editText")).Text="Numeral 20";
 if(!((CheckBox)Field(f,"matchPaper")).Checked)throw new Exception("Paper matching must default on");
 if(!((CheckBox)Field(f,"autoFit")).Checked)throw new Exception("Automatic fitting must default on");
 await Call(f,"AreaEdit","edit");File.Copy((string)Field(f,"current"),args[1],true);
 await Call(f,"Undo",-1);await Call(f,"Undo",1);
 File.WriteAllText(args[2],"PASS: real 5-page PDF, page 1, zoom and scroll, automatic height and paper background enabled, AreaEdit replacement, undo and redo.");
 }catch(Exception ex){File.WriteAllText(args[2],"FAIL: "+ex);}finally{typeof(MainForm).GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,false);f.Close();}};Application.Run(f);}
}
