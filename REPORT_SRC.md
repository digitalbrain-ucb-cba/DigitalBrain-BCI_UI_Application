# Reporte tecnico: `src/digital_brain`

## Objetivo de la reorganizacion

Antes, la logica estaba repartida en cuatro scripts ejecutables desde la raiz.
Cada uno mezclaba detalles del sistema operativo, lectura de archivos,
transformacion FFT, persistencia y logica de negocio. La nueva estructura usa
capas para que cada parte tenga una responsabilidad concreta y se pueda probar,
reutilizar o reemplazar sin afectar al resto.

```
cli -> application -> domain
       application -> infrastructure
```

La dependencia siempre apunta hacia el centro: `domain` no depende de Pandas,
archivos, modelos serializados ni del ejecutable externo.

## Capa `domain`

Ubicacion: `src/digital_brain/domain/`

Contiene reglas de negocio puras para transformar una ventana de cuatro canales
en caracteristicas espectrales. No abre archivos ni inicia procesos.

| Archivo | Elementos principales | Responsabilidad |
| --- | --- | --- |
| `__init__.py` | Paquete de dominio | Declara el limite de la capa. No contiene logica. |
| `signal_features.py` | `SpectralFeatureExtractor`, `InvalidSignalWindowError`, `FEATURE_COLUMNS`, `FREQUENCY_BANDS` | Valida ventanas de cuatro canales, calcula la FFT, obtiene potencia Theta, Alpha, Beta y Gamma, y devuelve un diccionario con 16 caracteristicas nombradas. |

`SpectralFeatureExtractor.extract()` sustituye la duplicacion de calculos FFT y
bandas que existia tanto en `DatosACSVNew.py` como en `ReadRealTime (1).py`.
Ahora hay una unica definicion de columnas y bandas, por lo que el entrenamiento
y la prediccion usan exactamente el mismo contrato.

## Capa `application`

Ubicacion: `src/digital_brain/application/`

Coordina casos de uso. Esta capa decide el flujo de trabajo, pero delega la
lectura de datos en infraestructura y el calculo matematico en dominio.

| Archivo | Elementos principales | Responsabilidad |
| --- | --- | --- |
| `__init__.py` | Paquete de casos de uso | Declara el limite de la capa. |
| `dataset.py` | `build_dataset`, `_windows` | Busca capturas `.txt`, elimina datos invalidos mediante el adaptador, forma ventanas deslizantes, extrae caracteristicas y produce un CSV etiquetado. |
| `training.py` | `train_classifier`, `_validate_dataset` | Combina datasets CSV, valida columnas y etiquetas, divide entrenamiento/prueba, entrena `RandomForestClassifier`, guarda el modelo y devuelve la exactitud. |
| `realtime.py` | `RealtimeClassifier`, `predict_windows` | Carga un modelo una sola vez, recibe un flujo de muestras, arma ventanas y emite una prediccion por ventana. |

### Equivalencia con el codigo anterior

| Script anterior | Funcion anterior | Nuevo destino |
| --- | --- | --- |
| `DatosACSVNew.py` | `leer_datos_a_csv` y bucles de ventanas | `application/dataset.py: build_dataset` |
| `MachineLearning.py` | Lectura CSV, `train_test_split`, entrenamiento y `pickle.dump` globales | `application/training.py: train_classifier` |
| `ReadRealTime (1).py` | Carga de modelo y funcion `predict` | `application/realtime.py: RealtimeClassifier` |

La mejora principal es que no existen ejecuciones al importar: entrenar, escribir
CSV o cargar modelos se hace solamente cuando se invoca el caso de uso
correspondiente.

## Capa `infrastructure`

Ubicacion: `src/digital_brain/infrastructure/`

Contiene adaptadores a elementos externos y reemplazables: formato de lineas de
captura, sistema de archivos y proceso Windows.

| Archivo | Elementos principales | Responsabilidad |
| --- | --- | --- |
| `__init__.py` | Paquete de infraestructura | Declara el limite de la capa. |
| `capture.py` | `parse_sample`, `read_samples`, `ExecutableCapture` | Convierte una linea CSV en una muestra valida, lee archivos de captura y transmite la salida estandar del ejecutable externo bajo demanda. |

`parse_sample()` centraliza reglas antes repetidas: se requieren cuatro valores
numericos y se descartan muestras con los sentinelas `-800` o `800`.

### Relacion con el ejecutable y scripts anteriores

| Recurso anterior | Problema anterior | Diseno actual |
| --- | --- | --- |
| `ConDumpAuto.exe` invocado desde varios scripts | Ruta implicita y ejecucion inmediata en codigo global. | `ExecutableCapture` recibe una ruta explicita y solo se inicia al llamar `stream_lines()`. |
| `CapturarDatosExe.py` | Generaba nombres, iniciaba el `.exe`, imprimia y guardaba al mismo tiempo. | La comunicacion con el binario esta aislada en `ExecutableCapture`; la persistencia de capturas puede agregarse como un adaptador dedicado sin mezclarla con dominio. |
| Bucle infinito de `ReadRealTime (1).py` | Ejecutaba el proceso y predecia al importar el archivo. | `RealtimeClassifier` solo clasifica un iterador de muestras; la decision de iniciar o repetir el proceso queda en una interfaz futura. |

El binario debe estar en `assets/executables/ConDumpAuto.exe`. Actualmente no se
encuentra en el repositorio y sigue inactivo. La ruta y las reglas de integracion
estan documentadas en `assets/executables/README.md`.

## Interfaz de entrega

| Archivo | Responsabilidad |
| --- | --- |
| `src/digital_brain/cli.py` | Expone los comandos `build-dataset` y `train`, convierte argumentos de consola a `Path` y llama al caso de uso adecuado. No implementa FFT, lectura de lineas ni entrenamiento. |
| `src/digital_brain/__init__.py` | Identifica el paquete raiz `digital_brain`. |

Ejemplos:

```powershell
python -m digital_brain.cli build-dataset datos/left artifacts/datasets/left.csv pierna_izquierda
python -m digital_brain.cli train artifacts/models/model.pkl artifacts/datasets/left.csv artifacts/datasets/right.csv
```

## Archivos complementarios

| Archivo | Funcion |
| --- | --- |
| `pyproject.toml` | Declara el paquete, Python minimo y dependencias (`numpy`, `pandas`, `scikit-learn`). |
| `tests/test_signal_features.py` | Pruebas unitarias de contrato: nombres de caracteristicas, ventana invalida y parsing de capturas. |
| `.gitignore` | Evita versionar caches, modelos, CSV generados y binarios `.exe`. |

## Beneficios concretos

- La FFT y las columnas se definen una sola vez.
- Se reemplazan nombres ambiguos como `rf`, `dfA`, `n` y `procesar` por nombres que expresan su intencion.
- El ejecutable no funcional no bloquea dataset, entrenamiento, importaciones ni pruebas unitarias.
- Agregar una API, interfaz grafica o nueva fuente de captura no exige cambiar las reglas FFT ni el entrenamiento.
