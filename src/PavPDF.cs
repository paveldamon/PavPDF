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

[assembly: System.Windows.Media.DisableDpiAwareness]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
static class Program {
 [STAThread] static void Main(string[] args) {
  Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
  Application.Run(new MainForm(args.Length>0?args[0]:null));
 }
}
class PageCanvas:ScrollableControl {
 public Image PageImage; public RectangleF Area {get;set;} public RectangleF ImageArea;
 public event EventHandler AreaSelected,ZoomChanged;
 public int ZoomPercent {get;private set;}
 float paperWidth=595.276f,paperHeight=841.89f;
 PointF start;Point panStart,panScroll;bool dragging,panning;
 public PageCanvas(){DoubleBuffered=true;AutoScroll=true;BackColor=Color.FromArgb(226,232,240);Cursor=Cursors.Cross;TabStop=true;}
 public float EffectiveScale {get{float w=paperWidth*96/72,h=paperHeight*96/72;return ZoomPercent==0?Math.Min((ClientSize.Width-34f)/w,(ClientSize.Height-34f)/h):ZoomPercent==-1?(ClientSize.Width-34f)/w:ZoomPercent/100f;}}
 void LayoutPage(){
  if(PageImage==null)return;float scale=Math.Max(.05f,EffectiveScale),w=paperWidth*96/72*scale,h=paperHeight*96/72*scale;
  AutoScrollMinSize=ZoomPercent==0?Size.Empty:new Size((int)Math.Ceiling(w+34),(int)Math.Ceiling(h+34));
  ImageArea=new RectangleF(Math.Max(17,(ClientSize.Width-w)/2)+AutoScrollPosition.X,Math.Max(17,(ClientSize.Height-h)/2)+AutoScrollPosition.Y,w,h);
 }
 public void SetZoom(int value){
  float centerX=ImageArea.Width>0?(ClientSize.Width/2f-ImageArea.X)/ImageArea.Width:.5f,centerY=ImageArea.Height>0?(ClientSize.Height/2f-ImageArea.Y)/ImageArea.Height:.5f;
  ZoomPercent=value<=0?Math.Max(-1,value):Math.Max(25,Math.Min(500,value));AutoScrollPosition=Point.Empty;LayoutPage();
  if(ZoomPercent>0)AutoScrollPosition=new Point((int)Math.Max(0,centerX*ImageArea.Width-ClientSize.Width/2f+17),(int)Math.Max(0,centerY*ImageArea.Height-ClientSize.Height/2f+17));
  LayoutPage();Invalidate();if(ZoomChanged!=null)ZoomChanged(this,EventArgs.Empty);
 }
 public void StepZoom(int step){int value=ZoomPercent>0?ZoomPercent:(int)(EffectiveScale*100);SetZoom(value+step);}
 public PointF DocumentPoint(PointF screen){return new PointF(Math.Max(0,Math.Min(1,(screen.X-ImageArea.X)/ImageArea.Width)),Math.Max(0,Math.Min(1,(screen.Y-ImageArea.Y)/ImageArea.Height)));}
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;LayoutPage();
  if(PageImage==null){using(var f=new Font("Segoe UI",19))e.Graphics.DrawString("Tu documento, listo para trabajar",f,Brushes.SlateGray,35,70);return;}
  e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;e.Graphics.FillRectangle(Brushes.White,ImageArea);e.Graphics.DrawImage(PageImage,ImageArea);
  if(!Area.IsEmpty){var r=new RectangleF(ImageArea.X+Area.X*ImageArea.Width,ImageArea.Y+Area.Y*ImageArea.Height,Area.Width*ImageArea.Width,Area.Height*ImageArea.Height);
   using(var b=new SolidBrush(Color.FromArgb(45,0,120,210)))e.Graphics.FillRectangle(b,r);using(var pen=new Pen(Color.FromArgb(0,110,210),2))e.Graphics.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);
  }
 }
 protected override void OnMouseDown(MouseEventArgs e){
  Focus();if(PageImage==null)return;
  if(e.Button==MouseButtons.Middle){panning=true;panStart=e.Location;panScroll=AutoScrollPosition;Cursor=Cursors.Hand;Capture=true;return;}
  if(e.Button!=MouseButtons.Left||!ImageArea.Contains(e.Location))return;start=DocumentPoint(e.Location);dragging=true;Capture=true;Area=RectangleF.Empty;
 }
 protected override void OnMouseMove(MouseEventArgs e){
  if(panning){AutoScrollPosition=new Point(-panScroll.X+panStart.X-e.X,-panScroll.Y+panStart.Y-e.Y);Invalidate();return;}
  if(!dragging)return;var pt=DocumentPoint(e.Location);Area=new RectangleF(Math.Min(start.X,pt.X),Math.Min(start.Y,pt.Y),Math.Abs(pt.X-start.X),Math.Abs(pt.Y-start.Y));Invalidate();
 }
 protected override void OnMouseUp(MouseEventArgs e){bool selected=dragging;dragging=false;panning=false;Capture=false;Cursor=Cursors.Cross;Invalidate();if(selected&&Area.Width>.005&&Area.Height>.005&&AreaSelected!=null)AreaSelected(this,EventArgs.Empty);}
 protected override void OnMouseWheel(MouseEventArgs e){if((ModifierKeys&Keys.Control)==Keys.Control){StepZoom(e.Delta>0?25:-25);return;}base.OnMouseWheel(e);Invalidate();}
 protected override void OnScroll(ScrollEventArgs e){base.OnScroll(e);LayoutPage();Invalidate();}
 protected override void OnResize(EventArgs e){base.OnResize(e);LayoutPage();Invalidate();}
 public void LoadImage(string path,float width,float height,bool reset){if(PageImage!=null)PageImage.Dispose();using(var im=Image.FromFile(path))PageImage=new Bitmap(im);paperWidth=width;paperHeight=height;if(reset){Area=RectangleF.Empty;AutoScrollPosition=Point.Empty;}LayoutPage();Invalidate();}
 public void LoadImage(string path){LoadImage(path,paperWidth,paperHeight,true);}
 protected override void Dispose(bool b){if(b&&PageImage!=null)PageImage.Dispose();base.Dispose(b);}
}
class MainForm:Form {
 const string Repo="paveldamon/PavPDF";
 const string Version="1.1.5";
 JavaScriptSerializer json=new JavaScriptSerializer(){MaxJsonLength=50000000};
 string root=Path.Combine(Path.GetTempPath(),"PavPDF-"+Guid.NewGuid().ToString("N")),current,original;
 List<string> history=new List<string>(); int historyPos=-1,revision=0;
 ListView pages=new ListView(); ImageList thumbs=new ImageList(); PageCanvas canvas=new PageCanvas();
 Label status=new Label(), docTitle=new Label(),count=new Label();
 FlowLayoutPanel tools=new FlowLayoutPanel(),top=new FlowLayoutPanel();
 SpellTextBox editText=new SpellTextBox(); NumericUpDown fontSize=new NumericUpDown(), header=new NumericUpDown();
 CheckBox matchPaper=new CheckBox(),autoFit=new CheckBox(),matchInk=new CheckBox(),closeGap=new CheckBox(),protect=new CheckBox(),autoFont=new CheckBox(),fontBold=new CheckBox(),fontItalic=new CheckBox(); Panel editor=new Panel();
 ComboBox fontFamily=new ComboBox(),zoomChoice=new ComboBox();Label fontInfo=new Label();
 System.Windows.Forms.Timer fontTimer=new System.Windows.Forms.Timer(){Interval=350},previewTimer=new System.Windows.Forms.Timer(){Interval=300};
 bool updatingZoom=false;int previewDpi=105;
 bool busy=false,dirty=false; HashSet<string> sourceFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase); List<Dictionary<string,object>> infos=new List<Dictionary<string,object>>();
 public MainForm(string initial){
  Text="PavPDF 1.1.5 · Documentos claros, trabajo simple"; Width=1370;Height=900;MinimumSize=new Size(1120,720);StartPosition=FormStartPosition.CenterScreen;
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
  pages.SelectedIndexChanged+=(s,e)=>{
   fontTimer.Stop();previewTimer.Stop();fontInfo.Text="Selecciona palabras para detectar la fuente y el tamaño.";
   if(pages.SelectedIndices.Count>0&&infos.Count>pages.SelectedIndices[0]){var info=infos[pages.SelectedIndices[0]];canvas.LoadImage((string)info["preview"],Convert.ToSingle(info["width"]),Convert.ToSingle(info["height"]),true);previewDpi=105;previewTimer.Start();}
  };
  var mark=new FlowLayoutPanel(){Dock=DockStyle.Bottom,Height=35};var all=new Button(){Text="Todas",Width=76,Height=28};var none=new Button(){Text="Ninguna",Width=80,Height=28};
  all.Click+=(s,e)=>{foreach(ListViewItem it in pages.Items)it.Checked=true;};none.Click+=(s,e)=>{foreach(ListViewItem it in pages.Items)it.Checked=false;};mark.Controls.AddRange(new Control[]{all,none});
  pagePanel.Controls.Add(pages);pages.BringToFront();pagePanel.Controls.Add(mark);
  var center=new Panel(){Dock=DockStyle.Fill};docTitle.Dock=DockStyle.Top;docTitle.Height=40;docTitle.Padding=new Padding(14,10,0,0);docTitle.Text="Arrastra un PDF a esta ventana para comenzar";docTitle.Font=new Font("Segoe UI",10,FontStyle.Bold);
  var zoomBar=new FlowLayoutPanel(){Dock=DockStyle.Top,Height=38,Padding=new Padding(10,3,0,0),BackColor=Color.FromArgb(245,248,252)};
  var minus=new Button(){Text="−",Width=35,Height=28};var plus=new Button(){Text="+",Width=35,Height=28};
  zoomChoice.Width=140;zoomChoice.DropDownStyle=ComboBoxStyle.DropDownList;zoomChoice.Items.AddRange(new object[]{"Ajustar página","Ajustar ancho","25%","50%","75%","100%","125%","150%","200%","250%","300%","400%","500%"});zoomChoice.SelectedIndex=0;
  minus.Click+=(s,e)=>canvas.StepZoom(-25);plus.Click+=(s,e)=>canvas.StepZoom(25);
  zoomChoice.SelectedIndexChanged+=(s,e)=>{if(updatingZoom)return;string value=Convert.ToString(zoomChoice.SelectedItem);canvas.SetZoom(value=="Ajustar página"?0:value=="Ajustar ancho"?-1:int.Parse(value.TrimEnd('%')));};
  canvas.ZoomChanged+=(s,e)=>{updatingZoom=true;string value=canvas.ZoomPercent==0?"Ajustar página":canvas.ZoomPercent==-1?"Ajustar ancho":canvas.ZoomPercent+"%";if(!zoomChoice.Items.Contains(value))zoomChoice.Items.Add(value);zoomChoice.SelectedItem=value;updatingZoom=false;previewTimer.Stop();previewTimer.Start();};
  zoomBar.Controls.AddRange(new Control[]{minus,zoomChoice,plus,new Label(){Text="Ctrl + rueda: zoom · Rueda: desplazar · Botón central: mover",AutoSize=true,Padding=new Padding(6,6,0,0),Font=new Font("Segoe UI",8)}});
  editor.Dock=DockStyle.Bottom;editor.Height=270;editor.Padding=new Padding(10);editor.BackColor=Color.FromArgb(245,248,252);
  var instructions=new Label(){Text="EDITAR TEXTO  ·  Dibuja un área sobre las palabras que quieres editar.",Location=new Point(12,6),AutoSize=true};
  var fontRow=new FlowLayoutPanel(){Location=new Point(8,29),Height=33,Width=850};
  fontFamily.Width=155;fontFamily.DropDownStyle=ComboBoxStyle.DropDownList;fontFamily.Items.AddRange(new object[]{"Arial","Arial Narrow","Times New Roman","Calibri","Cambria","Segoe UI","Tahoma","Verdana","Courier New","Helvetica","Times-Roman","Courier"});fontFamily.SelectedItem="Helvetica";
  fontSize.Minimum=1;fontSize.Maximum=300;fontSize.DecimalPlaces=2;fontSize.Increment=.5m;fontSize.Value=11;fontSize.Width=65;
  fontBold.Text="Negrita";fontBold.Width=76;fontItalic.Text="Cursiva";fontItalic.Width=76;autoFont.Text="Detectar automáticamente";autoFont.Checked=true;autoFont.Width=205;
  fontRow.Controls.AddRange(new Control[]{fontFamily,fontSize,new Label(){Text="pt",AutoSize=true,Padding=new Padding(0,5,0,0)},fontBold,fontItalic,autoFont});
  fontInfo.Text="Selecciona palabras para detectar la fuente y el tamaño.";fontInfo.SetBounds(12,64,800,24);fontInfo.Font=new Font("Segoe UI",9);fontInfo.ForeColor=Color.FromArgb(36,88,130);fontInfo.AutoEllipsis=true;
  editText.Location=new Point(12,116);editText.Size=new Size(480,51);
  protect.Text="Proteger membrete";protect.Checked=true;protect.Location=new Point(12,210);protect.Width=170;
  header.Minimum=0;header.Maximum=45;header.Value=16;header.Width=50;header.Location=new Point(185,209);
  closeGap.Text="Unir texto siguiente";closeGap.Checked=true;closeGap.Location=new Point(335,210);closeGap.Width=220;
  matchInk.Text="Igualar tinta del escaneo";matchInk.Checked=true;matchInk.Location=new Point(560,210);matchInk.Width=240;
  autoFit.Text="Ajustar altura automáticamente";autoFit.Checked=true;autoFit.Location=new Point(12,239);autoFit.Width=285;
  matchPaper.Text="Conservar fondo del escaneo";matchPaper.Checked=true;matchPaper.Location=new Point(310,239);matchPaper.Width=290;
  var spelling=new Button(){Text="Corregir texto",Location=new Point(12,85),Width=145,Height=27};spelling.Click+=(s,e)=>editText.Review();
  var spellingHint=new Label(){Text="Español: clic derecho sobre una palabra subrayada para ver sugerencias.",Location=new Point(167,91),AutoSize=true,Font=new Font("Segoe UI",8)};
  var headerLabel=new Label(){Text="% superior",Location=new Point(240,212),AutoSize=true};
  var editButtons=new FlowLayoutPanel(){Location=new Point(8,173),Width=650,Height=33};
  foreach(var pair in new[]{new[]{"Reemplazar texto","edit"},new[]{"Añadir texto","text"},new[]{"Borrar área","redact"}}){string act=pair[1];var b=new Button(){Text=pair[0],Width=145,Height=29};b.Click+=async(s,e)=>await Safe(async()=>await AreaEdit(act));editButtons.Controls.Add(b);}
  var detectFontButton=new Button(){Text="Detectar letra",Width=135,Height=29};detectFontButton.Click+=async(s,e)=>await Safe(async()=>await DetectFont());editButtons.Controls.Add(detectFontButton);
  editor.Controls.AddRange(new Control[]{instructions,fontRow,fontInfo,editText,protect,header,headerLabel,editButtons,closeGap,matchInk,autoFit,spelling,spellingHint,matchPaper});
  editor.SizeChanged+=(s,e)=>{editText.Width=Math.Max(100,editor.ClientSize.Width-24);fontInfo.Width=editor.ClientSize.Width-24;fontRow.Width=editor.ClientSize.Width-16;};
  canvas.AreaSelected+=(s,e)=>{fontTimer.Stop();if(autoFont.Checked)fontTimer.Start();};
  fontTimer.Tick+=async(s,e)=>{fontTimer.Stop();if(!busy&&current!=null)await DetectFont();};
  previewTimer.Tick+=async(s,e)=>{previewTimer.Stop();if(busy){previewTimer.Start();return;}if(current!=null)await LoadSharperPreview();};
  canvas.Dock=DockStyle.Fill;center.Controls.Add(canvas);center.Controls.Add(zoomBar);center.Controls.Add(docTitle);center.Controls.Add(editor);
  Controls.Add(center);Controls.Add(pagePanel);Controls.Add(tools);Controls.Add(top);Controls.Add(headerBar);Controls.Add(bottom);
  AllowDrop=true;DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
  DragDrop+=async(s,e)=>{if(busy)return;var files=(string[])e.Data.GetData(DataFormats.FileDrop);await Safe(async()=>{if(files.Length==1&&files[0].EndsWith(".pdf",StringComparison.OrdinalIgnoreCase))await LoadDocument(files[0]);else await MergeFiles(files);});};
  FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show("Espera a que termine la operación.","PavPDF");}else if(dirty&&MessageBox.Show("Hay cambios sin guardar. ¿Cerrar sin guardarlos?","PavPDF",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)e.Cancel=true;};
  FormClosed+=(s,e)=>{fontTimer.Stop();previewTimer.Stop();fontTimer.Dispose();previewTimer.Dispose();try{Directory.Delete(root,true);}catch{}};
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
  if(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","python.exe"))||!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine.py")))throw new Exception("Faltan componentes de PavPDF. Instala el paquete completo; no ejecutes el archivo de la aplicación por separado.");
  busy=true;tools.Enabled=false;top.Enabled=false;editor.Enabled=false;pages.Enabled=false;canvas.Enabled=false;status.Text=message;UseWaitCursor=true;
  try{return await Task.Run(()=>{
   string id=Guid.NewGuid().ToString("N"),req=Path.Combine(root,id+".json"),res=Path.Combine(root,id+".result.json");File.WriteAllText(req,json.Serialize(request),new System.Text.UTF8Encoding(false));
   var psi=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","python.exe"),"\""+Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine.py")+"\" \""+req+"\" \""+res+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
   using(var proc=Process.Start(psi)){string error=proc.StandardError.ReadToEnd();proc.WaitForExit();if(!File.Exists(res))throw new Exception("El motor PDF no pudo terminar. "+error);}
   var result=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(res));File.Delete(req);File.Delete(res);
   if(!(bool)result["ok"])throw new Exception(Convert.ToString(result["error"]));return (Dictionary<string,object>)result["data"];
  });}finally{busy=false;tools.Enabled=true;top.Enabled=true;editor.Enabled=true;pages.Enabled=true;canvas.Enabled=true;UseWaitCursor=false;}
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
 async Task Change(string action,Dictionary<string,object> extra){Require();int idx=Selected();var req=Request("mutate");req["action"]=action;req["pages"]=SelectedPages();req["output"]=Next();if(extra!=null)foreach(var kv in extra)req[kv.Key]=kv.Value;var result=await Engine(req,"Aplicando cambios…");await Commit((string)req["output"],idx);
  if(result.ContainsKey("font_sizes")){var sizes=result["font_sizes"] as System.Collections.ArrayList;if(sizes!=null&&sizes.Count>0){fontSize.Value=Math.Max(fontSize.Minimum,Math.Min(fontSize.Maximum,Convert.ToDecimal(sizes[0])));fontInfo.Text="Reemplazo aplicado a "+fontSize.Value.ToString("0.##")+" pt; altura ajustada a la línea.";}}}
 async Task MovePage(int direction){int i=Selected(),to=i+direction;if(to<0||to>=infos.Count)return;var order=Enumerable.Range(0,infos.Count).ToArray();order[i]=to;order[to]=i;await Change("reorder",new Dictionary<string,object>{{"order",order}});pages.Items[to].Selected=true;}
 async Task Enhance(){Require();if(MessageBox.Show("Mejora el contraste de las páginas marcadas (o la actual). Conserva el área superior protegida. La página se convierte en imagen a 240 ppp; guarda una copia para revisar el resultado.","Mejorar escaneo",MessageBoxButtons.OKCancel)==DialogResult.OK)await Change("enhance",new Dictionary<string,object>{{"protect_header",protect.Checked},{"protect",(double)header.Value/100}});}
 async Task AreaEdit(string action){Require();if(canvas.Area.Width<.005||canvas.Area.Height<.005)throw new Exception("Dibuja primero un rectángulo sobre la página.");var r=canvas.Area;var extra=new Dictionary<string,object>{{"rect",new[]{r.X,r.Y,r.Width,r.Height}},{"close_gap",closeGap.Checked},{"match_ink",matchInk.Checked},{"auto_fit",autoFit.Checked},{"match_paper",matchPaper.Checked},{"text",editText.Text},{"font_size",fontSize.Value},{"font_family",fontFamily.SelectedItem.ToString()},{"bold",fontBold.Checked},{"italic",fontItalic.Checked},{"protect_header",protect.Checked},{"protect",(double)header.Value/100},{"pages",new[]{Selected()}}};
  if(action=="image")using(var dlg=new OpenFileDialog(){Filter="Imágenes|*.png;*.jpg;*.jpeg"}){if(dlg.ShowDialog()!=DialogResult.OK)return;extra["image"]=dlg.FileName;}
  if((action=="edit"||action=="text"||action=="redact")&&protect.Checked&&r.Y<(float)header.Value/100){
   if(MessageBox.Show(this,"La selección está dentro del "+header.Value+" % superior protegido. ¿Permitir este cambio solo en el rectángulo seleccionado? El resto del membrete seguirá protegido.","Selección dentro del membrete",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
   extra["protect_header"]=false;
  }
  await Change(action,extra);
  if(action=="edit")status.Text="Texto reemplazado. Revisa la página y pulsa Guardar copia para conservar el cambio.";
 }
 async Task DetectFont(){
  if(current==null||busy)return;var r=canvas.Area;if(r.Width<.005||r.Height<.005)return;
  var req=Request("detect_font");req["page"]=Selected();req["rect"]=new[]{r.X,r.Y,r.Width,r.Height};
  try{
   var data=await Engine(req,"Detectando letra y tamaño en el área seleccionada…");string family=Convert.ToString(data["font_family"]);
   if(fontFamily.Items.Contains(family))fontFamily.SelectedItem=family;
   fontSize.Value=Math.Max(fontSize.Minimum,Math.Min(fontSize.Maximum,Convert.ToDecimal(data["font_size"])));
   fontBold.Checked=Convert.ToBoolean(data["bold"]);fontItalic.Checked=Convert.ToBoolean(data["italic"]);
   bool scan=Convert.ToString(data["mode"])=="scan";
   fontInfo.Text=(scan?"Estimación de escaneo: ":"Datos del PDF: ")+Convert.ToString(data["source_font"])+" · "+fontSize.Value.ToString("0.##")+" pt"+(scan?" · confianza "+Convert.ToString(data["confidence"]):Convert.ToBoolean(data["mixed"])?" · varios estilos; se usa el predominante":"")+". "+Convert.ToString(data["note"]);
   status.Text=scan?"Fuente y tamaño aproximados. Revisa el resultado o ajústalo antes de reemplazar.":"Fuente y tamaño leídos del PDF. Elige otra fuente si necesitas sustituirla.";
  }catch(Exception ex){fontInfo.Text="Detección no disponible: "+ex.Message;status.Text="Puedes seleccionar fuente y tamaño manualmente.";}
 }
 async Task LoadSharperPreview(){
  if(current==null||busy)return;int desired=Math.Max(105,Math.Min(300,(int)Math.Ceiling(canvas.EffectiveScale*96)));
  if(desired<=previewDpi)return;int page=Selected();var req=Request("preview");req["page"]=page;req["dpi"]=desired;req["output"]=Path.Combine(root,"zoom_"+Guid.NewGuid().ToString("N")+".png");
  try{await Engine(req,"Preparando vista ampliada…");var info=infos[page];canvas.LoadImage((string)req["output"],Convert.ToSingle(info["width"]),Convert.ToSingle(info["height"]),false);previewDpi=desired;status.Text="Vista ampliada · El zoom no modifica el PDF.";}catch(Exception ex){status.Text="No se pudo mejorar la vista previa: "+ex.Message;}
 }
 string OutputPath(string suffix){using(var dlg=new SaveFileDialog(){Filter="Documento PDF|*.pdf",FileName=(original==null?"Documento":Path.GetFileNameWithoutExtension(original))+suffix+".pdf",DefaultExt="pdf"}){if(dlg.ShowDialog()!=DialogResult.OK)return null;if(sourceFiles.Contains(Path.GetFullPath(dlg.FileName)))throw new Exception("Elige un nombre distinto para conservar el original.");return dlg.FileName;}}
 async Task SaveCopy(){Require();string file=OutputPath("_PavPDF");if(file==null)return;var req=Request("export");req["output"]=file;await Engine(req,"Guardando copia…");dirty=false;status.Text="Guardado: "+file;}
 async Task Extract(){Require();string file=OutputPath("_seleccion");if(file==null)return;var req=Request("export");req["kind"]="extract";req["pages"]=SelectedPages();req["output"]=file;await Engine(req,"Extrayendo páginas…");status.Text="Guardado: "+file;}
 async Task ExportImages(){Require();using(var dlg=new FolderBrowserDialog(){Description="Carpeta para las imágenes PNG"}){if(dlg.ShowDialog()!=DialogResult.OK)return;var req=Request("export");req["kind"]="images";req["pages"]=SelectedPages();req["folder"]=dlg.SelectedPath;await Engine(req,"Exportando imágenes…");status.Text="Imágenes guardadas en "+dlg.SelectedPath;}}
 async Task Compress(bool lossless){Require();int dpi=240,quality=88;if(!lossless)using(var dialog=new CompressDialog()){if(dialog.ShowDialog()!=DialogResult.OK)return;dpi=(int)dialog.Dpi.Value;quality=(int)dialog.Quality.Value;}
  long before=new FileInfo(current).Length;var req=Request("compress");req["mode"]=lossless?"lossless":"images";req["dpi"]=dpi;req["quality"]=quality;req["output"]=Next();var data=await Engine(req,"Optimizando el documento…");await Commit((string)req["output"],0);status.Text="Tamaño: "+(before/1000000d).ToString("0.00")+" → "+(Convert.ToDouble(data["bytes"])/1000000).ToString("0.00")+" MB. Guarda la copia o divídela en partes.";
 }
 async Task Split(){Require();using(var dlg=new FolderBrowserDialog(){Description="Elige una carpeta para las partes de máximo 10 MB"}){if(dlg.ShowDialog()!=DialogResult.OK)return;var req=Request("split");req["max_mb"]=10;req["folder"]=dlg.SelectedPath;var data=await Engine(req,"Separando y verificando el tamaño de cada parte…");status.Text=((ArrayList)data["outputs"]).Count+" PDF creados, cada uno de máximo 10.000.000 bytes.";Process.Start(new ProcessStartInfo(dlg.SelectedPath){UseShellExecute=true});}}
 async Task Updates(){if(busy)return;try{ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;string body=await Task.Run(()=>{using(var client=new WebClient()){client.Headers["User-Agent"]="PavPDF/"+Version;return client.DownloadString("https://api.github.com/repos/"+Repo+"/releases/latest");}});var release=json.Deserialize<Dictionary<string,object>>(body);string tag=Convert.ToString(release["tag_name"]);if(MessageBox.Show("Versión publicada en GitHub: "+tag+"\nVersión instalada: "+Version+"\n\n¿Abrir la página de descarga?","PavPDF",MessageBoxButtons.YesNo)==DialogResult.Yes)Process.Start(new ProcessStartInfo("https://github.com/"+Repo+"/releases/latest"){UseShellExecute=true});}catch{MessageBox.Show("Aún no hay una versión publicada o no se pudo conectar. Repositorio configurado:\nhttps://github.com/"+Repo+"\n\nPuedes usar todas las funciones PDF sin conexión.","PavPDF");}}
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

// Spanish spelling suggestions run locally through the Windows/WPF dictionary.
class SpellTextBox : System.Windows.Forms.Integration.ElementHost {
 public readonly System.Windows.Controls.TextBox Editor;
 public SpellTextBox(){
  Editor=new System.Windows.Controls.TextBox();
  Editor.AcceptsReturn=true;Editor.TextWrapping=System.Windows.TextWrapping.Wrap;
  Editor.VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto;
  Editor.FontSize=14;Editor.FontFamily=new System.Windows.Media.FontFamily("Segoe UI");
  Editor.Language=System.Windows.Markup.XmlLanguage.GetLanguage("es-ES");
  Editor.SpellCheck.IsEnabled=true;Child=Editor;
 }
 public override string Text {get{return Editor==null?"":Editor.Text;}set{if(Editor!=null)Editor.Text=value??"";}}
 public void Review(){
  int index=Editor.GetNextSpellingErrorCharacterIndex(0,System.Windows.Documents.LogicalDirection.Forward);
  if(index<0){System.Windows.Forms.MessageBox.Show("No se encontraron faltas de ortografía. Revisa también nombres, cifras y el sentido del texto.","Revisar texto");return;}
  var dialog=new System.Windows.Forms.Form(){Text="Corregir texto · Español",Width=460,Height=330,StartPosition=System.Windows.Forms.FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false};
  var label=new System.Windows.Forms.Label(){Left=15,Top=15,Width=415,Height=40};
  var choices=new System.Windows.Forms.ListBox(){Left=15,Top=60,Width=415,Height=160};
  var apply=new System.Windows.Forms.Button(){Text="Aplicar sugerencia",Left=15,Top=235,Width=145};
  var skip=new System.Windows.Forms.Button(){Text="Omitir",Left=170,Top=235,Width=110};
  var close=new System.Windows.Forms.Button(){Text="Cerrar",Left=290,Top=235,Width=140};
  System.Action show=null;
  show=()=>{
   if(index<0){dialog.Close();return;}
   int length=Editor.GetSpellingErrorLength(index);var error=Editor.GetSpellingError(index);
   if(error==null||length<=0){dialog.Close();return;}
   label.Text="Revisar: "+Editor.Text.Substring(index,length);choices.Items.Clear();
   foreach(string suggestion in error.Suggestions)choices.Items.Add(suggestion);
   if(choices.Items.Count>0)choices.SelectedIndex=0;apply.Enabled=choices.Items.Count>0;
   Editor.Select(index,length);
  };
  apply.Click+=(s,e)=>{if(choices.SelectedItem==null)return;int length=Editor.GetSpellingErrorLength(index);string value=choices.SelectedItem.ToString();Editor.Select(index,length);Editor.SelectedText=value;index=Editor.GetNextSpellingErrorCharacterIndex(index+value.Length,System.Windows.Documents.LogicalDirection.Forward);show();};
  skip.Click+=(s,e)=>{index=Editor.GetNextSpellingErrorCharacterIndex(index+System.Math.Max(1,Editor.GetSpellingErrorLength(index)),System.Windows.Documents.LogicalDirection.Forward);show();};
  close.Click+=(s,e)=>dialog.Close();dialog.Controls.AddRange(new System.Windows.Forms.Control[]{label,choices,apply,skip,close});
  show();dialog.ShowDialog(this.FindForm());dialog.Dispose();
 }
}
