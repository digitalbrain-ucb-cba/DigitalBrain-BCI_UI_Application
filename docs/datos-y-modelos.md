# Datos y modelos

Este documento describe el comportamiento actual del backend. Los archivos de `BACK/datos/` son capturas originales: hay archivos `.txt` de pierna izquierda en `left/`, brazo derecho en `Right/` y hombro derecho en `datos_hombro_derecho/`. Sus nombres y carpetas no constituyen metadatos formales de sujeto, sesión o posición de sensores.

## De línea de captura a muestra

`infrastructure/capture.py` intenta leer los primeros cuatro valores de cada línea separados por comas. Ignora cabeceras y líneas no numéricas; descarta una muestra si alguno de esos valores es exactamente `-800` u `800`. No verifica que los valores sean finitos ni exige que la línea tenga exactamente cuatro columnas. `read_samples()` omite las líneas descartadas, de modo que el ventaneo posterior puede unir tramos separados por muestras inválidas.

El capturador externo y sus archivos auxiliares no están incluidos en el repositorio. El comando `capture` recibe la ruta de **la carpeta externa** donde están, busca allí `ConDump.exe` por defecto y lo ejecuta con esa carpeta como directorio de trabajo. Si el archivo tiene otro nombre, se indica con `--executable-name`. El comando muestra su salida y guarda el tramo entre `--capture-from` y `--seconds` en un `.txt`. El destino predeterminado es `BACK/datos_capturados/` cuando se ejecuta desde `BACK/`; esa carpeta está ignorada por Git. El [README principal](../README.md#flujo-básico) muestra dónde escribir la ruta en PowerShell.

## Ventanas y características

El extractor usa por defecto 125 muestras por canal y una frecuencia de 125 Hz: una ventana representa aproximadamente un segundo. `build_dataset` avanza `125 // 2 = 62` muestras entre ventanas, por lo que dos ventanas consecutivas comparten 63 muestras. Las ventanas se forman por separado para cada archivo de captura.

En cada ventana se aplica una FFT a los cuatro canales. El programa suma la potencia `|FFT|²` de frecuencias positivas en cuatro bandas: theta `[4, 8)`, alpha `[8, 13)`, beta `[13, 30)` y gamma desde `30 Hz` hasta el límite positivo representado por la FFT. El resultado son 16 columnas: una por combinación de banda y canal A, B, C o D. Actualmente no se aplica una ventana de Hann, detrend ni filtrado adicional en este extractor.

El CSV generado contiene esas 16 columnas y `label`. La etiqueta es un argumento obligatorio del comando `build-dataset`; el código no verifica su significado experimental. Tampoco guarda en el CSV el archivo, sesión o sujeto de origen de cada ventana.

## Entrenamiento e inferencia

`application/training.py` combina uno o más CSV y exige las 16 columnas y al menos dos etiquetas. Divide las filas al azar en 80 % para entrenamiento y 20 % para prueba, con estratificación por etiqueta y `random_state=7`. Después entrena el estimador seleccionado, lo guarda como `.pkl` y devuelve su exactitud en la partición de prueba.

| Algoritmo | Configuración actual |
| --- | --- |
| `random_forest` | `RandomForestClassifier(random_state=7)` |
| `lda` | `LinearDiscriminantAnalysis()` |
| `svm` | `StandardScaler` seguido de `SVC` con kernel RBF y `probability=True` |
| `extra_trees` | `ExtraTreesClassifier(random_state=7)` |
| `gradient_boosting` | `GradientBoostingClassifier(random_state=7)` |

La división aleatoria puede colocar ventanas solapadas de una misma captura a ambos lados de la evaluación. Por ejemplo, las ventanas de muestras `0–124` y `62–186` comparten 63 muestras, pero pueden terminar una en entrenamiento y otra en prueba. **La exactitud actual puede ser optimista** y no mide generalización a una sesión o sujeto nuevo. Para evaluar eso harán falta metadatos de origen y una división por grupos; todavía no está implementada.

`RealtimeClassifier` carga un `.pkl` y recibe un iterador de tuplas de cuatro valores. Forma ventanas con el extractor suministrado y entrega una etiqueta por ventana. El código de inferencia no comprueba metadatos del modelo ni conecta por sí solo con `ExecutableCapture`. Carga únicamente modelos `.pkl` de fuentes confiables.

## Archivos relacionados

- [Extracción espectral](../BACK/src/digital_brain/domain/signal_features.py)
- [Lectura y captura](../BACK/src/digital_brain/infrastructure/capture.py)
- [Construcción del dataset](../BACK/src/digital_brain/application/dataset.py)
- [Entrenamiento](../BACK/src/digital_brain/application/training.py) y [modelos](../BACK/src/digital_brain/application/models.py)
- [Inferencia](../BACK/src/digital_brain/application/realtime.py)
