// SPDX-License-Identifier: AGPL-3.0-or-later
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

enum UpdateStatus { Invalid, Older, Current, Incomplete, Available }

sealed class UpdateDecision {
 public UpdateStatus Status;
 public string Message, PageUrl, InstallerUrl, ChecksumUrl;
 public bool CanDownload { get { return Status == UpdateStatus.Available; } }
}

static class UpdatePolicy {
 public const string Repository = "paveldamon/PavPDF";

 // Normalize numeric parts: 1.10 is newer than 1.9, and 1.1 equals 1.1.0.
 public static bool TryVersion(string value, bool allowLocal, out Version version) {
  version = null;
  if (String.IsNullOrWhiteSpace(value)) return false;
  value = value.Trim();
  if (allowLocal && value.EndsWith("-local", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 6);
  var match = Regex.Match(value, @"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?$");
  if (!match.Success) return false;
  int[] parts = new int[4];
  for (int i = 0; i < parts.Length; i++) {
   if (match.Groups[i + 1].Success && !Int32.TryParse(match.Groups[i + 1].Value, out parts[i])) return false;
  }
  version = new Version(parts[0], parts[1], parts[2], parts[3]);
  return true;
 }

 public static string ReadInstalledVersion(string installRoot) {
  string path = Path.Combine(installRoot, "pavpdf.install");
  if (!File.Exists(path)) return null;
  string value = File.ReadAllText(path).Trim();
  if (value.StartsWith("PavPDF ", StringComparison.Ordinal)) value = value.Substring(7).Trim();
  Version parsed;
  if (!TryVersion(value, true, out parsed)) throw new InvalidOperationException("No se pudo identificar la versión instalada de PavPDF. No se realizará ningún cambio.");
  return value;
 }

 public static void EnsureInstallAllowed(string installed, string candidate) {
  Version target, current;
  if (!TryVersion(candidate, false, out target)) throw new InvalidOperationException("El instalador no tiene una versión válida.");
  if (installed == null) return;
  if (!TryVersion(installed, true, out current)) throw new InvalidOperationException("No se pudo identificar la versión instalada. No se realizará ningún cambio.");
  if (target < current) throw new InvalidOperationException("Ya tienes PavPDF " + installed + ", una versión más reciente que este instalador (" + candidate + "). Conserva tu instalación actual.");
 }

 static string Text(Dictionary<string,object> data, string key) {
  object value;
  return data != null && data.TryGetValue(key, out value) ? Convert.ToString(value) : "";
 }
 static bool Flag(Dictionary<string,object> data, string key) {
  object value;
  return data != null && data.TryGetValue(key, out value) && value is bool && (bool)value;
 }
 static UpdateDecision Result(UpdateStatus status, string message) {
  return new UpdateDecision { Status = status, Message = message };
 }
 static bool ValidAsset(string url, string tag, string filename) {
  Uri uri;
  return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
   uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
   uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
   uri.AbsolutePath == "/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + filename;
 }

 public static UpdateDecision Assess(Dictionary<string,object> release, string installed) {
  string tag = Text(release, "tag_name");
  Version published, current = null;
  if (release == null || Flag(release, "draft") || Flag(release, "prerelease") ||
      !TryVersion(tag, false, out published) || (installed != null && !TryVersion(installed, true, out current)))
   return Result(UpdateStatus.Invalid, "No se pudo identificar una versión estable válida en GitHub. No se realizará ninguna descarga.");
  string versions = "\n\nVersión instalada: " + (installed ?? "ninguna") + "\nVersión publicada: " + tag;
  if (current != null && published < current)
   return Result(UpdateStatus.Older, "Ya tienes una versión más reciente." + versions + "\n\nConserva tu instalación actual. No hay una actualización disponible.");
  if (current != null && published == current)
   return Result(UpdateStatus.Current, "PavPDF está actualizado." + versions + "\n\nNo necesitas instalar nada.");
  string installer = null, checksum = null;
  object rawAssets;
  if (release.TryGetValue("assets", out rawAssets) && rawAssets is IEnumerable) {
   foreach (object item in (IEnumerable)rawAssets) {
    var asset = item as Dictionary<string,object>;
    if (asset == null || Text(asset, "state") != "uploaded") continue;
    string name = Text(asset, "name"), url = Text(asset, "browser_download_url");
    if (!ValidAsset(url, tag, name)) continue;
    if (name == "PavPDF-Setup.exe") installer = url;
    if (name == "PavPDF-Setup.exe.sha256") checksum = url;
   }
  }
  if (installer == null || checksum == null)
   return Result(UpdateStatus.Incomplete, "La versión " + tag + " todavía no tiene el instalador completo y su archivo de verificación disponibles. Intenta más tarde.\n\nNo se realizará ninguna descarga.");
  return new UpdateDecision {
   Status = UpdateStatus.Available,
   Message = "Hay una actualización disponible." + versions,
   PageUrl = "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag),
   InstallerUrl = installer, ChecksumUrl = checksum
  };
 }
}
