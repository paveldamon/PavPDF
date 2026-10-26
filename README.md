# PavPDF 1.0

**[Descargar instalador para Windows](https://github.com/paveldamon/PavPDF/releases/latest/download/PavPDF-Setup.exe)** · [Todas las descargas](https://github.com/paveldamon/PavPDF/releases/latest)


Aplicación de escritorio en español para Windows 10/11 de 64 bits. Trabaja localmente con PDF, sin subir los documentos a un servidor.

## Instalación

Ejecuta **PavPDF-Setup.exe**. Instala para el usuario actual, incluye Python y el motor PDF, crea accesos en Inicio y el escritorio y aparece en Aplicaciones instaladas de Windows. No requiere instalar Python por separado.

**PavPDF-Instalar-desde-GitHub.exe** es el instalador pequeño: consulta la última Release pública de `paveldamon/PavPDF`, descarga el instalador completo, verifica el SHA-256 publicado y lo abre. Necesita que exista esa Release; el instalador completo funciona antes de publicarla.

## Funciones

- Vista previa de páginas y selección por casillas.
- Detección conservadora de posibles páginas en blanco. Revisa las marcadas y pulsa Eliminar.
- Eliminar, girar, subir, bajar, duplicar y extraer páginas.
- Unir PDF e incorporar imágenes PNG, JPEG o TIFF. Se añaden al final; luego puedes reorganizar páginas.
- Mejorar contraste de escaneos con franja superior protegida.
- Editar escaneos dibujando un área y reemplazando su contenido por texto nuevo; añadir texto o borrar áreas.
- Insertar una imagen, sello o firma que ya tengas como archivo. No genera una firma digital certificada.
- Recortar, numerar y exportar páginas a PNG.
- Deshacer y rehacer durante la sesión.
- Optimización sin pérdida; compresión de imágenes con resolución y calidad elegibles.
- Dividir en varios PDF de máximo **10.000.000 bytes**, manteniendo el orden y sin recomprimir.
- Consultar versiones publicadas en GitHub.

## Membretes y edición

La protección del membrete está activada inicialmente para el 16% superior. Puedes ajustar el porcentaje. Reemplazar texto, añadir texto y borrar áreas rechazan selecciones dentro de esa franja. Al mejorar un escaneo, los píxeles renderizados de la franja se conservan sin aplicarles contraste. El recorte y la inserción de imágenes son herramientas manuales: revisa dónde dibujas el área.

La edición de un escaneo es visual y localizada: selecciona un rectángulo y escribe el reemplazo. No convierte automáticamente el documento en Word ni incluye OCR automático en esta versión. Para mantener un fondo distinto del blanco conviene usar Añadir texto; Reemplazar y Borrar dejan fondo blanco. El texto admite caracteres latinos, incluidos acentos y ñ.

## Cómo cumplir el límite de 10 MB

1. Abre el documento, elimina las páginas que no necesitas y guarda una copia.
2. Prueba **Optimizar sin pérdida**. Conserva el contenido, pero la reducción depende del PDF.
3. Usa **Dividir en partes ≤ 10 MB** para conservar la calidad. No cambia el tamaño del papel ni los píxeles de las imágenes.
4. Si una sola página supera el límite, el programa lo indica y no entrega partes incompletas. Usa **Comprimir escaneos**, revisa su legibilidad y vuelve a dividir.

No existe una reducción arbitraria garantizada sin pérdida para todos los PDF. La compresión JPEG puede reducir detalle; el modo sin pérdida y la división permiten evitar esa degradación. La mejora del escaneo rasteriza las páginas seleccionadas a 240 ppp y puede aumentar el tamaño. Las marcas de firma o sellos visibles se preservan en la imagen, pero cualquier edición invalida las firmas digitales certificadas.

## Publicar en GitHub

La cuenta configurada es **paveldamon** y el repositorio previsto es **PavPDF**. Este paquete no crea ni publica el repositorio por sí solo.

1. Crea el repositorio público `paveldamon/PavPDF` y sube el código fuente de este paquete, incluidos `src`, `build.ps1`, licencias y documentación.
2. Crea una Release con etiqueta `v1.0.0`.
3. Adjunta con estos nombres exactos: `PavPDF-Setup.exe`, `PavPDF-Setup.exe.sha256`, `PavPDF-Instalar-desde-GitHub.exe` y `PavPDF-Fuentes.zip`.
4. Al publicar la Release, el instalador pequeño y el botón de actualizaciones podrán encontrarla. No necesitan acceder a tu sesión de Chrome ni guardar tu contraseña.

Los ejecutables no tienen firma Authenticode. La comprobación SHA-256 verifica integridad frente al archivo publicado en la misma Release; no sustituye una firma de editor.

## Compilar

Necesita el compilador de .NET Framework incluido en Windows, un directorio con Python 3.12 embebido de 64 bits, PyMuPDF 1.26.4 y Pillow 11.3.0. Ejecuta `build.ps1 -RuntimePath RUTA_DEL_RUNTIME`. El script no descarga dependencias. El instalador incluye el runtime listo para usar. Para reconstruir con exactamente las dependencias distribuidas, puedes extraerlo con `PavPDF-Setup.exe --extract CARPETA_NUEVA` y usar la subcarpeta `runtime`.

Las fuentes correspondientes se incluyen también dentro de la aplicación instalada. `src/test_engine.py` prueba integridad de páginas, membretes, redacción, compresión y límites de división. Los resultados de la ejecución local están en `PRUEBAS.json`.

## Licencias

PavPDF se distribuye bajo AGPL-3.0-or-later. PyMuPDF/MuPDF tiene licencia AGPL; Python y Pillow conservan sus licencias incluidas en el runtime. Consulta `LICENSE.txt` y los avisos del runtime. Fuentes de las dependencias: [CPython 3.12.10](https://github.com/python/cpython/tree/v3.12.10), [PyMuPDF 1.26.4](https://github.com/pymupdf/PyMuPDF/tree/1.26.4), [Pillow 11.3.0](https://github.com/python-pillow/Pillow/tree/11.3.0).
