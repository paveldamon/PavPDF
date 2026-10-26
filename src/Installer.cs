// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Win32;
using System.Net;
using System.Web.Script.Serialization;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;

static class SetupProgram {
 [STAThread] static void Main(string[] args){
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  if(args.Length==2&&args[0]=="--extract"){SetupForm.Extract(args[1]);return;}
  if(args.Length>0&&args[0]=="--uninstall"){
   string temp=Path.Combine(Path.GetTempPath(),"PavPDF-Remove-"+Guid.NewGuid().ToString("N")+".exe");
   File.Copy(Application.ExecutablePath,temp);Process.Start(temp,"--remove");return;
  }
  if(args.Length>0&&args[0]=="--remove"){
   if(MessageBox.Show("¿Desinstalar PavPDF? Tus documentos guardados se conservarán.","PavPDF",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
   try{
    if(Process.GetProcessesByName("PavPDF").Length>0)throw new Exception("Cierra PavPDF e intenta nuevamente.");
    string root=SetupForm.InstallRoot;
    if(!File.Exists(Path.Combine(root,"pavpdf.install")))throw new Exception("No se encontró una instalación de PavPDF identificada.");
    Directory.Delete(root,true);
    foreach(string place in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.Programs)}){string link=Path.Combine(place,"PavPDF.lnk");if(File.Exists(link))File.Delete(link);}
    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PavPDF",false);
    MessageBox.Show("PavPDF fue desinstalado.","PavPDF");
   }catch(Exception e){MessageBox.Show(e.Message,"PavPDF");}return;
  }
  Application.Run(new SetupForm());
 }
}
class SetupForm:Form {
 public static string InstallRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PavPDF");
 Label info=new Label();Button install=new Button();ProgressBar progress=new ProgressBar();CheckBox launch=new CheckBox();bool working,finished;
 public SetupForm(){
  Text="Instalar PavPDF";ClientSize=new Size(550,350);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(245,248,252);
  var title=new Label(){Text="PavPDF",Font=new Font("Segoe UI",29,FontStyle.Bold),ForeColor=Color.FromArgb(15,34,58),Location=new Point(30,20),AutoSize=true};
  info.SetBounds(32,91,485,125);
#if ONLINE
  info.Text="Instalador conectado a GitHub\n\npaveldamon/PavPDF\n\nDescarga y verifica la última versión publicada. Después se abrirá su instalador.";
  install.Text="Descargar instalador";
#else
  info.Text="Editor y organizador de documentos PDF\n\nSe instalará para tu usuario, sin permisos de administrador. Incluye todas sus dependencias y funciona sin conexión.\n\nVersión 1.0.0 · Windows 10/11 de 64 bits";
  install.Text="Instalar PavPDF";
#endif
  launch.Text="Abrir PavPDF al terminar";launch.Checked=true;launch.SetBounds(32,225,300,28);
  progress.SetBounds(32,265,485,10);install.SetBounds(310,294,207,38);install.BackColor=Color.FromArgb(15,92,164);install.ForeColor=Color.White;install.FlatStyle=FlatStyle.Flat;
  install.Click+=async(s,e)=>await Install();Controls.AddRange(new Control[]{title,info,launch,progress,install});
  FormClosing+=(s,e)=>{if(working)e.Cancel=true;};
 }
 public static void Extract(string target){
  Directory.CreateDirectory(target);string full=Path.GetFullPath(target)+Path.DirectorySeparatorChar;
  using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")){
   if(stream==null)throw new Exception("Este instalador no incluye el paquete local.");
   using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))foreach(var entry in zip.Entries){
    string path=Path.GetFullPath(Path.Combine(target,entry.FullName));if(!path.StartsWith(full,StringComparison.OrdinalIgnoreCase))throw new Exception("Ruta de paquete inválida.");
    if(entry.Name.Length==0){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path));entry.ExtractToFile(path,true);
   }
  }
 }
 async Task Install(){
  if(finished){Close();return;}
  working=true;install.Enabled=false;progress.Style=ProgressBarStyle.Marquee;
  try{
#if ONLINE
   await Download();
#else
   if(!Environment.Is64BitOperatingSystem)throw new Exception("PavPDF necesita Windows de 64 bits.");
   if(Process.GetProcessesByName("PavPDF").Length>0)throw new Exception("Cierra PavPDF antes de instalar o actualizar.");
   info.Text="Preparando archivos…";
   await Task.Run(()=>{
    Directory.CreateDirectory(InstallRoot);
    string stage=Path.Combine(InstallRoot,"stage-"+Guid.NewGuid().ToString("N"));Extract(stage);
    string app=Path.Combine(InstallRoot,"app"),previous=Path.Combine(InstallRoot,"previous-"+Guid.NewGuid().ToString("N"));
    if(Directory.Exists(app))Directory.Move(app,previous);
    try{Directory.Move(stage,app);}catch{if(Directory.Exists(previous))Directory.Move(previous,app);throw;}
    if(Directory.Exists(previous))Directory.Delete(previous,true);
    File.WriteAllText(Path.Combine(InstallRoot,"pavpdf.install"),"PavPDF 1.0.0");
    File.Copy(Application.ExecutablePath,Path.Combine(InstallRoot,"Uninstall.exe"),true);
    using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PavPDF")){
     key.SetValue("DisplayName","PavPDF");key.SetValue("DisplayVersion","1.0.0");key.SetValue("Publisher","paveldamon");key.SetValue("InstallLocation",app);key.SetValue("DisplayIcon",Path.Combine(app,"PavPDF.exe"));key.SetValue("UninstallString","\""+Path.Combine(InstallRoot,"Uninstall.exe")+"\" --uninstall");key.SetValue("URLInfoAbout","https://github.com/paveldamon/PavPDF");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
    }
   });
   string exe=Path.Combine(InstallRoot,"app","PavPDF.exe");
   foreach(string place in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.Programs)}){
    dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic link=shell.CreateShortcut(Path.Combine(place,"PavPDF.lnk"));link.TargetPath=exe;link.WorkingDirectory=Path.GetDirectoryName(exe);link.Description="PavPDF — Editor y organizador PDF";link.Save();
   }
   info.Text="Instalación completada.\n\nYa puedes abrir PavPDF desde el escritorio o desde Inicio.";if(launch.Checked)Process.Start(exe);
#endif
   progress.Style=ProgressBarStyle.Blocks;progress.Value=100;install.Text="Terminar";install.Enabled=true;finished=true;
  }catch(Exception e){MessageBox.Show(e.Message,"PavPDF",MessageBoxButtons.OK,MessageBoxIcon.Information);install.Enabled=true;progress.Style=ProgressBarStyle.Blocks;}finally{working=false;}
 }
#if ONLINE
 async Task Download(){
  ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
  using(var web=new WebClient()){
   web.Headers["User-Agent"]="PavPDF-Installer/1.0";
   string raw;try{raw=await web.DownloadStringTaskAsync("https://api.github.com/repos/paveldamon/PavPDF/releases/latest");}catch(WebException){throw new Exception("Todavía no hay una versión pública disponible en paveldamon/PavPDF, o no hay conexión. Publica PavPDF-Setup.exe y PavPDF-Setup.exe.sha256 en una Release. Mientras tanto, usa el instalador completo.");}
   var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(raw);string url=null,hashUrl=null;
   foreach(Dictionary<string,object> asset in (ArrayList)data["assets"]){string name=(string)asset["name"];if(name=="PavPDF-Setup.exe")url=(string)asset["browser_download_url"];if(name=="PavPDF-Setup.exe.sha256")hashUrl=(string)asset["browser_download_url"];}
   if(url==null||hashUrl==null)throw new Exception("La Release debe incluir PavPDF-Setup.exe y PavPDF-Setup.exe.sha256.");
   foreach(string u in new[]{url,hashUrl}){var uri=new Uri(u);if(uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/paveldamon/PavPDF/releases/download/",StringComparison.OrdinalIgnoreCase))throw new Exception("La dirección de descarga no pertenece al repositorio configurado.");}
   string folder=Path.Combine(Path.GetTempPath(),"PavPDF-download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string file=Path.Combine(folder,"PavPDF-Setup.exe");
   info.Text="Descargando desde GitHub…";progress.Style=ProgressBarStyle.Blocks;web.DownloadProgressChanged+=(s,e)=>progress.Value=Math.Max(0,Math.Min(100,e.ProgressPercentage));await web.DownloadFileTaskAsync(new Uri(url),file);
   string expected=(await web.DownloadStringTaskAsync(hashUrl)).Trim().Split(new[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries)[0];string actual;
   using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
   if(!String.Equals(expected,actual,StringComparison.OrdinalIgnoreCase)){File.Delete(file);throw new Exception("La descarga no pasó la verificación SHA-256. Intenta nuevamente.");}
   info.Text="Descarga verificada. Abriendo el instalador…";Process.Start(new ProcessStartInfo(file){UseShellExecute=true});
  }
 }
#endif
}
