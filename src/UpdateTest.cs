// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

static class UpdateTest {
 static int checks;
 static void Check(bool result, string description) {
  if (!result) throw new Exception(description);
  checks++; Console.WriteLine("PASS: " + description);
 }
 static Dictionary<string,object> Release(string tag) {
  var assets = new ArrayList();
  foreach (string name in new[]{"PavPDF-Setup.exe", "PavPDF-Setup.exe.sha256"})
   assets.Add(new Dictionary<string,object> { {"name",name}, {"state","uploaded"}, {"browser_download_url","https://github.com/paveldamon/PavPDF/releases/download/" + tag + "/" + name} });
  // Exercise the real API deserialization used by the app and both installers.
  var json = new JavaScriptSerializer();
  return json.Deserialize<Dictionary<string,object>>(json.Serialize(new Dictionary<string,object> { {"tag_name",tag}, {"draft",false}, {"prerelease",false}, {"assets",assets} }));
 }
 static void NoDownload(UpdateDecision d, UpdateStatus expected, string label) {
  Check(d.Status == expected && !d.CanDownload && d.PageUrl == null && d.InstallerUrl == null && d.ChecksumUrl == null, label);
 }
 static bool RejectedInstall(string installed, string candidate) {
  try { UpdatePolicy.EnsureInstallAllowed(installed,candidate); return false; }
  catch(InvalidOperationException) { return true; }
 }
 static int Main(string[] args) {
  try {
   NoDownload(UpdatePolicy.Assess(Release("v1.1.0"),"1.1.5"), UpdateStatus.Older, "caso reportado: 1.1.5 instalada y v1.1.0 publicada no ofrece descarga");
   NoDownload(UpdatePolicy.Assess(Release("v1.1.5"),"1.1.5"), UpdateStatus.Current, "misma versión: actualizado");
   NoDownload(UpdatePolicy.Assess(Release("v1.1.5"),"1.1.5-local"), UpdateStatus.Current, "versión local equivalente: no reinstala");
   var newer = UpdatePolicy.Assess(Release("v1.1.6"),"1.1.5");
   Check(newer.CanDownload && newer.PageUrl.EndsWith("/tag/v1.1.6") && newer.InstallerUrl.EndsWith("/v1.1.6/PavPDF-Setup.exe"), "versión nueva: descarga fijada a la versión comprobada");
   Check(UpdatePolicy.Assess(Release("v1.10.0"),"1.9.9").CanDownload, "comparación numérica: 1.10 supera 1.9.9");
   NoDownload(UpdatePolicy.Assess(Release("v1.9.9"),"1.10.0"), UpdateStatus.Older, "comparación numérica: 1.9.9 no supera 1.10");
   NoDownload(UpdatePolicy.Assess(Release("v1.1.0.0"),"1.1"), UpdateStatus.Current, "normaliza componentes de versión ausentes");
   Check(UpdatePolicy.Assess(Release("v1.1.6"),null).CanDownload, "instalación nueva permitida");
   foreach(string tag in new[]{"", "latest", "v1.1.6-beta", "1.2.3.4.5", "999999999999.2.3"})
    NoDownload(UpdatePolicy.Assess(Release(tag),"1.1.5"), UpdateStatus.Invalid, "rechaza versión desconocida: " + tag);
   NoDownload(UpdatePolicy.Assess(Release("v1.1.6"),"desconocida"), UpdateStatus.Invalid, "versión instalada desconocida no descarga");
   NoDownload(UpdatePolicy.Assess(null,"1.1.5"), UpdateStatus.Invalid, "respuesta vacía no descarga");
   foreach(string flag in new[]{"draft","prerelease"}) {
    var release = Release("v1.1.6"); release[flag] = true;
    NoDownload(UpdatePolicy.Assess(release,"1.1.5"), UpdateStatus.Invalid, flag + " no se ofrece como estable");
   }
   var incomplete = Release("v1.1.6"); incomplete["assets"] = new object[0];
   NoDownload(UpdatePolicy.Assess(incomplete,"1.1.5"), UpdateStatus.Incomplete, "sin instalador: no ofrece actualización");
   incomplete = Release("v1.1.6"); var assets = (IList)incomplete["assets"]; ((Dictionary<string,object>)assets[1])["state"] = "new";
   NoDownload(UpdatePolicy.Assess(incomplete,"1.1.5"), UpdateStatus.Incomplete, "verificación pendiente de carga: no ofrece actualización");
   foreach(string url in new[]{"https://example.com/PavPDF-Setup.exe", "https://github.com/otro/PavPDF/releases/download/v1.1.6/PavPDF-Setup.exe", "https://github.com/paveldamon/PavPDF/releases/download/v1.1.0/PavPDF-Setup.exe"}) {
    var release = Release("v1.1.6"); var items = (IList)release["assets"]; ((Dictionary<string,object>)items[0])["browser_download_url"] = url;
    NoDownload(UpdatePolicy.Assess(release,"1.1.5"), UpdateStatus.Incomplete, "rechaza un instalador ajeno o de otra versión");
   }
   Check(RejectedInstall("1.1.6","1.1.5"), "instalador completo impide retroceder");
   Check(RejectedInstall("1.10.0","1.9.0"), "instalador completo compara números");
   UpdatePolicy.EnsureInstallAllowed("1.1.5-local","1.1.6"); Check(true,"actualiza una prueba local");
   UpdatePolicy.EnsureInstallAllowed("1.1.6","1.1.6"); Check(true,"permite reparar la misma versión con el instalador completo");
   string scratch = Path.Combine(args[0], "update-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
   Check(UpdatePolicy.ReadInstalledVersion(scratch) == null,"sin marcador: instalación nueva");
   File.WriteAllText(Path.Combine(scratch,"pavpdf.install"),"PavPDF 1.1.5-local");
   Check(UpdatePolicy.ReadInstalledVersion(scratch) == "1.1.5-local","lee el marcador de instalaciones anteriores");
   File.WriteAllText(Path.Combine(scratch,"pavpdf.install"),"no válido");
   bool invalid = false; try { UpdatePolicy.ReadInstalledVersion(scratch); } catch(InvalidOperationException) {invalid=true;}
   Check(invalid,"marcador desconocido conserva la instalación");
   if(args.Length > 1) {
    var live = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(args[1]));
    NoDownload(UpdatePolicy.Assess(live,"1.1.6"),UpdateStatus.Older,"respuesta real de GitHub v1.1.0: conserva 1.1.6");
   }
   Console.WriteLine("PASS: " + checks + " comprobaciones"); return 0;
  } catch(Exception e) { Console.WriteLine("FAIL: " + e); return 1; }
 }
}
