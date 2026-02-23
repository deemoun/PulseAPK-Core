# PulseAPK

**PulseAPK** es una GUI de nivel profesional para ingeniería inversa de Android y análisis de seguridad, creada con Avalonia (.NET 8). Combina el poder de `apktool` con capacidades avanzadas de análisis estático, envueltas en una interfaz de alto rendimiento con estética cyberpunk. PulseAPK optimiza todo el flujo de trabajo desde la decompilación hasta el análisis, la recompilación y la firma.

[Ver la demo en YouTube](https://youtu.be/Mkdt0c-7Wwg)

![PulseAPK UI](images/pulse_apk_decompile.png)

Use the Analysis tab to select the decompiled project folder and run Smali analysis.

![PulseAPK Smali Analysis](images/apktool_analysis.png)

Si quieres compilar (y firmar si es necesario) la carpeta Smali, usa la sección "Build APK".

![PulseAPK Build APK](images/pulse_apk_build.png)

## Funcionalidades clave

- **🛡️ Análisis de seguridad estático**: Escanea automáticamente el código Smali en busca de vulnerabilidades, incluida la detección de root, comprobaciones de emulador, credenciales codificadas y uso inseguro de SQL/HTTP.
- **⚙️ Motor de reglas dinámico**: Reglas de análisis totalmente personalizables a través de `smali_analysis_rules.json`. Modifica los patrones de detección al vuelo sin reiniciar la aplicación. Usa caché para un rendimiento óptimo.
- **🚀 Modern UI/UX**: A responsive, dark-themed interface designed for efficiency, with real-time console feedback.
- **📦 Flujo de trabajo completo**: Decompila, analiza, edita, recompila y firma APKs en un único entorno.
- **⚡ Seguro y robusto**: Incluye validación inteligente y prevención de fallos para proteger tu espacio de trabajo y tus datos.
- **🔧 Totalmente configurable**: Gestiona rutas de herramientas (Java, Apktool), ajustes del espacio de trabajo y parámetros de análisis con facilidad.

## Capacidades avanzadas

### Análisis de seguridad
PulseAPK incluye un analizador estático integrado que escanea el código decompilado en busca de indicadores de seguridad comunes:
- **Detección de root**: Identifica comprobaciones de Magisk, SuperSU y binarios de root comunes.
- **Detección de emulador**: Encuentra comprobaciones de QEMU, Genymotion y propiedades específicas del sistema.
- **Datos sensibles**: Escanea claves API, tokens y encabezados basic auth codificados.
- **Red insegura**: Marca el uso de HTTP y posibles puntos de fuga de datos.

*Las reglas se definen en `smali_analysis_rules.json` y se pueden personalizar según tus necesidades.*

### Gestión de APK
- **Decompilación**: Decodifica recursos y fuentes sin esfuerzo con opciones configurables.
- **Recompilación**: Reconstruye tus proyectos modificados en APKs válidos.
- **Firma**: Gestión integrada de keystore para firmar APKs recompilados y dejarlos listos para instalar en dispositivos.

## Requisitos previos

1.  **Java Runtime Environment (JRE)**: Necesario para `apktool`. Asegúrate de que `java` esté en tu `PATH`.
2.  **Apktool**: Descarga `apktool.jar` desde [ibotpeaches.github.io](https://ibotpeaches.github.io/Apktool/).
3.  **Ubersign (Uber APK Signer)**: Necesario para firmar APKs recompilados. Descarga la última versión de `uber-apk-signer.jar` desde las [releases de GitHub](https://github.com/patrickfav/uber-apk-signer/releases).
4.  **.NET 8.0 Runtime**: Necesario para ejecutar PulseAPK en Windows.

## Guía de inicio rápido

1.  **Descargar y compilar**
    ```powershell
    dotnet build
    dotnet run
    ```

2.  **Configuración**
    - Abre **Settings**.
    - Indica la ruta de `apktool.jar`.
    - PulseAPK detectará automáticamente tu instalación de Java según las variables de entorno.

3.  **Analizar un APK**
    - **Decompila** tu APK objetivo en la pestaña Decompile.
    - Cambia a la pestaña **Analysis**.
    - Selecciona la carpeta del proyecto decompilado.
    - Haz clic en **Analyze Smali** para generar un informe de seguridad.

4.  **Modificar y recompilar**
    - Edita los archivos en la carpeta del proyecto.
    - Usa la pestaña **Build** para recompilar en un nuevo APK.
    - Usa la pestaña **Sign** para firmar el APK resultante.

## Arquitectura técnica

PulseAPK utiliza una arquitectura MVVM (Model-View-ViewModel) limpia:

- **Core**: .NET 8.0, Avalonia.
- **Analysis**: Motor de análisis estático personalizado basado en regex con reglas recargables en caliente.
- **Services**: servicios dedicados para la interacción con Apktool, el monitoreo del sistema de archivos y la gestión de configuraciones.

## Licencia

Este proyecto es de código abierto y está disponible bajo la [Apache License 2.0](LICENSE.md).

### ❤️ Apoya el proyecto

Si PulseAPK te resulta útil, puedes apoyar su desarrollo pulsando el botón "Support" en la parte superior.

Dar una estrella al repositorio también ayuda mucho.

### Contribuciones

¡Damos la bienvenida a las contribuciones! Ten en cuenta que todos los colaboradores deben firmar nuestro [Contributor License Agreement (CLA)](CLA.md) para que su trabajo pueda distribuirse legalmente.
Al enviar un pull request, aceptas los términos del CLA.
