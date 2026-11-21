# DigitalBrain-BCI_UI_Application

DigitalBrain procesa capturas de cuatro canales, extrae características espectrales y entrena clasificadores con Python. El repositorio contiene un backend funcional por línea de comandos y, en `FRONT`, una aplicación de escritorio en C# y WPF cuya pantalla de captura y entrenamiento ya funciona sobre esa CLI.

## Estado actual

| Parte | Estado |
| --- | --- |
| Captura | La CLI recibe la carpeta externa del capturador, ejecuta `ConDump.exe` desde allí y guarda su salida. El programa y sus archivos auxiliares no se incluyen. |
| Preparación de datos | La CLI convierte archivos `.txt` en CSV con 16 características por ventana y una etiqueta. |
| Entrenamiento | La CLI entrena Random Forest, LDA, SVM, Extra Trees o Gradient Boosting. |
| Inferencia | El comando `predict` lanza el capturador y clasifica en vivo cada ventana de 125 muestras, con una decisión nueva cada 63. Acepta modelos del proyecto y de `MachineLearning.py`. |
| Interfaz WPF | **Inicio** resume el estado real del sistema. **Capturar** graba por sujeto con la CLI `capture` y muestra los cuatro canales en vivo. **Entrenar** entrena con `build-dataset` + `train` y gestiona los modelos de cada sujeto. **Tiempo real** lista por nombre los modelos de todos los sujetos, ejecuta `predict` y muestra las últimas 20 predicciones con la confianza del modelo en cada una; el envío de comandos al robot UGOT sigue pendiente. **Osciloscopio** muestra en vivo (CLI `monitor`) o desde una grabación la forma de onda filtrada, los pulsos, el espectro FFT y la potencia por banda de cada canal. |

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
& $pythonDB -m digital_brain.cli build-dataset "datos/Datos originales/movimientos/Pierna izquierda" artifacts/datasets/left.csv pierna_izquierda
& $pythonDB -m digital_brain.cli build-dataset "datos/Datos originales/movimientos/Brazo derecho" artifacts/datasets/right.csv brazo_derecho
& $pythonDB -m digital_brain.cli train artifacts/models/model.pkl artifacts/datasets/left.csv artifacts/datasets/right.csv --algorithm random_forest
```

`train` acepta `random_forest`, `lda`, `svm`, `extra_trees` y `gradient_boosting`; si se omite `--algorithm`, usa `random_forest`. La exactitud impresa actualmente procede de una división aleatoria de ventanas y **no debe interpretarse como una evaluación independiente por sesión o sujeto**. Consulta [Datos y modelos](docs/datos-y-modelos.md) antes de comparar modelos.

Para adquirir datos, conserva **fuera del repositorio** la carpeta completa del programa, incluidos `ConDump.exe` y los archivos auxiliares que necesita. Desde `BACK/`, cambia el valor de `$capturador` por la ruta real de esa carpeta en tu equipo:

```powershell
$capturador = "C:\ruta\a\la\carpeta\del\capturador"
& $pythonDB -m digital_brain.cli capture $capturador datos_capturados --prefix sesion --seconds 30 --capture-from 6
```

Para clasificar en vivo con un modelo entrenado, detén el comando con `Ctrl+C` cuando termines:

```powershell
python -m digital_brain.cli predict $capturador datos\ModelMI_Arturo\modelos\ModelMI_Arturo_random_forest.pkl
```

`build-dataset` y `predict` aceptan `--sample-rate` (125 Hz por defecto; la ventana dura 1 s). `predict` busca `ConDumpAuto.exe` por defecto, lo relanza si termina y se detiene con un error si una ejecución no entrega ninguna muestra válida. `train` acepta también los CSV de `DatosACSVNew.py`, y `predict` los `.pkl` de `MachineLearning.py`.

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

Al abrirse, la aplicación crea o actualiza en el escritorio el acceso directo **DigitalBrain**, con el logo de `FRONT/Assets/DigitalBrain.ico`, que usa `Ejecutar-DigitalBrain.bat` con la consola minimizada; así recompila si el código cambió.

La aplicación localiza `BACK/` subiendo desde su carpeta de ejecución, así que debe lanzarse desde el repositorio.

### Organización de las capturas

Cada sujeto tiene una carpeta en `BACK/datos/` con sus grabaciones por movimiento y sus modelos:

```text
datos/ModelMI_Arturo/
├── movimientos/
│   ├── Brazo derecho/      # Brazo derecho 1b.txt, Brazo derecho 2b.txt…
│   └── Pierna izquierda/
└── modelos/
    ├── ModelMI_Arturo_random_forest.pkl
    └── ModelMI_Arturo_random_forest.json   # algoritmo, exactitud, fecha y movimientos
```

El nombre del sujeto lleva la abreviatura del **Tipo de entrenamiento** con que se creó: Motor Imagery guarda `ModelMI_Arturo` y Motor Movement `ModelM_Arturo`. Las carpetas se crean solas al añadir un sujeto o un movimiento desde la pantalla (y, si faltara, al grabar). No hay archivo de configuración aparte: las listas de la interfaz se leen de esas carpetas.

Las capturas originales del repositorio están en el sujeto `Datos originales`. Como no tiene prefijo, no se ofrece para grabar, pero sí para entrenar.

Al abrirse, la aplicación pasa a esta organización los datos de la anterior: `datos/<movimiento>/<sujeto>/*.txt` va a `datos/<sujeto>/movimientos/<movimiento>/`, las capturas sueltas de un movimiento van a `Datos originales`, y los modelos de `artifacts/models` con nombre automático `<sujeto>_<algoritmo>.pkl` van a la carpeta de su sujeto. Solo mueve: si un archivo ya existe en el destino lo deja donde estaba. Los modelos con otro nombre se quedan en `artifacts/models` y siguen apareciendo en Tiempo real.

- Un movimiento añadido, por ejemplo «Codo», usa `datos\<sujeto>\movimientos\Codo\`, aparece para todos los sujetos y usa la etiqueta `codo` (minúsculas, sin tildes, `_` en lugar de espacios) y archivos `Codo 1.txt`, `Codo 2.txt`…

### Pestaña Capturar

| Zona | Comportamiento |
| --- | --- |
| Configuración de sesión | El primer campo es **Sujeto**, después **Movimiento** y **Tipo de entrenamiento**. El sujeto se elige una vez y se mantiene al cambiar de movimiento. El botón **+** de cada lista abre un campo para escribir uno nuevo (Intro añade, Esc cancela); el tipo solo se elige al añadir un sujeto, porque define su prefijo, y después muestra el del sujeto elegido. Si el nombre ya existe aparece un aviso y no se crea nada; tampoco se aceptan nombres que no sirvan como carpeta. Un movimiento nuevo se crea en la carpeta del sujeto elegido. **Duración de la grabación** son los segundos que se guardan; los 5 s de estabilización se suman aparte (25 s → el capturador corre 30 s). La **carpeta de salida** es automática (`datos\<sujeto>\movimientos\<movimiento>\`) y el número de sesión propuesto es el siguiente libre en esa carpeta. La ruta del ejecutable se recuerda entre ejecuciones. |
| Parámetros avanzados | Solo lectura. Muestran los valores que usa el backend: 125 Hz, ventana de 125 muestras, avance de 63 muestras (media ventana redondeada hacia arriba), saturación ±800 µV y canales A–D. |
| CH-A … CH-D | Últimos 4 s de cada canal mientras se graba, con la amplitud pico (±µV) de lo visible. |
| Válidas CH-x | Porcentaje de muestras de ese canal que no están en ±800 µV dentro del tramo que se guarda. |
| Grabación | **Iniciar captura** ejecuta la captura; el mismo botón pasa a **Detener captura**. Durante los primeros 5 s el estado indica «Estabilizando» y el tiempo no avanza; después cuenta solo el tiempo que se guarda. Muestras válidas (líneas que `build-dataset` usará) y descartadas (alguna columna en ±800 µV). Al terminar, los contadores se recalculan sobre el archivo guardado. No se puede capturar mientras se entrena. |
| Últimas grabaciones | Las cuatro capturas más recientes de `BACK/datos`, con movimiento, sujeto, duración, muestras válidas y ventanas que generarán. «Ver las N →» abre la carpeta de datos. |

### Pestaña Entrenar

Arriba se elige el **sujeto**; debajo, una fila por movimiento con una casilla para incluirlo y las grabaciones que tiene ese sujeto. Al elegir otro sujeto se marcan todos sus movimientos con grabaciones. Hacen falta al menos dos movimientos marcados con grabaciones. La lista se vuelve a leer al entrar en la pestaña (conservando lo elegido) o con «Actualizar lista». La tarjeta Modelo muestra el algoritmo, el nombre del archivo, el estado y la exactitud del último entrenamiento. Debajo, **Modelos de <sujeto>** lista los modelos de su carpeta con su algoritmo, exactitud y fecha; «Eliminar» pide confirmación y borra el `.pkl` y su `.json`. No se puede entrenar mientras se captura.

El panel Estado indica si llega señal del casco durante la grabación, el modelo más reciente de todos los sujetos y el total de grabaciones.

### Pantalla Osciloscopio

Sirve para comprobar los electrodos antes de grabar, como el «Signal Tester» del fabricante. No guarda nada y sus filtros solo afectan a lo que se ve: las grabaciones y el modelo siguen usando la señal original.

| Zona | Comportamiento |
| --- | --- |
| Fuente | **Casco en vivo** ejecuta `python -u -m digital_brain.cli monitor <carpeta del exe> --executable-name ConDumpAuto.exe`, que reenvía cada muestra `a,b,c,d` (también las saturadas) hasta recibir `stop` por stdin. **Grabación** reproduce un `.txt` a la frecuencia de muestreo de Inicio, en bucle. El casco solo puede usarlo un proceso: mientras el osciloscopio está en vivo no se puede capturar ni predecir, y al revés. |
| Forma de onda | Una franja por canal con su línea base y escala ±2 divisiones. **Barrido**: la traza avanza y reescribe, como un osciloscopio analógico. **Rodante**: la señal se desplaza a la izquierda. **Disparo**: la pantalla se congela en cada pulso detectado, con un 25 % de pre-disparo (marca «T»). Base de tiempo de 0,1 a 2 s/div; amplitud fija o automática (percentil 99 de lo visible). Las franjas rojas marcan muestras en ±800 µV y los triángulos, pulsos. **Congelar** detiene la pantalla sin dejar de recibir. |
| Filtros | Pasa altos Butterworth de 4.º orden (0,5, 1 o 5 Hz), notch de red (50 o 60 Hz, Q = 25) y pasa bajos Butterworth de 4.º orden (30 o 45 Hz). Durante la saturación se mantiene la última muestra válida para que los filtros no oscilen. Al cambiar un filtro se vuelve a filtrar la historia (30 s). |
| Pulsos | Una muestra filtrada que se aleja de la mediana más de k·σ (k = 4, 6 o 10), con σ = 1,4826·MAD de los últimos 2 s: el umbral se adapta al ruido de cada electrodo y un pulso grande no lo sube. Periodo refractario de 0,3 s. |
| Espectro | Estimador de Welch sobre los últimos 4 s de señal filtrada: segmentos de 256 muestras (≈ 2 s, resolución 0,49 Hz a 125 Hz), sin media, ventana de Hann y 50 % de solape. Se dibuja en mariposa (A/B y C/D) hasta 60 Hz, coloreado por banda, con la frecuencia dominante (máximo entre 1 y 45 Hz con interpolación parabólica). |
| Medidas por canal | RMS y pico a pico de los últimos 2 s, frecuencia y banda dominantes, potencia relativa por banda (delta 0,5–4, theta 4–8, alfa 8–13, beta 13–30 y gamma ≥ 30 Hz; las cuatro últimas son las del modelo), amplitud de la red eléctrica en la señal **sin** filtrar (indica mal contacto), pulsos y porcentaje de saturación. |

El procesado está en `FRONT/Models/Dsp/` (`Biquad`, `Fft`, `WelchSpectrum`, `SignalConditioner`, `PulseDetector`, `SampleHistory`), sin dependencias de WPF; los gráficos son controles propios en `FRONT/Controls/` (`OscilloscopeDisplay`, `SpectrumButterfly`, `BandPowerBar`).

**Inicio** se calcula con lo que hay en disco cada vez que se abre: canales, frecuencia de muestreo (editable; se usa al crear datasets y al predecir, y un modelo debe usarse con la frecuencia con que se entrenó), características por ventana, si existe el capturador o si se está grabando o prediciendo, el modelo más reciente y el estado de cada paso. Un paso está «Hecho» si ya hay grabaciones o modelos, «Siguiente paso» si es el que toca y «En curso» mientras se ejecuta.

### Cómo se conecta con el backend

**Captura.** La pantalla no habla con el dispositivo directamente: ejecuta el comando ya existente

```powershell
python -u -m digital_brain.cli capture <carpeta del exe> <carpeta de salida> --executable-name ConDumpAuto.exe --prefix .captura_ui_<id> --seconds <5 + duración> --capture-from 5
```

con `BACK/` como directorio de trabajo. La CLI reenvía por stdout lo que imprime el capturador; cada línea `a,b,c,d` alimenta las gráficas y los contadores. Cuando la CLI termina e imprime `Datos guardados en: …`, el archivo `prefijo_1.txt` se renombra al nombre elegido en la pantalla. Notas:

- «Duración de la grabación» es el tiempo guardado. La CLI recibe `--seconds <5 + duración>` y `--capture-from 5`, así que los 5 s iniciales que no se guardan van aparte (el valor por defecto de la CLI sigue siendo 6 s; la interfaz siempre pasa 5).
- La CLI guarda la salida **por líneas completas**: solo escribe las líneas que empezaron a llegar después de `--capture-from`, terminaron antes de `--seconds` y tienen exactamente cuatro números separados por comas. Las cabeceras de ConDump (`Channel`, `Min`, `Max`), las líneas vacías y las cortadas se descartan, y al terminar imprime cuántas líneas guardó y cuántas descartó. Las capturas grabadas antes de este cambio pueden tener una primera o última línea incompleta.
- La CLI no tiene orden de parada. **Detener captura** termina el proceso de Python junto con el capturador y borra el archivo temporal: una captura detenida no deja archivo.
- No se sobrescriben capturas: si el nombre ya existe, la pantalla pide otro número de sesión.
- Los metadatos que la CLI no guarda (sujeto, notas, duración configurada, muestras) se añaden como una línea JSON a `BACK/datos_capturados/sesiones.jsonl`, carpeta ignorada por Git, para no mezclar archivos extra con los `.txt` de entrenamiento.

**Entrenamiento.** **Entrenar modelo** ejecuta `build-dataset` una vez por movimiento marcado, con la carpeta de ese movimiento del sujeto y la frecuencia de muestreo de Inicio (`--sample-rate`). Cada uno genera `BACK/artifacts/datasets/<sujeto>__<etiqueta>.csv`. Después ejecuta `train` con el algoritmo elegido sobre esos CSV, guarda el modelo en `datos/<sujeto>/modelos/` y escribe a su lado un `.json` con el algoritmo, la exactitud, la fecha y los movimientos. El nombre es automático, `<sujeto>_<algoritmo>.pkl` (el sujeto ya lleva su prefijo), y el botón **Editar** permite cambiarlo; si el archivo ya existe se avisa antes de reemplazarlo. Se necesitan al menos dos movimientos. Antes de llamar a Python se comprueba que cada movimiento elegido dé ventanas (cada archivo necesita al menos 125 muestras válidas, 1 s sin saturar); si no, se indica qué movimiento y sujeto fallan. Si `build-dataset` o `train` fallan igualmente, se muestra la última línea de error de Python. La exactitud mostrada es la que imprime `train`, sobre una división aleatoria de ventanas; no es una evaluación por sesión ni por sujeto.

### Arquitectura del código creado

El proyecto sigue capas de arquitectura limpia: las dependencias apuntan hacia el dominio y los casos de uso solo conocen interfaces (puertos).

```text
FRONT/
├── Models/                       # Dominio: sin WPF ni procesos
│   ├── AcquisitionParameters.cs  # 125 Hz, ventana 125, avance 63, ±800 µV, canales; cálculo de ventanas
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


