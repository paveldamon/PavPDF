// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Net;

static class Program {
 [STAThread] static void Main(string[] args) {
  Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
  Application.Run(new MainForm(args.Length>0?args[0]:null));
 }
}
class PageCanvas:Control {
 public Image PageImage; public RectangleF Area {get;set;} public RectangleF ImageArea;
 Point start; bool dragging;
 public PageCanvas(){DoubleBuffered=true; BackColor=Color.FromArgb(226,232,240); Cursor=Cursors.Cross;}
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  if(PageImage==null){using(var f=new Font("Segoe UI",19))e.Graphics.DrawString("Tu documento, listo para trabajar",f,Brushes.SlateGray,35,70);return;}
  float scale=Math.Min((Width-34f)/PageImage.Width,(Height-34f)/PageImage.Height);
  if(scale<=0)return;
  ImageArea=new RectangleF((Width-PageImage.Width*scale)/2,(Height-PageImage.Height*scale)/2,PageImage.Width*scale,PageImage.Height*scale);
  e.Graphics.FillRectangle(Brushes.White,ImageArea); e.Graphics.DrawImage(PageImage,ImageArea);
  if(!Area.IsEmpty){var r=new RectangleF(ImageArea.X+Area.X*ImageArea.Width,ImageArea.Y+Area.Y*ImageArea.Height,Area.Width*ImageArea.Width,Area.Height*ImageArea.Height);
   using(var b=new SolidBrush(Color.FromArgb(45,0,120,210)))e.Graphics.FillRectangle(b,r);
   using(var p=new Pen(Color.FromArgb(0,110,210),2))e.Graphics.DrawRectangle(p,r.X,r.Y,r.Width,r.Height);
  }
 }
 protected override void OnMouseDown(MouseEventArgs e){if(PageImage==null||!ImageArea.Contains(e.Location))return;start=e.Location;dragging=true;Capture=true;Area=RectangleF.Empty;}
 protected override void OnMouseMove(MouseEventArgs e){if(!dragging)return;
  float x=Math.Max(ImageArea.Left,Math.Min(e.X,ImageArea.Right)),y=Math.Max(ImageArea.Top,Math.Min(e.Y,ImageArea.Bottom));
  Area=new RectangleF((Math.Min(start.X,x)-ImageArea.X)/ImageArea.Width,(Math.Min(start.Y,y)-ImageArea.Y)/ImageArea.Height,Math.Abs(x-start.X)/ImageArea.Width,Math.Abs(y-start.Y)/ImageArea.Height);Invalidate();}
 protected override void OnMouseUp(MouseEventArgs e){dragging=false;Capture=false;Invalidate();}
 protected override void OnResize(EventArgs e){base.OnResize(e);Invalidate();}
 public void LoadImage(string path){if(PageImage!=null)PageImage.Dispose();using(var im=Image.FromFile(path))PageImage=new Bitmap(im);Area=RectangleF.Empty;Invalidate();}
 protected override void Dispose(bool b){if(b&&PageImage!=null)PageImage.Dispose();base.Dispose(b);}
}
class MainForm:Form {
 const string Repo="paveldamon/PavPDF";
 JavaScriptSerializer json=new JavaScriptSerializer(){MaxJsonLength=50000000};
 string root=Path.Combine(Path.GetTempPath(),"PavPDF-"+Guid.NewGuid().ToString("N")),current,original;
 List<string> history=new List<string>(); int historyPos=-1,revision=0;
 ListView pages=new ListView(); ImageList thumbs=new ImageList(); PageCanvas canvas=new PageCanvas();
 Label status=new Label(), docTitle=new Label(),count=new Label();
 FlowLayoutPanel tools=new FlowLayoutPanel(),top=new FlowLayoutPanel();
 TextBox editText=new TextBox(); NumericUpDown fontSize=new NumericUpDown(), header=new NumericUpDown();
 CheckBox protect=new CheckBox(); Panel editor=new Panel();
 bool busy=false,dirty=false; HashSet<string> sourceFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase); List<Dictionary<string,object>> infos=new List<Dictionary<string,object>>();
 public MainForm(string initial){
  Text="PavPDF · Documentos claros, trabajo simple"; Width=1370;Height=900;MinimumSize=new Size(1120,720);StartPosition=FormStartPosition.CenterScreen;
  Font=new Font("Segoe UI",10);BackColor=Color.White;Directory.CreateDirectory(root);
  try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
  var headerBar=new Panel(){Dock=DockStyle.Top,Height=82,BackColor=Color.FromArgb(15,34,58)};
  var brand=new Label(){Text="PavPDF",Font=new Font("Segoe UI",25,FontStyle.Bold),ForeColor=Color.White,Location=new Point(22,12),AutoSize=true};
  var subtitle=new Label(){Text="TU OFICINA PDF  /  100% LOCAL",ForeColor=Color.FromArgb(145,184,208),Location=new Point(190,34),AutoSize=true};
  var github=new Button(){Text="GitHub y actualizaciones",Size=new Size(220,36),Anchor=AnchorStyles.Top|AnchorStyles.Right,Location=new Point(Width-260,23),FlatStyle=FlatStyle.Flat,ForeColor=Color.White};github.Click+=async(s,e)=>await Updates();
  headerBar.Controls.AddRange(new Control[]{brand,subtitle,github});
  headerBar.SizeChanged+=(s,e)=>github.Left=headerBar.ClientSize.Width-github.Width-20;
  top.Dock=DockStyle.Top;top.Height=53;top.Padding=new Padding(14,8,0,0);top.BackColor=Color.FromArgb(245,248,252);
  AddTop("Abrir PDF",async()=>await Open());AddTop("Unir / agregar",async()=>await Merge());AddTop("Guardar copia",async()=>await SaveCopy());AddTop("Deshacer",async()=>await Undo(-1));AddTop("Rehacer",async()=>await Undo(1));
  var bottom=new Panel(){Dock=DockStyle.Bottom,Height=35,BackColor=Color.FromArgb(15,34,58)};
  status.Dock=DockStyle.Fill;status.Padding=new Padding(14,7,0,0);status.ForeColor=Color.White;status.Text="Listo. Abre un PDF o agrega varios documentos.";bottom.Controls.Add(status);
  tools.Dock=DockStyle.Left;tools.Width=222;tools.FlowDirection=FlowDirection.TopDown;tools.WrapContents=false;tools.AutoScroll=true;tools.BackColor=Color.FromArgb(245,248,252);tools.Padding=new Padding(12);
  Section("ORGANIZAR");Tool("Detectar páginas en blanco",()=>Detect());Tool("Eliminar marcadas",async()=>await Delete());
  Tool("Girar 90°",async()=>await Change("rotate",new Dictionary<string,object>{{"angle",90}}));
  Tool("Subir página",async()=>await MovePage(-1));Tool("Bajar página",async()=>await MovePage(1));
  Tool("Duplicar marcadas",async()=>await Change("duplicate",null));Tool("Extraer marcadas",async()=>await Extract());
  Section("MEJORAR Y EDITAR");Tool("Mejorar escaneo",async()=>await Enhance());Tool("Recortar al área dibujada",async()=>await AreaEdit("crop"));Tool("Insertar imagen / sello",async()=>await AreaEdit("image"));
  Tool("Numerar páginas",async()=>await Change("number",null));Tool("Exportar imágenes PNG",async()=>await ExportImages());
  Section("TAMAÑO Y ENTREGA");Tool("Optimizar sin pérdida",async()=>await Compress(true));Tool("Comprimir escaneos…",async()=>await Compress(false));Tool("Dividir en partes ≤ 10 MB",async()=>await Split());
  Section("AYUDA");Tool("Guía de uso",()=>{Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"LEEME.html")){UseShellExecute=true});});
  var pagePanel=new Panel(){Dock=DockStyle.Left,Width=195,Padding=new Padding(8)};
  count.Dock=DockStyle.Top;count.Height=50;count.Text="PÁGINAS\nMarca para aplicar en grupo";count.Font=new Font("Segoe UI",9);pagePanel.Controls.Add(count);
  pages.Dock=DockStyle.Fill;pages.View=View.LargeIcon;pages.CheckBoxes=true;pages.MultiSelect=false;pages.HideSelection=false;pages.BorderStyle=BorderStyle.None;
  thumbs.ImageSize=new Size(108,140);thumbs.ColorDepth=ColorDepth.Depth32Bit;pages.LargeImageList=thumbs;
  pages.SelectedIndexChanged+=(s,e)=>{if(pages.SelectedIndices.Count>0&&infos.Count>pages.SelectedIndices[0])canvas.LoadImage((string)infos[pages.SelectedIndices[0]]["preview"]);};
  var mark=new FlowLayoutPanel(){Dock=DockStyle.Bottom,Height=35};var all=new Button(){Text="Todas",Width=76,Height=28};var none=new Button(){Text="Ninguna",Width=80,Height=28};
  all.Click+=(s,e)=>{foreach(ListViewItem it in pages.Items)it.Checked=true;};none.Click+=(s,e)=>{foreach(ListViewItem it in pages.Items)it.Checked=false;};mark.Controls.AddRange(new Control[]{all,none});
  pagePanel.Controls.Add(pages);pages.BringToFront();pagePanel.Controls.Add(mark);
  var center=new Panel(){Dock=DockStyle.Fill};docTitle.Dock=DockStyle.Top;docTitle.Height=40;docTitle.Padding=new Padding(14,10,0,0);docTitle.Text="Arrastra un PDF a esta ventana para comenzar";docTitle.Font=new Font("Segoe UI",10,FontStyle.Bold);
  editor.Dock=DockStyle.Bottom;editor.Height=176;editor.Padding=new Padding(10);editor.BackColor=Color.FromArgb(245,248,252);
  var instructions=new Label(){Text="EDITAR ESCANEO  ·  Dibuja un rectángulo en la página y elige una acción.",Location=new Point(12,8),AutoSize=true};
  editText.Location=new Point(12,32);editText.Size=new Size(480,65);editText.Multiline=true;editText.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;editText.ScrollBars=ScrollBars.Vertical;
  fontSize.Location=new Point(510,35);fontSize.Minimum=5;fontSize.Maximum=72;fontSize.Value=11;fontSize.Width=58;fontSize.Anchor=AnchorStyles.Top|AnchorStyles.Right;
  var fontLabel=new Label(){Text="Letra",Location=new Point(510,10),AutoSize=true,Anchor=AnchorStyles.Top|AnchorStyles.Right};
  protect.Text="Proteger membrete";protect.Checked=true;protect.Location=new Point(12,137);protect.Width=170;
  header.Minimum=0;header.Maximum=45;header.Value=16;header.Width=50;header.Location=new Point(185,136);
  var headerLabel=new Label(){Text="% superior",Location=new Point(240,139),AutoSize=true};
  var editButtons=new FlowLayoutPanel(){Location=new Point(8,102),Width=600,Height=33};
  foreach(var pair in new[]{new[]{"Reemplazar texto","edit"},new[]{"Añadir texto","text"},new[]{"Borrar área","redact"}}){string act=pair[1];var b=new Button(){Text=pair[0],Width=150,Height=29};b.Click+=async(s,e)=>await Safe(async()=>await AreaEdit(act));editButtons.Controls.Add(b);}
  editor.Controls.AddRange(new Control[]{instructions,editText,fontSize,fontLabel,protect,header,headerLabel,editButtons});
  editor.SizeChanged+=(s,e)=>{fontSize.Left=editor.ClientSize.Width-82;fontLabel.Left=fontSize.Left;editText.Width=Math.Max(100,editor.ClientSize.Width-110);};
  canvas.Dock=DockStyle.Fill;center.Controls.Add(canvas);center.Controls.Add(docTitle);center.Controls.Add(editor);
  Controls.Add(center);Controls.Add(pagePanel);Controls.Add(tools);Controls.Add(top);Controls.Add(headerBar);Controls.Add(bottom);
  AllowDrop=true;DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
  DragDrop+=async(s,e)=>{if(busy)return;var files=(string[])e.Data.GetData(DataFormats.FileDrop);await Safe(async()=>{if(files.Length==1&&files[0].EndsWith(".pdf",StringComparison.OrdinalIgnoreCase))await LoadDocument(files[0]);else await MergeFiles(files);});};
  FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show("Espera a que termine la operación.","PavPDF");}else if(dirty&&MessageBox.Show("Hay cambios sin guardar. ¿Cerrar sin guardarlos?","PavPDF",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)e.Cancel=true;};
  FormClosed+=(s,e)=>{try{Directory.Delete(root,true);}catch{}};
  Shown+=async(s,e)=>{if(initial!=null&&File.Exists(initial))await Safe(async()=>await LoadDocument(initial));};
 }
 void Section(string text){tools.Controls.Add(new Label(){Text=text,ForeColor=Color.FromArgb(71,95,123),Font=new Font("Segoe UI",8,FontStyle.Bold),Width=190,Height=25,Padding=new Padding(0,9,0,0)});}
 void Tool(string text,Action action){var b=new Button(){Text=text,Width=190,Height=32,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,BackColor=Color.White,Margin=new Padding(0,2,0,2)};b.FlatAppearance.BorderColor=Color.FromArgb(215,225,237);b.Click+=(s,e)=>{if(!busy)try{action();}catch(Exception ex){Error(ex);}};tools.Controls.Add(b);}
 void Tool(string text,Func<Task> action){var b=new Button(){Text=text,Width=190,Height=32,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,BackColor=Color.White,Margin=new Padding(0,2,0,2)};b.FlatAppearance.BorderColor=Color.FromArgb(215,225,237);b.Click+=async(s,e)=>await Safe(action);tools.Controls.Add(b);}
 void AddTop(string text,Func<Task> action){var b=new Button(){Text=text,Width=126,Height=33,FlatStyle=FlatStyle.Flat,BackColor=Color.White};b.FlatAppearance.BorderColor=Color.FromArgb(206,218,231);b.Click+=async(s,e)=>await Safe(action);top.Controls.Add(b);}
 void Error(Exception ex){status.Text="No se aplicó la operación.";MessageBox.Show(ex.Message,"PavPDF",MessageBoxButtons.OK,MessageBoxIcon.Information);}
 async Task Safe(Func<Task> action){if(busy)return;try{await action();}catch(Exception ex){Error(ex);}}
 void Require(){if(current==null)throw new Exception("Abre un PDF primero.");}
 int Selected(){Require();return pages.SelectedIndices.Count>0?pages.SelectedIndices[0]:0;}
 int[] SelectedPages(){Require();var ids=pages.CheckedIndices.Cast<int>().ToArray();return ids.Length>0?ids:new[]{Selected()};}
 Dictionary<string,object> Request(string cmd){return new Dictionary<string,object>{{"cmd",cmd},{"input",current}};}
 string Next(){return Path.Combine(root,"document_"+(++revision)+".pdf");}
 async Task<Dictionary<string,object>> Engine(Dictionary<string,object> request,string message){
  busy=true;tools.Enabled=false;top.Enabled=false;editor.Enabled=false;pages.Enabled=false;status.Text=message;UseWaitCursor=true;
  try{return await Task.Run(()=>{
   string id=Guid.NewGuid().ToString("N"),req=Path.Combine(root,id+".json"),res=Path.Combine(root,id+".result.json");File.WriteAllText(req,json.Serialize(request),new System.Text.UTF8Encoding(false));
   var psi=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","python.exe"),"\""+Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine.py")+"\" \""+req+"\" \""+res+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
   using(var proc=Process.Start(psi)){string error=proc.StandardError.ReadToEnd();proc.WaitForExit();if(!File.Exists(res))throw new Exception("El motor PDF no pudo terminar. "+error);}
   var result=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(res));File.Delete(req);File.Delete(res);
   if(!(bool)result["ok"])throw new Exception(Convert.ToString(result["error"]));return (Dictionary<string,object>)result["data"];
  });}finally{busy=false;tools.Enabled=true;top.Enabled=true;editor.Enabled=true;pages.Enabled=true;UseWaitCursor=false;}
 }
 async Task Refresh(int selected){
  var req=Request("inspect");req["cache"]=Path.Combine(root,"preview_"+Guid.NewGuid().ToString("N"));
  var data=await Engine(req,"Preparando vista previa y revisando páginas…");
  infos=((ArrayList)data["pages"]).Cast<Dictionary<string,object>>().ToList();
  pages.BeginUpdate();pages.Items.Clear();thumbs.Images.Clear();
  foreach(var p in infos){using(var im=Image.FromFile((string)p["preview"])){var thumb=new Bitmap(108,140);using(var g=Graphics.FromImage(thumb)){g.Clear(Color.White);float scale=Math.Min(108f/im.Width,140f/im.Height);g.DrawImage(im,(108-im.Width*scale)/2,(140-im.Height*scale)/2,im.Width*scale,im.Height*scale);}thumbs.Images.Add(thumb);thumb.Dispose();}
   int i=Convert.ToInt32(p["index"]);var it=new ListViewItem("Página "+(i+1)+((bool)p["blank"]?" · revisar":""),i);pages.Items.Add(it);}
  pages.EndUpdate();if(pages.Items.Count>0){int idx=Math.Min(Math.Max(0,selected),pages.Items.Count-1);pages.Items[idx].Selected=true;pages.Items[idx].EnsureVisible();}
  count.Text=infos.Count+" PÁGINAS\nMarca para aplicar en grupo";
  docTitle.Text=(original==null?"Documento combinado":Path.GetFileName(original))+"  ·  "+(Convert.ToDouble(data["bytes"])/1000000).ToString("0.00")+" MB";
  status.Text=(bool)data["signed"]?"Documento con firma digital: modificarlo invalida su firma. Conserva el original.":"Listo · Los originales se conservan. Usa Guardar copia para exportar.";
 }
 async Task Commit(string file,int selected){if(historyPos<history.Count-1)history.RemoveRange(historyPos+1,history.Count-historyPos-1);history.Add(file);historyPos=history.Count-1;current=file;dirty=true;await Refresh(selected);}
 async Task LoadDocument(string file){if(dirty&&MessageBox.Show("Hay cambios sin guardar. ¿Abrir otro documento y descartarlos?","PavPDF",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;string copy=Next();File.Copy(file,copy);string prior=current;current=copy;try{await Refresh(0);}catch{current=prior;throw;}original=file;sourceFiles.Clear();sourceFiles.Add(Path.GetFullPath(file));dirty=false;history.Clear();history.Add(copy);historyPos=0;docTitle.Text=Path.GetFileName(file)+"  ·  "+(new FileInfo(file).Length/1000000d).ToString("0.00")+" MB";}
 async Task Open(){using(var dlg=new OpenFileDialog(){Filter="Documentos PDF|*.pdf"})if(dlg.ShowDialog()==DialogResult.OK)await LoadDocument(dlg.FileName);}
 async Task Merge(){using(var dlg=new OpenFileDialog(){Filter="PDF e imágenes|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff",Multiselect=true})if(dlg.ShowDialog()==DialogResult.OK)await MergeFiles(dlg.FileNames);}
 async Task MergeFiles(string[] files){var list=new List<string>();if(current!=null)list.Add(current);list.AddRange(files);string output=Next();await Engine(new Dictionary<string,object>{{"cmd","merge"},{"files",list},{"output",output}},"Uniendo documentos…");foreach(string file in files)sourceFiles.Add(Path.GetFullPath(file));await Commit(output,0);}
 async Task Undo(int delta){if(historyPos+delta<0||historyPos+delta>=history.Count)return;historyPos+=delta;current=history[historyPos];dirty=true;await Refresh(0);}
 void Detect(){Require();int n=0;for(int i=0;i<pages.Items.Count;i++){bool blank=(bool)infos[i]["blank"];pages.Items[i].Checked=blank;if(blank)n++;}status.Text=n+" posibles páginas en blanco marcadas. Revisa las miniaturas antes de eliminar.";if(n>0)pages.Items[pages.CheckedIndices[0]].Selected=true;}
 async Task Delete(){var ids=SelectedPages();if(MessageBox.Show("¿Eliminar "+ids.Length+" página(s) de esta copia? Puedes deshacer el cambio.","PavPDF",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)await Change("delete",null);}
 async Task Change(string action,Dictionary<string,object> extra){Require();int idx=Selected();var req=Request("mutate");req["action"]=action;req["pages"]=SelectedPages();req["output"]=Next();if(extra!=null)foreach(var kv in extra)req[kv.Key]=kv.Value;await Engine(req,"Aplicando cambios…");await Commit((string)req["output"],idx);}
 async Task MovePage(int direction){int i=Selected(),to=i+direction;if(to<0||to>=infos.Count)return;var order=Enumerable.Range(0,infos.Count).ToArray();order[i]=to;order[to]=i;await Change("reorder",new Dictionary<string,object>{{"order",order}});pages.Items[to].Selected=true;}
 async Task Enhance(){Require();if(MessageBox.Show("Mejora el contraste de las páginas marcadas (o la actual). Conserva el área superior protegida. La página se convierte en imagen a 240 ppp; guarda una copia para revisar el resultado.","Mejorar escaneo",MessageBoxButtons.OKCancel)==DialogResult.OK)await Change("enhance",new Dictionary<string,object>{{"protect_header",protect.Checked},{"protect",(double)header.Value/100}});}
 async Task AreaEdit(string action){Require();if(canvas.Area.Width<.005||canvas.Area.Height<.005)throw new Exception("Dibuja primero un rectángulo sobre la página.");var r=canvas.Area;var extra=new Dictionary<string,object>{{"rect",new[]{r.X,r.Y,r.Width,r.Height}},{"text",editText.Text},{"font_size",fontSize.Value},{"protect_header",protect.Checked},{"protect",(double)header.Value/100},{"pages",new[]{Selected()}}};
  if(action=="image")using(var dlg=new OpenFileDialog(){Filter="Imágenes|*.png;*.jpg;*.jpeg"}){if(dlg.ShowDialog()!=DialogResult.OK)return;extra["image"]=dlg.FileName;}
  await Change(action,extra);
 }
 string OutputPath(string suffix){using(var dlg=new SaveFileDialog(){Filter="Documento PDF|*.pdf",FileName=(original==null?"Documento":Path.GetFileNameWithoutExtension(original))+suffix+".pdf",DefaultExt="pdf"}){if(dlg.ShowDialog()!=DialogResult.OK)return null;if(sourceFiles.Contains(Path.GetFullPath(dlg.FileName)))throw new Exception("Elige un nombre distinto para conservar el original.");return dlg.FileName;}}
 async Task SaveCopy(){Require();string file=OutputPath("_PavPDF");if(file==null)return;var req=Request("export");req["output"]=file;await Engine(req,"Guardando copia…");dirty=false;status.Text="Guardado: "+file;}
 async Task Extract(){Require();string file=OutputPath("_seleccion");if(file==null)return;var req=Request("export");req["kind"]="extract";req["pages"]=SelectedPages();req["output"]=file;await Engine(req,"Extrayendo páginas…");status.Text="Guardado: "+file;}
 async Task ExportImages(){Require();using(var dlg=new FolderBrowserDialog(){Description="Carpeta para las imágenes PNG"}){if(dlg.ShowDialog()!=DialogResult.OK)return;var req=Request("export");req["kind"]="images";req["pages"]=SelectedPages();req["folder"]=dlg.SelectedPath;await Engine(req,"Exportando imágenes…");status.Text="Imágenes guardadas en "+dlg.SelectedPath;}}
 async Task Compress(bool lossless){Require();int dpi=240,quality=88;if(!lossless)using(var dialog=new CompressDialog()){if(dialog.ShowDialog()!=DialogResult.OK)return;dpi=(int)dialog.Dpi.Value;quality=(int)dialog.Quality.Value;}
  long before=new FileInfo(current).Length;var req=Request("compress");req["mode"]=lossless?"lossless":"images";req["dpi"]=dpi;req["quality"]=quality;req["output"]=Next();var data=await Engine(req,"Optimizando el documento…");await Commit((string)req["output"],0);status.Text="Tamaño: "+(before/1000000d).ToString("0.00")+" → "+(Convert.ToDouble(data["bytes"])/1000000).ToString("0.00")+" MB. Guarda la copia o divídela en partes.";
 }
 async Task Split(){Require();using(var dlg=new FolderBrowserDialog(){Description="Elige una carpeta para las partes de máximo 10 MB"}){if(dlg.ShowDialog()!=DialogResult.OK)return;var req=Request("split");req["max_mb"]=10;req["folder"]=dlg.SelectedPath;var data=await Engine(req,"Separando y verificando el tamaño de cada parte…");status.Text=((ArrayList)data["outputs"]).Count+" PDF creados, cada uno de máximo 10.000.000 bytes.";Process.Start(new ProcessStartInfo(dlg.SelectedPath){UseShellExecute=true});}}
 async Task Updates(){if(busy)return;try{ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;string body=await Task.Run(()=>{using(var client=new WebClient()){client.Headers["User-Agent"]="PavPDF/1.0.0";return client.DownloadString("https://api.github.com/repos/"+Repo+"/releases/latest");}});var release=json.Deserialize<Dictionary<string,object>>(body);string tag=Convert.ToString(release["tag_name"]);if(MessageBox.Show("Versión publicada en GitHub: "+tag+"\nVersión instalada: 1.0.0\n\n¿Abrir la página de descarga?","PavPDF",MessageBoxButtons.YesNo)==DialogResult.Yes)Process.Start(new ProcessStartInfo("https://github.com/"+Repo+"/releases/latest"){UseShellExecute=true});}catch{MessageBox.Show("Aún no hay una versión publicada o no se pudo conectar. Repositorio configurado:\nhttps://github.com/"+Repo+"\n\nPuedes usar todas las funciones PDF sin conexión.","PavPDF");}}
}
class CompressDialog:Form {
 public NumericUpDown Dpi=new NumericUpDown(),Quality=new NumericUpDown();
 public CompressDialog(){Text="Comprimir escaneos";Width=440;Height=265;FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterParent;MaximizeBox=false;MinimizeBox=false;Font=new Font("Segoe UI",10);
  Controls.Add(new Label(){Text="Ajusta las imágenes. El texto digital y los vectores se conservan. Esta opción puede reducir detalle.",Location=new Point(20,15),Size=new Size(390,55)});
  Controls.Add(new Label(){Text="Resolución (ppp)",Location=new Point(20,85),AutoSize=true});Dpi.Location=new Point(220,80);Dpi.Minimum=100;Dpi.Maximum=600;Dpi.Value=240;
  Controls.Add(new Label(){Text="Calidad JPEG",Location=new Point(20,125),AutoSize=true});Quality.Location=new Point(220,120);Quality.Minimum=40;Quality.Maximum=100;Quality.Value=88;
  var ok=new Button(){Text="Aplicar",DialogResult=DialogResult.OK,Location=new Point(220,175),Width=90};var cancel=new Button(){Text="Cancelar",DialogResult=DialogResult.Cancel,Location=new Point(115,175),Width=90};Controls.AddRange(new Control[]{Dpi,Quality,ok,cancel});AcceptButton=ok;CancelButton=cancel;
 }
}
