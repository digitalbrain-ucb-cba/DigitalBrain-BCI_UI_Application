# DigitalBrain

DigitalBrain procesa capturas de cuatro canales, extrae características espectrales y entrena clasificadores con Python. El repositorio contiene un backend funcional por línea de comandos y una carpeta `FRONT` reservada para la futura aplicación de escritorio en C# y WPF. La integración WPF todavía no está implementada.

## Estado actual

| Parte | Estado |
| --- | --- |
| Captura | La CLI recibe la carpeta externa del capturador, ejecuta `ConDump.exe` desde allí y guarda su salida. El programa y sus archivos auxiliares no se incluyen. |
| Preparación de datos | La CLI convierte archivos `.txt` en CSV con 16 características por ventana y una etiqueta. |
| Entrenamiento | La CLI entrena Random Forest, LDA, SVM, Extra Trees o Gradient Boosting. |
| Inferencia | Existe `RealtimeClassifier` para uso desde Python, pero no hay comando `predict` ni conexión directa con la captura. |
| Interfaz WPF | Pendiente; `FRONT/README.md` indica dónde irá el proyecto C# y WPF. |

## Estructura

```text
DigitalBrain/
├── BACK/
│   ├── datos/               # capturas originales
│   ├── src/digital_brain/   # dominio, casos de uso, infraestructura y CLI
│   ├── tests/
│   └── pyproject.toml
├── FRONT/                   # reservado para la aplicación WPF
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

## Documentación

- [Arquitectura y flujo del backend](docs/arquitectura.md)
- [Datos, ventanas, características y modelos](docs/datos-y-modelos.md)
- [Estado y diseño pendiente de la integración WPF](docs/integracion-wpf.md)

Las capturas `.txt` de `BACK/datos/` son datos originales, no documentación. Los modelos `.pkl` son artefactos de Python y deben cargarse únicamente desde fuentes confiables.
