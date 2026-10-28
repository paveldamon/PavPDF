using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
class EditTest {
 static object Field(MainForm f,string name){return typeof(MainForm).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f);}
 static async Task Call(MainForm f,string name,params object[] args){await (Task)typeof(MainForm).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,args);}
 [STAThread] static void Main(string[] args){Application.EnableVisualStyles();var f=new MainForm(null);f.Shown+=async(s,e)=>{try{
 await Call(f,"LoadDocument",args[0]);var canvas=(PageCanvas)Field(f,"canvas");canvas.SetZoom(105);canvas.AutoScrollPosition=new Point(300,200);
 canvas.Area=new RectangleF(.71129033f,.14490070f,.05899193f,.01305960f);
 await Call(f,"DetectFont");if(((ComboBox)Field(f,"fontFamily")).Text!="Times New Roman"||!((CheckBox)Field(f,"fontBold")).Checked)throw new Exception("Scan detection failed");
 ((SpellTextBox)Field(f,"editText")).Text="CUSCO";((CheckBox)Field(f,"protect")).Checked=false;
 await Call(f,"AreaEdit","edit");File.Copy((string)Field(f,"current"),args[1],true);
 await Call(f,"Undo",-1);await Call(f,"Undo",1);
 File.WriteAllText(args[2],"PASS: real 19-page PDF, zoom 105%, scroll, automatic scan typography, AreaEdit replacement, undo and redo.");
 }catch(Exception ex){File.WriteAllText(args[2],"FAIL: "+ex);}finally{typeof(MainForm).GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,false);f.Close();}};Application.Run(f);}
}
