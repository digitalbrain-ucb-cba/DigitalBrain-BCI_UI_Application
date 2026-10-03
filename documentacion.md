# Documentación del Proyecto DigitalBrain

> **Repositorio:** `DigitalBrain-BCI_UI_Application`  
> **Rama de desarrollo Frontend:** `feature/frontend`  
> **Fecha de actualización:** Octubre 2026  
> **Stack tecnológico:** Python 3.10+ (Backend / Machine Learning) y C# .NET 10 WPF (Frontend / Desktop UI)

---

## 1. Visión General del Proyecto

**DigitalBrain** es una plataforma biomédica diseñada para la adquisición, visualización en tiempo real, preprocesamiento espectral y clasificación de señales fisiológicas (EMG/EEG) de 4 canales muestreadas a 125 Hz.

El sistema se divide en dos componentes principales desacoplados:
1. **`BACK/` (Backend en Python):** Casos de uso de procesamiento digital de señales (FFT en bandas de frecuencia), preparación de datasets etiquetados y entrenamiento de modelos de Machine Learning (Random Forest, LDA, SVM, Extra Trees, Gradient Boosting).
2. **`FRONT/` (Frontend en C# / WPF):** Aplicación de escritorio moderna bajo el patrón arquitectónico **MVVM (Model-View-ViewModel)**, orientada al monitoreo en tiempo real, gestión de sesiones de captura y control visual del flujo de trabajo del usuario.

---

## 2. Arquitectura General del Sistema

```mermaid
flowchart TB
    subgraph FRONT["Frontend (C# / .NET 10 WPF - MVVM)"]
        UI["Vistas XAML (Views / Controls)"]
        VM["ViewModels (CommunityToolkit.Mvvm)"]
        SRB["SignalRingBuffer (Búfer circular 125 Hz)"]
        CS["ConDumpCaptureService (ICaptureService)"]
        PB["PythonCliBridge (IBackendBridge)"]
        NS["NavigationService (INavigationService)"]

        UI <--> VM
        VM --> NS
        VM --> SRB
        VM --> CS
        VM --> PB
    end

    subgraph HARDWARE["Adquisición Externa"]
        EXE["ConDump.exe / ConDumpAuto.exe"]
        SIM["Simulador sintético 125 Hz (Fallback)"]
    end

    subgraph BACK["Backend (Python / digital_brain)"]
        CLI["digital_brain.cli"]
        DS["application/dataset.py"]
        TR["application/training.py"]
        RT["application/realtime.py"]
        SF["domain/signal_features.py (FFT 16 feat)"]

        CLI --> DS
        CLI --> TR
        CLI --> RT
        DS --> SF
        RT --> SF
    end

    CS -->|Spawn & Process I/O| EXE
    CS -.->|Fallback si no existe .exe| SIM
    PB -->|Process Execution (stdout/stderr)| CLI
    EXE -->|Archivos .txt| RAW[("datos_capturados/")]
    CLI -->|Genera CSV| CSV[("datasets/*.csv")]
    CLI -->|Serializa .pkl| PKL[("models/*.pkl")]
```

---

## 3. Arquitectura del Frontend (`FRONT/`)

El frontend está diseñado siguiendo una **arquitectura base limpia**, sin código visual rígido ni pantallas prefabricadas, permitiendo construir la interfaz paso a paso según el prototipo de diseño.

### 3.1 Estructura de Directorios

```text
FRONT/
├── DigitalBrain.UI.csproj            # Configuración del proyecto .NET 10 WPF y NuGet
├── App.xaml                          # Recursos compartidos y estilos
├── App.xaml.cs                       # Contenedor de Inyección de Dependencias (ServiceCollection)
├── AssemblyInfo.cs                   # Metadatos del ensamblado WPF
│
├── Controls/                         # Controles visuales personalizados (osciloscopio, widgets)
│   └── .gitkeep
│
├── Models/                           # Modelos de dominio inmutables (records)
│   ├── SignalSample.cs               # Muestra de 4 canales a 125 Hz con validación de saturación
│   ├── SessionMetadata.cs            # Parámetros y metadatos de sesión experimental
│   └── TrainingResult.cs             # Resultado del entrenamiento de un clasificador
│
├── Services/                         # Servicios transversales y contratos desacoplados
│   ├── Hardware/
│   │   ├── ICaptureService.cs        # Contrato de captura de datos
│   │   └── ConDumpCaptureService.cs  # Manejo seguro del subproceso capturador + modo simulación
│   ├── Signal/
│   │   └── SignalRingBuffer.cs       # Búfer circular concurrente en memoria para 125 Hz
│   ├── Backend/
│   │   ├── IBackendBridge.cs         # Contrato de integración con la CLI de Python
│   │   └── PythonCliBridge.cs        # Ejecución asíncrona de digital_brain.cli
│   └── Navigation/
│       ├── INavigationService.cs     # Contrato de navegación entre pantallas
│       └── NavigationService.cs      # Gestión de la vista activa en MainWindow
│
├── Styles/                           # Diccionarios de recursos XAML
│   ├── Colors.xaml                   # Paleta temática oscura biomédica y colores por canal
│   └── ControlStyles.xaml            # Diccionario base para estilos de botones, campos y tarjetas
│
├── ViewModels/                       # Capa de presentación (MVVM)
│   ├── BaseViewModel.cs              # Base reactiva (ObservableObject)
│   └── MainViewModel.cs              # ViewModel principal que aloja la vista activa
│
└── Views/                            # Vistas y lienzos XAML
    ├── MainWindow.xaml               # Ventana principal con ContentControl dinámico
    └── MainWindow.xaml.cs            # DataContext inyectado por DI
```

### 3.2 Paquetes NuGet Utilizados

| Paquete | Versión | Propósito |
| --- | --- | --- |
| `CommunityToolkit.Mvvm` | 8.4.2 | Implementación MVVM estándar de Microsoft con generadores de código fuente (`[ObservableProperty]`, `[RelayCommand]`). |
| `ScottPlot.WPF` | 5.1.59 | Biblioteca de graficación de alto rendimiento para el osciloscopio en tiempo real (manejo eficiente de miles de puntos). |
| `Microsoft.Extensions.DependencyInjection` | 10.0.12 | Inversión de control (IoC) y contenedor de inyección de dependencias liviano. |

### 3.3 Componentes y Servicios Clave

1. **`ConDumpCaptureService` (`ICaptureService`)**:
   - Gestiona el ciclo de vida del subproceso externo (`ConDump.exe` / `ConDumpAuto.exe`).
   - Lee de forma asíncrona la salida estándar (`StandardOutput`) sin bloquear la interfaz de usuario.
   - Descarta cabeceras no numéricas y detecta saturación en $\pm 800$ unidades.
   - Detiene de forma segura el hardware enviando el comando `stop` por `StandardInput`.
   - **Modo Simulación Integrado:** Si el binario físico no está presente, genera automáticamente ondas compuestas con ruido a 125 Hz (muestreo cada 8 ms), permitiendo desarrollar y probar la interfaz sin hardware.

2. **`SignalRingBuffer`**:
   - Búfer circular concurrente en memoria con capacidad configurable (por defecto 1250 muestras = 10 segundos).
   - Desacopla la tasa de adquisición de datos (125 Hz) de la tasa de refresco del monitor (30 a 60 FPS), evitando asignaciones de memoria excesivas y pausas de recolección de basura (*Garbage Collector*).

3. **`PythonCliBridge` (`IBackendBridge`)**:
   - Invoca las operaciones del backend mediante `python -m digital_brain.cli`.
   - Admite `build-dataset` para crear archivos CSV a partir de capturas `.txt`.
   - Admite `train` seleccionando el algoritmo deseado (`random_forest`, `lda`, `svm`, `extra_trees`, `gradient_boosting`).
   - Captura `stdout` y `stderr` de forma asíncrona y reporta el progreso mediante `IProgress<string>`.

4. **`NavigationService` (`INavigationService`)**:
   - Permite la transición entre vistas (`NavigateTo<TViewModel>()`) a través de un delegado de fábrica desacoplado, sin que las vistas se conozcan entre sí.

---

## 4. Análisis y Evaluación de las Decisiones Arquitectónicas

### Puntos Fuertes de la Arquitectura Decidida:
1. **Lienzo Limpio sin Deuda Técnica:** Al eliminar vistas temporales y dejar únicamente la infraestructura de servicios y contratos, el equipo puede maquetar directamente el prototipo de diseño oficial sin necesidad de refactorizar ni borrar código obsoleto.
2. **Desacoplamiento Estricto:** Ni la interfaz gráfica depende directamente de Python (se comunica a través de CLI y procesos), ni el backend de Python depende de la interfaz. Esto asegura que cambios en la UI no afecten los modelos de ML y viceversa.
3. **Resiliencia ante Ausencia de Hardware:** El modo de simulación a 125 Hz en `ConDumpCaptureService` permite continuar el desarrollo de los gráficos y widgets sin depender de la placa física conectada por USB.
4. **Manejo de Frecuencia y Rendimiento:** El uso conjunto de `SignalRingBuffer` y `ScottPlot.WPF` garantiza que el osciloscopio pueda graficar los 4 canales de forma fluida sin degradar el rendimiento de la CPU.

### Consideraciones y Próximos Pasos Técnicos:
1. **Detección del Entorno de Python:** Actualmente `PythonCliBridge` usa `"python"` por defecto o busca la carpeta `BACK`. Se recomienda añadir en la UI una opción para configurar la ruta del intérprete o enlazar automáticamente el entorno virtual ubicado en `$env:LOCALAPPDATA\DigitalBrain\venv\Scripts\python.exe`.
2. **Inferencia en Tiempo Real:** El backend cuenta con `RealtimeClassifier`, pero aún no expone un canal IPC estructurado (como sockets o tuberías con JSON) para clasificar en vivo mientras se captura. Este protocolo deberá definirse cuando se implemente la pantalla de inferencia.
3. **Persistencia de Sesiones:** El modelo `SessionMetadata` incluye el nombre del sujeto, etiqueta y número de sesión; en las siguientes pantallas se podrá enlazar con un formulario de inicio de sesión y guardado automático.

---

## 5. Guía de Ejecución y Compilación

### 5.1 Frontend (C# / WPF)
Requisitos: SDK de **.NET 10.0** instalado.

```powershell
# Ubicarse en la carpeta FRONT desde la raíz del repositorio
cd FRONT

# Restaurar paquetes y compilar
dotnet build DigitalBrain.UI.csproj

# Ejecutar la aplicación
dotnet run --project DigitalBrain.UI.csproj
```

### 5.2 Backend (Python)
Requisitos: **Python 3.10+**.

```powershell
# Ubicarse en BACK
cd BACK

# Crear e inicializar entorno virtual
python -m venv "$env:LOCALAPPDATA\DigitalBrain\venv"
$pythonDB = "$env:LOCALAPPDATA\DigitalBrain\venv\Scripts\python.exe"

# Instalar dependencias en modo editable
& $pythonDB -m pip install -e .

# Ejecutar pruebas unitarias
& $pythonDB -m unittest discover -s tests -v
```

---

## 6. Historial de Commits en `feature/frontend`

1. `feat(front): inicializar arquitectura base de escritorio en C# con WPF (MVVM)`:
   - Reemplazo de archivo inicial por proyecto .NET 10 WPF.
   - Creación de carpetas `Models`, `Services`, `Styles`, `ViewModels`, `Views`, `Controls`.
   - Adición de servicios reutilizables (`ConDumpCaptureService`, `SignalRingBuffer`, `PythonCliBridge`, `NavigationService`).
   - Actualización de `.gitignore` para artefactos de compilación .NET.
2. `refact(bridge): incorporar flag --algorithm en invocación de entrenamiento CLI`.
3. `docs: crear documentacion.md con analisis arquitectonico y estado del proyecto`.
