param([Parameter(Mandatory=$true)][string]$RuntimePath,[string]$BuildDirectory,[string]$ProjectDirectory=$PSScriptRoot)
$ErrorActionPreference='Stop'
$projectRoot=$ProjectDirectory
$runtimeRoot=(Resolve-Path -LiteralPath $RuntimePath).Path
if (-not $BuildDirectory) { $BuildDirectory=Join-Path $env:TEMP ('PavPDF-build-'+[guid]::NewGuid().ToString('N')) }
$buildRoot=[IO.Path]::GetFullPath($BuildDirectory)
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
$package=Join-Path $buildRoot 'package'
if (Test-Path -LiteralPath $package) { throw 'Usa una carpeta de compilación nueva.' }
New-Item -ItemType Directory -Path $package | Out-Null
Copy-Item -LiteralPath $runtimeRoot -Destination (Join-Path $package 'runtime') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\engine.py') -Destination $package
foreach ($name in @('LEEME.html','README.md','LICENSE.txt','build.ps1')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $package }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src') -Destination (Join-Path $package 'src') -Recurse
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon=Join-Path $projectRoot 'src\PavPDF.ico'
& $csc /nologo /target:winexe /platform:x64 "/win32icon:$icon" "/out:$package\PavPDF.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'src\PavPDF.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de PavPDF.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload=Join-Path $buildRoot 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($package,$payload,[IO.Compression.CompressionLevel]::Optimal,$false)
$references=@('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:System.Web.Extensions.dll','/reference:Microsoft.CSharp.dll')
& $csc /nologo /target:winexe /platform:x64 "/win32icon:$icon" "/out:$projectRoot\PavPDF-Setup.exe" "/resource:$payload,payload.zip" @references (Join-Path $projectRoot 'src\Installer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del instalador.' }
& $csc /nologo /target:winexe /platform:x64 /define:ONLINE "/win32icon:$icon" "/out:$projectRoot\PavPDF-Instalar-desde-GitHub.exe" @references (Join-Path $projectRoot 'src\Installer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del instalador conectado.' }
$hash=(Get-FileHash -LiteralPath (Join-Path $projectRoot 'PavPDF-Setup.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $projectRoot 'PavPDF-Setup.exe.sha256'),$hash+'  PavPDF-Setup.exe'+[Environment]::NewLine,[Text.Encoding]::ASCII)
$sourceStage=Join-Path $buildRoot 'source'
New-Item -ItemType Directory -Path $sourceStage | Out-Null
foreach($name in @('src','dependencies','README.md','LEEME.html','LICENSE.txt','build.ps1','PRUEBAS.json')) {
 $path=Join-Path $projectRoot $name
 if (Test-Path -LiteralPath $path) {Copy-Item -LiteralPath $path -Destination $sourceStage -Recurse}
}
$sourceZip=Join-Path $projectRoot 'PavPDF-Fuentes.zip'
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
[IO.Compression.ZipFile]::CreateFromDirectory($sourceStage,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$false)
Write-Output ('Instalador: '+(Join-Path $projectRoot 'PavPDF-Setup.exe'))
Write-Output ('Paquete de prueba: '+$package)
