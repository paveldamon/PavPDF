using System;using System.IO;using System.Windows.Forms;using System.Threading.Tasks;
class SpellTest {
 [STAThread]static void Main(string[] args){var f=new Form();var box=new SpellTextBox(){Dock=DockStyle.Fill};f.Controls.Add(box);f.Shown+=async(s,e)=>{try{
 box.Text="disposicion 15";box.Editor.Focus();box.Editor.CaretIndex=box.Text.Length;await Task.Delay(1200);
 int i=box.Editor.GetNextSpellingErrorCharacterIndex(0,System.Windows.Documents.LogicalDirection.Forward);
 if(i!=0)throw new Exception("Spanish spelling did not detect disposicion: "+i+" text="+box.Text+" language="+box.Editor.Language);
 var error=box.Editor.GetSpellingError(i);var suggestions=String.Join(",",error.Suggestions);if(!suggestions.Contains("disposición"))throw new Exception("Expected accent suggestion: "+suggestions);
 box.Editor.Select(i,box.Editor.GetSpellingErrorLength(i));box.Editor.SelectedText="disposición";
 if(box.Text!="disposición 15")throw new Exception("Spelling changed the number");
 File.WriteAllText(args[0],"PASS: Spanish spelling detects disposicion, suggests disposición, correction preserves 15.");
 }catch(Exception ex){File.WriteAllText(args[0],"FAIL: "+ex);}finally{f.Close();}};Application.Run(f);}
}
