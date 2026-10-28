# Ensayos de interfaz

Compila cada ensayo junto con `PavPDF.cs`, eligiendo `/main:NOMBRE` y las mismas referencias WinForms y WPF de `build.ps1`. Pon el ejecutable de ensayo en la carpeta `package` generada, junto al motor y al runtime. Los ensayos escriben PASS o FAIL y cierran su propia ventana. No prueban una instalación dentro del perfil del usuario.

- `SpellTest`: argumento 1, ruta del resultado. Comprueba «disposicion 15» → «disposición 15» en una sesión normal de Windows con español instalado.
- `SmokeTest`: argumento 1, PDF de 14 páginas con cuatro páginas blancas (2, 6, 12, 14); argumento 2, resultado. Opcionales: PDF generado de Arial 12 y captura de la ventana. Es un caso de regresión de un documento concreto, no un validador de cualquier PDF.
- `EditTest`: PDF de 19 páginas, salida PDF y resultado. Prueba el caso RURAL → CUSCO.
- `GapUiTest`: PDF de 13 páginas, salida PDF y resultado. Prueba el espaciado en la página 13.
- `ToneUiTest`: PDF de cuatro páginas, salida PDF y resultado. Prueba tinta en la página 2.
- `HeightUiTest`: PDF de cinco páginas, salida PDF y resultado. Prueba altura, zoom, historial y reemplazo en la página 1.

Los documentos particulares usados en estos ensayos no se incluyen en el código fuente. Las pruebas Python generan sus propios documentos y se pueden ejecutar sin ellos.
