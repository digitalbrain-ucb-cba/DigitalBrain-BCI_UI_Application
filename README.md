# DigitalBrain-BCI_UI_Application

DigitalBrain procesa capturas de cuatro canales, extrae características espectrales y entrena clasificadores con Python. El repositorio contiene un backend funcional por línea de comandos y, en `FRONT`, una aplicación de escritorio en C# y WPF cuya pantalla de captura y entrenamiento ya funciona sobre esa CLI.

## Estado actual

| Parte | Estado |
| --- | --- |
| Captura | La CLI recibe la carpeta externa del capturador, ejecuta `ConDump.exe` desde allí y guarda su salida. El programa y sus archivos auxiliares no se incluyen. |
| Preparación de datos | La CLI convierte archivos `.txt` en CSV con 16 características por ventana y una etiqueta. |
| Entrenamiento | La CLI entrena Random Forest, LDA, SVM, Extra Trees o Gradient Boosting. |
| Inferencia | Existe `RealtimeClassifier` para uso desde Python, pero no hay comando `predict` ni conexión directa con la captura. |
| Interfaz WPF | Pantalla **Captura y entrenamiento** funcional: graba con la CLI `capture`, muestra los cuatro canales en vivo y entrena con `build-dataset` + `train`. El resto de pantallas de la barra lateral están pendientes. |

## Estructura

```text
DigitalBrain/
├── BACK/
│   ├── datos/               # capturas originales
│   ├── src/digital_brain/   # dominio, casos de uso, infraestructura y CLI
│   ├── tests/
│   └── pyproject.toml
├── FRONT/                   # aplicación de escritorio C# / WPF (.NET 10)
└── docs/                    # documentación vigente
```

## Preparar el backend

Se requiere Python 3.10 o posterior. Desde PowerShell, en la raíz del repositorio:

```powershell
cd BACK
python -m venv "$env:LOCALAPPDATA\DigitalBrain\venv"
$pythonDB = "$env:LOCALAPPDATA\DigitalBrain\venv\Scripts\python.exe"
& $pythonDB -m pip install -e .
& $pythonDB -m unittest discover -s tests -v
```

El entorno virtual se crea fuera del repositorio. `pip install -e .` instala el paquete y las dependencias declaradas en `BACK/pyproject.toml`.

## Flujo básico

Los siguientes comandos se ejecutan desde `BACK/`, con el entorno virtual preparado. Las etiquetas de ejemplo describen los movimientos presentes en esas carpetas; el código no comprueba que una etiqueta corresponda al protocolo de captura.

```powershell
& $pythonDB -m digital_brain.cli build-dataset datos/left artifacts/datasets/left.csv pierna_izquierda
& $pythonDB -m digital_brain.cli build-dataset datos/Right artifacts/datasets/right.csv brazo_derecho
& $pythonDB -m digital_brain.cli train artifacts/models/model.pkl artifacts/datasets/left.csv artifacts/datasets/right.csv --algorithm random_forest
```

`train` acepta `random_forest`, `lda`, `svm`, `extra_trees` y `gradient_boosting`; si se omite `--algorithm`, usa `random_forest`. La exactitud impresa actualmente procede de una división aleatoria de ventanas y **no debe interpretarse como una evaluación independiente por sesión o sujeto**. Consulta [Datos y modelos](docs/datos-y-modelos.md) antes de comparar modelos.

Para adquirir datos, conserva **fuera del repositorio** la carpeta completa del programa, incluidos `ConDump.exe` y los archivos auxiliares que necesita. Desde `BACK/`, cambia el valor de `$capturador` por la ruta real de esa carpeta en tu equipo:

```powershell
$capturador = "C:\ruta\a\la\carpeta\del\capturador"
& $pythonDB -m digital_brain.cli capture $capturador datos_capturados --prefix sesion --seconds 30 --capture-from 6
```

El primer argumento de `capture` es **la carpeta**, no el archivo `.exe`. Por defecto busca `ConDump.exe` en ella. Si el ejecutable tiene otro nombre, añade `--executable-name ConDumpAuto.exe` o el nombre real. El proceso se inicia con esa carpeta como directorio de trabajo para que pueda encontrar sus dependencias. La ruta se pasa al ejecutar el comando; no se guarda en el código ni en Git.

El archivo capturado queda en `datos_capturados/`, una carpeta ignorada por Git; no se incorpora automáticamente a un dataset. Para ver los parámetros disponibles, ejecuta `& $pythonDB -m digital_brain.cli capture --help`.

## Interfaz WPF: pantalla «Captura y entrenamiento»

Es la primera pantalla de la aplicación de escritorio (`FRONT/`, C# + WPF sobre .NET 10, MVVM con `CommunityToolkit.Mvvm`). Cubre los pasos 1 y 2 del flujo en dos pestañas de la barra lateral: **Capturar** (grabar sesiones con `ConDumpAuto.exe`) y **Entrenar** (entrenar el modelo con ellas). Reproduce el diseño del prototipo, con tema claro y oscuro.

### Ejecutarla

Requiere el SDK de .NET 10 y el backend preparado como se indica arriba (la interfaz usa `%LOCALAPPDATA%\DigitalBrain\venv\Scripts\python.exe` si existe; si no, `python` del PATH).

```powershell
cd FRONT
dotnet run --project DigitalBrain.UI.csproj
```

La aplicación localiza `BACK/` subiendo desde su carpeta de ejecución, así que debe lanzarse desde el repositorio.

### Organización de las capturas

Cada movimiento es una carpeta de `BACK/datos/` y cada sujeto una subcarpeta suya: `datos/<movimiento>/<sujeto>/*.txt`. Las carpetas se crean solas al añadir un movimiento o un sujeto desde la pantalla (y, si faltara, al grabar). No hay archivo de configuración aparte: las listas de la interfaz se leen de esas carpetas.

- Los tres movimientos predefinidos conservan sus carpetas (`datos\Right\`, `datos\left\`, `datos\datos_hombro_derecho\`) y sus nombres de archivo (`Brazo derecho 26b.txt`, `Pierna izquierda 27.txt`, `Hombro derecho_11.txt`).
- Un movimiento añadido, por ejemplo «Codo», usa `datos\Codo\`, la etiqueta `codo` (minúsculas, sin tildes, `_` en lugar de espacios) y archivos `Codo 1.txt`, `Codo 2.txt`…
- Las capturas anteriores que están sueltas en la carpeta del movimiento no tienen sujeto; siguen contando para el entrenamiento con «Todos los sujetos».

### Pestaña Capturar

| Zona | Comportamiento |
| --- | --- |
| Configuración de sesión | **Movimiento** y **Sujeto** son listas; el sujeto muestra solo los que tiene el movimiento elegido (si también existe en el nuevo movimiento, se mantiene al cambiarlo). El botón **+** de cada lista abre un campo para escribir uno nuevo (Intro añade, Esc cancela). Si el nombre ya existe en ese movimiento aparece un aviso y no se crea nada; tampoco se aceptan nombres que no sirvan como carpeta. Al añadir un movimiento se pide directamente su primer sujeto. **Duración de la grabación** son los segundos que se guardan; los 5 s de estabilización se suman aparte (25 s → el capturador corre 30 s). La **carpeta de salida** es automática (`datos\<movimiento>\<sujeto>\`) y el número de sesión propuesto es el siguiente libre en esa carpeta. La ruta del ejecutable se recuerda entre ejecuciones. |
| Parámetros avanzados | Solo lectura. Muestran los valores que usa el backend: 125 Hz, ventana de 125 muestras, avance de 62 muestras (`window_size // 2`), saturación ±800 µV y canales A–D. |
| CH-A … CH-D | Últimos 4 s de cada canal mientras se graba, con la amplitud pico (±µV) de lo visible. |
| Válidas CH-x | Porcentaje de muestras de ese canal que no están en ±800 µV dentro del tramo que se guarda. |
| Grabación | **Iniciar captura** ejecuta la captura; el mismo botón pasa a **Detener captura**. Durante los primeros 5 s el estado indica «Estabilizando» y el tiempo no avanza; después cuenta solo el tiempo que se guarda. Muestras válidas (líneas que `build-dataset` usará) y descartadas (alguna columna en ±800 µV). Al terminar, los contadores se recalculan sobre el archivo guardado. No se puede capturar mientras se entrena. |
| Últimas grabaciones | Las cuatro capturas más recientes de `BACK/datos`, con movimiento, sujeto, duración, muestras válidas y ventanas que generarán. «Ver las N →» abre la carpeta de datos. |

### Pestaña Entrenar

Una fila por movimiento: casilla para incluirlo en el modelo, lista de sujetos («Todos los sujetos» o uno concreto) y número de grabaciones que se usarán. Hacen falta al menos dos movimientos marcados con grabaciones. La lista se vuelve a leer al entrar en la pestaña (conservando lo elegido) o con «Actualizar lista». La tarjeta Modelo muestra el estado, la exactitud del último entrenamiento, el algoritmo y el archivo. No se puede entrenar mientras se captura.

La barra lateral tiene implementadas «Capturar» y «Entrenar»; las demás entradas están deshabilitadas. El panel Estado indica si llega señal del casco durante la grabación, si existe `ModelMotores.pkl` y el total de grabaciones.

### Cómo se conecta con el backend

**Captura.** La pantalla no habla con el dispositivo directamente: ejecuta el comando ya existente

```powershell
python -u -m digital_brain.cli capture <carpeta del exe> <carpeta de salida> --executable-name ConDumpAuto.exe --prefix .captura_ui_<id> --seconds <5 + duración> --capture-from 5
```

con `BACK/` como directorio de trabajo. La CLI reenvía por stdout lo que imprime el capturador; cada línea `a,b,c,d` alimenta las gráficas y los contadores. Cuando la CLI termina e imprime `Datos guardados en: …`, el archivo `prefijo_1.txt` se renombra al nombre elegido en la pantalla. Notas:

- «Duración de la grabación» es el tiempo guardado. La CLI recibe `--seconds <5 + duración>` y `--capture-from 5`, así que los 5 s iniciales que no se guardan van aparte (el valor por defecto de la CLI sigue siendo 6 s; la interfaz siempre pasa 5).
- La CLI no tiene orden de parada. **Detener captura** termina el proceso de Python junto con el capturador y borra el archivo temporal: una captura detenida no deja archivo.
- No se sobrescriben capturas: si el nombre ya existe, la pantalla pide otro número de sesión.
- Los metadatos que la CLI no guarda (sujeto, notas, duración configurada, muestras) se añaden como una línea JSON a `BACK/datos_capturados/sesiones.jsonl`, carpeta ignorada por Git, para no mezclar archivos extra con los `.txt` de entrenamiento.

**Entrenamiento.** **Entrenar modelo** ejecuta `build-dataset` una vez por carpeta con capturas de lo elegido: la del sujeto concreto o, con «Todos los sujetos», la de cada sujeto más la del movimiento si tiene capturas sueltas. Cada una genera `BACK/artifacts/datasets/<etiqueta>__<sujeto>.csv` (`<etiqueta>__anteriores.csv` para las sueltas) con la etiqueta del movimiento. Después ejecuta `train` con `random_forest` sobre todos esos CSV y guarda `BACK/artifacts/models/ModelMotores.pkl`. Se necesitan al menos dos movimientos. Antes de llamar a Python se comprueba que cada movimiento elegido dé ventanas (cada archivo necesita al menos 125 muestras válidas, 1 s sin saturar); si no, se indica qué movimiento y sujeto fallan. Si `build-dataset` o `train` fallan igualmente, se muestra la última línea de error de Python. La exactitud mostrada es la que imprime `train`, sobre una división aleatoria de ventanas; no es una evaluación por sesión ni por sujeto.

### Arquitectura del código creado

El proyecto sigue capas de arquitectura limpia: las dependencias apuntan hacia el dominio y los casos de uso solo conocen interfaces (puertos).

```text
FRONT/
├── Models/                       # Dominio: sin WPF ni procesos
│   ├── AcquisitionParameters.cs  # 125 Hz, ventana 125, avance 62, ±800 µV, canales; cálculo de ventanas
│   ├── SignalValidity.cs         # Lectura de una línea y regla de saturación (equivalente a parse_sample)
│   ├── MovementClass.cs          # Clase/movimiento: etiqueta, carpeta, patrón de nombre; predefinidos y añadidos
│   ├── CatalogNames.cs           # Reglas de nombres de movimiento/sujeto (sirven de carpeta)
│   ├── RecordingPlan.cs          # Datos de una grabación y sus validaciones
│   ├── CaptureStatistics.cs      # Contadores válidas/descartadas, total y por canal (seguro entre hilos)
│   └── RecordingSummary.cs       # Resumen de un archivo guardado
├── UseCases/                     # Casos de uso
│   ├── Ports/                    # ICaptureGateway, IRecordingRepository, IDataCatalog, ISessionLog,
│   │                             # IBackendEnvironment, ISettingsStore
│   ├── RecordSessionUseCase.cs   # Valida, captura, analiza el archivo y registra metadatos
│   └── TrainModelUseCase.cs      # build-dataset por movimiento/sujeto elegido + train
├── Services/                     # Adaptadores (infraestructura)
│   ├── Backend/BackendEnvironment.cs        # Localiza BACK/ y el intérprete de Python
│   ├── Backend/PythonCliCaptureGateway.cs   # Ejecuta `digital_brain.cli capture` y lee su salida
│   ├── Backend/PythonCliBridge.cs           # (existente) build-dataset y train
│   ├── Storage/FileSystemDataCatalog.cs     # Movimientos y sujetos = carpetas de BACK/datos
│   ├── Storage/FileSystemRecordingRepository.cs  # Lista y analiza capturas de BACK/datos
│   ├── Storage/JsonlSessionLog.cs           # BACK/datos_capturados/sesiones.jsonl
│   ├── Storage/JsonSettingsStore.cs         # %LOCALAPPDATA%\DigitalBrain\ui-settings.json
│   ├── Dialogs/WpfDialogService.cs          # Selección de exe y carpeta
│   └── Theme/ThemeService.cs                # Cambio de tema claro/oscuro
├── ViewModels/                   # Presentación
│   ├── MainViewModel.cs          # Barra lateral, tema y vista activa
│   ├── CaptureViewModel.cs       # Pestaña Capturar
│   ├── TrainingViewModel.cs, TrainingMovementViewModel.cs  # Pestaña Entrenar
│   ├── ChannelViewModel.cs, RecordingItemViewModel.cs, SystemStatusViewModel.cs
├── Views/                        # MainWindow.xaml (contenedor), CaptureView.xaml y TrainingView.xaml
├── Controls/SignalTrace.cs       # Traza ligera de un canal (sin ejes, como el diseño)
├── Converters/                   # InverseBooleanConverter, InverseBooleanToVisibilityConverter
└── Styles/                       # Colors.xaml (fuentes, iconos), ControlStyles.xaml,
                                  # Themes/Light.xaml y Themes/Dark.xaml (mismas claves)
```

Flujo de una grabación: `CaptureView` → `CaptureViewModel.ToggleCaptureCommand` → `RecordSessionUseCase` → `ICaptureGateway` (`PythonCliCaptureGateway` → CLI de Python → `ConDumpAuto.exe`). Las muestras llegan en un hilo de fondo al `SignalRingBuffer` y a `CaptureStatistics`; un temporizador de interfaz a ~30 FPS refresca gráficas y contadores. Todo se registra en el contenedor de dependencias de `App.xaml.cs`, y `App.xaml` asocia `CaptureViewModel` y `TrainingViewModel` con sus vistas mediante `DataTemplate`.

Para añadir otra pantalla: crear su ViewModel (heredando de `BaseViewModel`) y su vista, registrar el ViewModel en `App.xaml.cs`, añadir el `DataTemplate` en `App.xaml` y un comando de navegación en `MainViewModel`.

`ConDumpCaptureService` (con modo simulación) sigue registrado pero esta pantalla no lo usa: la captura pasa por la CLI del backend para que el archivo guardado sea exactamente el que produce `capture`.

## Documentación

- [Documentación general y arquitectura de la solución](documentacion.md)
- [Arquitectura y flujo del backend](docs/arquitectura.md)
- [Datos, ventanas, características y modelos](docs/datos-y-modelos.md)
- [Estado y diseño de la integración WPF](docs/integracion-wpf.md)

Las capturas `.txt` de `BACK/datos/` son datos originales, no documentación. Los modelos `.pkl` son artefactos de Python y deben cargarse únicamente desde fuentes confiables.


