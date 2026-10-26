# Fuentes de dependencias AGPL

Los archivos originales sin modificar acompañan al código de PavPDF para la redistribución de los componentes AGPL del instalador:

- `pymupdf-1.26.4.tar.gz`: distribución fuente oficial de PyPI, incluye instrucciones y scripts de compilación.
- `mupdf-1.26.7-source.tar.gz`: fuente oficial de MuPDF correspondiente a la biblioteca incluida en el runtime, con sus componentes de terceros.
- `SHA256SUMS.txt`: sumas calculadas de los archivos descargados.

PyMuPDF: https://pypi.org/project/PyMuPDF/1.26.4/#files

MuPDF: https://mupdf.com/downloads/archive/mupdf-1.26.7-source.tar.gz

Para reconstruir la biblioteca sigue el `setup.py` de PyMuPDF y las instrucciones incluidas en los archivos fuente. Para reconstruir PavPDF usa `build.ps1` del directorio principal. El programa no modifica el código de estas bibliotecas.

Publica `PavPDF-Fuentes.zip` junto al instalador en la misma Release de GitHub. Este archivo incluye estas fuentes. El runtime conserva también los avisos de licencia de Python 3.12.10 y Pillow 11.3.0, distribuidos bajo sus licencias permisivas.
