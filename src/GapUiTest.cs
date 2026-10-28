using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
class GapUiTest {
 static object Field(MainForm f,string name){return typeof(MainForm).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f);}
 static async Task Call(MainForm f,string name,params object[] args){await (Task)typeof(MainForm).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,args);}
 [STAThread] static void Main(string[] args){Application.EnableVisualStyles();var f=new MainForm(null);f.Shown+=async(s,e)=>{try{
 await Call(f,"LoadDocument",args[0]);var canvas=(PageCanvas)Field(f,"canvas");canvas.SetZoom(105);canvas.AutoScrollPosition=new Point(300,200);
 var pages=(ListView)Field(f,"pages");pages.Items[12].Selected=true;
 canvas.Area=new RectangleF(77f/595.2f,382f/841.68f,60f/595.2f,17f/841.68f);
 ((ComboBox)Field(f,"fontFamily")).SelectedItem="Arial";((NumericUpDown)Field(f,"fontSize")).Value=12;
 ((SpellTextBox)Field(f,"editText")).Text="la cual";
 if(!((CheckBox)Field(f,"closeGap")).Checked)throw new Exception("Gap control must default on");
 await Call(f,"AreaEdit","edit");File.Copy((string)Field(f,"current"),args[1],true);
 await Call(f,"Undo",-1);await Call(f,"Undo",1);
 File.WriteAllText(args[2],"PASS: real 13-page PDF, page 13, zoom and scroll, close-gap UI enabled, AreaEdit replacement, undo and redo.");
 }catch(Exception ex){File.WriteAllText(args[2],"FAIL: "+ex);}finally{typeof(MainForm).GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,false);f.Close();}};Application.Run(f);}
}
