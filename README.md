# PavPDF 1.1.6

Editor de PDF para Windows 10/11 de 64 bits. Los documentos se procesan en el equipo. Consulta los instaladores disponibles en [GitHub Releases](https://github.com/paveldamon/PavPDF/releases). El código del repositorio puede ser más reciente que el último instalador publicado.

## Contenido del código completo

- `src/PavPDF.cs`: interfaz WinForms, visor, zoom y cuadro WPF con corrector ortográfico en español.
- `src/engine.py`: edición, reconstrucción del fondo, conservación del tono de tinta, organización, compresión y división de PDF.
- `src/typography.py`: lectura de fuentes y estimación visual de tipografías de escaneos.
- `src/WindowsOcr.cs`: reconocimiento de texto local de Windows.
- `src/UpdatePolicy.cs`: comparación de versiones y validación de descargas compartida por el programa y los instaladores.
- `src/Installer.cs`: instalador completo, desinstalación e instalador opcional conectado a GitHub.
- `src/test_*.py` y `src/*Test.cs`: pruebas y casos de regresión.
- `build.ps1`: compilación del programa y los instaladores.
- `runtime/`: Python 3.12.10 de 64 bits, PyMuPDF 1.26.4 y Pillow 11.3.0, listo para compilar y ejecutar sin descargarlos de nuevo. Se incluye en el ZIP de código completo.
- `dependencies/`: fuentes de PyMuPDF y MuPDF, licencias, procedencia y sumas de verificación.
- `PRUEBAS.json`: resultado de las verificaciones de esta entrega.

## Instalar

Guarda tus cambios y cierra PavPDF. Ejecuta `PavPDF-Setup.exe`. Instala para el usuario actual e incluye el motor PDF. Abre PavPDF desde su acceso del escritorio o Inicio. No ejecutes solamente el archivo PavPDF.exe fuera de su carpeta: necesita los archivos que lo acompañan.

Requiere .NET Framework 4.8 y, para reconocimiento y ortografía, los componentes de idioma español de Windows. Las fuentes se toman de las instaladas en Windows; no se redistribuyen las fuentes comerciales.

## Cambios de esta entrega

**Actualizaciones corregidas:** el botón compara los números de versión. Si GitHub ofrece una versión anterior, conserva la instalada y muestra que ya es más reciente. Si son iguales, indica que PavPDF está actualizado. Solo ofrece una descarga cuando la versión es más nueva y están disponibles el instalador y su SHA-256. El enlace abre la versión comprobada. El instalador conectado aplica la misma comprobación; el instalador completo impide retroceder de versión y permite reparar la misma versión.

El código fuente del repositorio y los instaladores de Releases se publican por separado: subir código no convierte esa versión en una actualización descargable.

La opción **Conservar fondo del escaneo**, activada por defecto, reconstruye el tono y el grano del papel con muestras claras cercanas al área editada. Reemplaza el relleno blanco en los escaneos y funciona con y sin **Unir texto siguiente**. La reconstrucción es aproximada: conviene revisar documentos con manchas, tramas, tablas o fondos complejos.

Se mantienen **Igualar tinta del escaneo**, el ajuste de altura, el corrector en español, el zoom y el historial Deshacer/Rehacer. La fuente de un escaneo se estima: revisa especialmente los resultados de confianza baja y ajusta manualmente fuente, negrita y tamaño si hace falta.

## Reemplazar texto

1. Abre una copia del PDF y selecciona las palabras originales de una sola línea.
2. Escribe el reemplazo. Pulsa **Corregir texto** para revisar sugerencias, o usa el menú contextual de una palabra subrayada. La ortografía no decide el significado ni modifica cifras automáticamente.
3. Deja **Conservar fondo del escaneo**, **Igualar tinta del escaneo** y **Ajustar altura automáticamente** activados.
4. **Unir texto siguiente** desplaza el resto de la misma línea para cerrar el hueco. Desactívalo en formularios o columnas con posiciones fijas. No redistribuye automáticamente un párrafo completo.
5. Pulsa **Reemplazar texto**, revisa el resultado con zoom y usa **Guardar copia**.

El membrete protege inicialmente el 16 % superior. Si la selección entra en esa franja, la aplicación pide autorizar únicamente esa edición. El texto nuevo con tinta suavizada conserva una capa buscable. Al desplazar el resto de una línea escaneada se conservan sus píxeles; su capa OCR previa puede necesitar nuevo reconocimiento.

**Añadir texto** conserva lo que existe debajo. **Borrar área** continúa usando un relleno blanco. El fondo reconstruido no recupera la textura original que estaba oculta por las letras; produce una aproximación a partir del papel vecino. La aplicación no garantiza que una modificación sea imperceptible.

## Organizar y preparar archivos de hasta 10 MB

Puedes unir PDF e imágenes, detectar posibles páginas en blanco, eliminar, girar, mover, duplicar y extraer páginas; recortar, añadir imágenes, numerar y exportar PNG. Revisa las páginas marcadas antes de eliminarlas.

**Optimizar sin pérdida** conserva el contenido. **Comprimir escaneos** puede reducir detalle. **Dividir en partes ≤ 10 MB** verifica que cada archivo tenga como máximo 10.000.000 bytes sin recomprimir. Si una página sola supera el límite, primero comprímela. No se garantiza una reducción arbitraria sin pérdida.

La mejora de escaneo rasteriza a 240 ppp y puede aumentar el tamaño. Cualquier edición de un documento firmado digitalmente altera la validez de su firma; conserva el archivo original.

## Compilar desde este ZIP

Extrae todo el ZIP en una carpeta con espacio libre. No hace falta Visual Studio: se usa el compilador de .NET Framework y WinMetadata de Windows. La carpeta `runtime` debe permanecer completa.

Abre PowerShell en esa carpeta y ejecuta:

```powershell
$project = (Get-Location).Path
& ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $project 'build.ps1')))) -RuntimePath (Join-Path $project 'runtime') -ProjectDirectory $project -BuildDirectory (Join-Path $project ('build-' + [guid]::NewGuid().ToString('N')))
```

El script compila PavPDF.exe y WindowsOcr.exe y ejecuta las pruebas de actualizaciones; genera PavPDF-Setup.exe, su SHA-256, el instalador conectado y PavPDF-Fuentes.zip. Usa una carpeta de compilación nueva en cada ejecución. El script no publica ni descarga nada.

Para verificar el motor desde el código completo:

```powershell
New-Item -ItemType Directory -Force work-tests | Out-Null
.\runtime\python.exe .\src\run_tests.py --scratch .\work-tests --output .\PRUEBAS-nuevas.json
```

Para agregar las pruebas de detección con OCR, añade `--ocr RUTA_DEL_PAQUETE\\WindowsOcr.exe` después de compilar. Los ensayos C# conservan sus argumentos documentados en `src/PRUEBAS-UI.md`. El corrector de Windows debe probarse en una sesión de escritorio normal; un entorno restringido puede impedir que se active.

## GitHub y licencias

El repositorio configurado es [paveldamon/PavPDF](https://github.com/paveldamon/PavPDF). El botón de actualizaciones consulta la última versión pública. `PavPDF-Instalar-desde-GitHub.exe` descarga el instalador de la última versión publicada y verifica su SHA-256. No contiene contraseñas ni necesita la sesión de Chrome. Para compilar desde una copia del repositorio, descarga `PavPDF-Fuentes.zip` de la misma Release: incluye el runtime y las fuentes de las dependencias que no se guardan en Git.

PavPDF: AGPL-3.0-or-later, texto completo en `LICENSE.txt`. PyMuPDF y MuPDF: fuentes y avisos en `dependencies/`. Python y Pillow conservan sus licencias en `runtime/`. Los ejecutables no incluyen firma Authenticode.
