# Reporte de Optimización y Propuesta de UI

**Proyecto:** DigitalBrain — clasificación de intención motora a partir de EEG/EMG de 4 canales (Neurobit Optima+ 4, 125 Hz)
**Rama analizada:** `main` (`d3182ed`)
**Fecha:** 2026-09-06
**Alcance:** revisión estática de los 4 scripts Python, el corpus `datos/` (50 archivos `.txt`, 4.9 MB) y los CSV derivados (`left.csv`, `right.csv`).

> Este documento es exclusivamente de diagnóstico y propuesta. No se ha modificado ningún archivo de código fuente del proyecto.

---

## Resumen ejecutivo

El proyecto implementa un pipeline coherente de extracción de potencia espectral por bandas (Theta/Alpha/Beta/Gamma × 4 canales = 16 features) sobre ventanas deslizantes de 125 muestras con avance de 63. La arquitectura conceptual es correcta y está bien encaminada.

Sin embargo, el pipeline **no es ejecutable de extremo a extremo en su estado actual**. Se identificaron cuatro defectos bloqueantes verificados:

| # | Defecto | Archivo | Consecuencia |
|---|---------|---------|--------------|
| B1 | Ambas clases se etiquetan `"lol,lmao"` | `DatosACSVNew.py` | El dataset generado no es separable: no distingue izquierda de derecha |
| B2 | Se leen `PiernaIzquierda.csv` / `BrazoDer.csv`, inexistentes | `MachineLearning.py` | `FileNotFoundError` en la primera línea de ejecución |
| B3 | `IndexError` en el búfer antes de alcanzar la rama de 126 | `ReadRealTime (1).py` | La inferencia en tiempo real aborta al llegar a la muestra 125 |
| B4 | `pd.DataFrame` construido desde escalares sin índice | `ReadRealTime (1).py` | `ValueError: If using all scalar values, you must pass an index` |

A esto se suma un problema metodológico de alto impacto: **fuga de datos entre entrenamiento y prueba** por ventanas solapadas al 50 % repartidas aleatoriamente, que inflará artificialmente la exactitud reportada.

La prioridad recomendada es: (1) corregir el etiquetado y el bloqueo del pipeline, (2) unificar la extracción de features en un único módulo compartido entre entrenamiento e inferencia, (3) corregir el particionado por grupos, y solo después (4) construir la interfaz de usuario.

---

## 1. Diagnóstico de Archivos de Tareas

### Inventario

| Archivo | Rol en el pipeline | Estado |
|---------|--------------------|--------|
| `CapturarDatosExe.py` | Etapa 1 — Adquisición: lanza `ConDumpAuto.exe` y persiste su stdout | Funcional, con mejoras pendientes |
| `DatosACSVNew.py` | Etapa 2 — Procesamiento: parseo, ventaneo, FFT, features → CSV | Funcional, con un defecto crítico de etiquetado |
| `MachineLearning.py` | Etapa 3 — Entrenamiento: RandomForest → `.pkl` | **No ejecutable** |
| `ReadRealTime (1).py` | Etapa 4 — Inferencia en tiempo real | **No ejecutable** |
| `datos/left/`, `datos/Right/` | Corpus crudo, 25 + 25 sesiones | Nomenclatura inconsistente |
| `left.csv`, `right.csv` | Dataset derivado, 998 + 970 ventanas | Etiquetas inválidas |

---

### 1.1 `CapturarDatosExe.py` — Adquisición

**Función actual.** Expone `siguiente_nombre()`, que resuelve el próximo nombre libre `prefijo_N.txt`, y `ejecutar_y_guardar()`, que arranca el ejecutable del dispositivo con `subprocess.Popen`, redirige `stderr` a `stdout` y escribe cada línea simultáneamente en consola y en un archivo nuevo. Evita correctamente la sobrescritura de sesiones previas.

**Oportunidades de mejora.**

- **Pérdida de datos ante interrupción.** El script documenta que la sesión termina con `Esc`, pero no captura `KeyboardInterrupt`. Al interrumpirse, el búfer de escritura de Python no se vacía y el proceso hijo puede quedar huérfano reteniendo el puerto USB del dispositivo. Es el riesgo operativo más serio del archivo: una sesión de captura sobre un sujeto humano no es repetible a coste cero.
- **Sin `flush` periódico.** Una caída del proceso pierde hasta un búfer completo de muestras.
- **Sin validación previa.** No se comprueba que `ConDumpAuto.exe` exista ni sea ejecutable; el fallo aparece como una traza de `FileNotFoundError` poco informativa para un operador no técnico.
- **Sin timeout ni límite de duración.** No hay forma de solicitar «captura de 30 segundos», que es lo que un protocolo experimental necesita.
- **Sin metadatos.** El `.txt` no registra sujeto, clase/etiqueta, sesión, fecha ni condiciones. La clase se infiere hoy del nombre de la carpeta, un acoplamiento frágil.
- **La salida no alimenta el pipeline.** Escribe en `datos_capturados/captura_N.txt`, mientras que `DatosACSVNew.py` lee de `datos/left` y `datos/right`. Toda captura nueva requiere un movimiento manual de archivos.
- **Import muerto.** `from datetime import datetime` no se usa.
- **Sin interfaz de parámetros.** Ruta, carpeta y prefijo están fijados en el bloque `__main__`; cambiar de clase exige editar código.

**Buenas prácticas recomendadas.**

```
try:
    for line in iter(process.stdout.readline, ""):
        ...
        f.write(line)
except KeyboardInterrupt:
    pass
finally:
    process.terminate()
    try:
        process.wait(timeout=5)
    except sp.TimeoutExpired:
        process.kill()
    f.flush()
    os.fsync(f.fileno())
```

Complementar con: validación mediante `shutil.which()` / `os.access()` antes de lanzar; `flush()` cada N líneas; escritura de un `sesion.json` hermano con sujeto, clase, timestamp ISO-8601, tasa de muestreo y número de muestras válidas; y `argparse` en el `__main__` conservando los valores actuales como predeterminados, de modo que la invocación existente siga funcionando sin cambios.

---

### 1.2 `DatosACSVNew.py` — Procesamiento y extracción de features

**Función actual.** Recorre los `.txt` de una carpeta, descarta la cabecera del dispositivo y las líneas de log quedándose con las que empiezan por dígito o `-`, filtra muestras saturadas, convierte a `float`, y aplica una ventana deslizante de 125 muestras con avance de 63. Por ventana y canal calcula `|FFT|²` y suma la potencia en Theta (4–8 Hz), Alpha (8–13), Beta (13–30) y Gamma (≥30), produciendo 16 features más la etiqueta. Es el archivo mejor estructurado del proyecto.

#### Defecto crítico — etiquetado (B1)

```python
def leer_datos_a_csv(folder_path, nombre_csv, label="lol,lmao", ...)
...
leer_datos_a_csv(os.path.join("datos", "left"),  "left.csv")   # sin label
leer_datos_a_csv(os.path.join("datos", "right"), "right.csv")  # sin label
```

Ninguna llamada pasa `label`, por lo que ambas clases heredan el marcador de posición. Verificado sobre los artefactos versionados: el único valor distinto de la cabecera en la columna 17 de **ambos** CSV es `"lol,lmao"`. Consecuencias:

1. Un modelo entrenado con esta unión ve una sola clase y no puede aprender nada.
2. La coma interna obliga a entrecomillar el campo, un riesgo latente para cualquier consumidor que parsee por `split(",")`.

**Recomendación:** convertir `label` en parámetro obligatorio (sin valor por defecto) y usar identificadores sin comas ni espacios: `izquierda` / `derecha`. Añadir una aserción de que el CSV resultante contiene al menos dos clases distintas antes de escribirlo.

#### Portabilidad de rutas

`os.path.join("datos", "right")` frente a la carpeta real `datos/Right`. Funciona hoy únicamente porque NTFS es insensible a mayúsculas; falla en Linux, contenedores o CI. Además, la nomenclatura interna es inconsistente (`Pierna izquierda 1.txt`, `Pierna Izquierda 2.txt`, `Brazo Derecho 10b.txt`, `Brazo derecho 12b.txt`) y falta `Pierna izquierda 4.txt` en la secuencia. Recomendación: normalizar a minúsculas (`datos/left`, `datos/right`) y resolver rutas relativas al archivo (`Path(__file__).parent`) en lugar de al directorio de trabajo.

#### Calidad de señal

- **Filtro de saturación por comparación de cadenas.** `if num == "-800" or num == "800"` solo captura la representación textual exacta. En los datos reales conviven `-800` y `799.976`; este último **no** se detecta como saturación. Recomendación: filtrar numéricamente, `if any(abs(v) >= 799.0 for v in valores)`, tras la conversión a `float`.
- **Ruptura de continuidad temporal.** Las filas saturadas se eliminan y las restantes se concatenan antes de ventanear. Una ventana puede así abarcar muestras separadas por segundos de señal descartada, introduciendo una discontinuidad artificial cuyo espectro contamina todas las bandas. En el archivo de muestra inspeccionado se descartan 151 de 2 424 filas (≈6 %), suficiente para afectar a buena parte de las ventanas. **Recomendación:** segmentar la señal en tramos contiguos válidos y ventanear cada tramo por separado, descartando los tramos más cortos que la ventana.
- **Ausencia de ventana de apodización.** Se aplica la FFT directamente al segmento rectangular, generando fuga espectral que desplaza energía entre bandas. Recomendación: multiplicar por una ventana de Hann y compensar su ganancia coherente.
- **Sin eliminación de DC ni detrend.** Una deriva de línea base concentra energía en las frecuencias bajas y sesga Theta.
- **Banda Gamma sin límite superior.** `Hz >= 30` abarca hasta Nyquist (62 Hz), incluyendo la interferencia de red de 50/60 Hz. Con `N=125` y `fs=125` la resolución es exactamente 1 Hz, de modo que el bin de red cae íntegro dentro de Gamma y probablemente la domina. Recomendación: acotar Gamma a 30–45 Hz y aplicar un filtro notch antes del análisis.
- **Banda Delta ausente.** No se extrae 0.5–4 Hz; es información potencialmente discriminante que se descarta sin justificación explícita.
- **Sin normalización por longitud de ventana.** La potencia no es comparable si `tamanowindow` cambia, lo que acopla implícitamente el CSV al valor 125.

#### Correctitud del ventaneo

`while sr < tamano` descarta la última ventana completa cuando `sr == tamano`. La condición correcta es `sr <= tamano`. El impacto es menor (una ventana por archivo, ~50 en total) pero es un error silencioso.

#### Rendimiento

Bucle Python que ejecuta cuatro `np.fft.fft` independientes por ventana y hace `append` sobre 17 listas paralelas. La ruta vectorizada equivalente:

```
from numpy.lib.stride_tricks import sliding_window_view
sig  = np.stack([datosAp, datosBp, datosCp, datosDp])        # (4, T)
win  = sliding_window_view(sig, tamanowindow, axis=1)[:, ::avancewindow]
esp  = np.abs(np.fft.rfft(win * np.hanning(tamanowindow), axis=-1)) ** 2
```

reduce el cálculo a una sola llamada por lote y permite obtener las cuatro bandas con un producto matricial contra una máscara `(4_bandas × n_bins)`. El uso de `rfft` en lugar de `fft` descarta el espectro de frecuencias negativas —redundante para señal real— y aproximadamente duplica la velocidad por sí solo. Mejora esperada: uno a dos órdenes de magnitud. Sobre el corpus actual el tiempo absoluto no es crítico, pero sí lo será al crecer el dataset y —sobre todo— porque el mismo código debe correr en tiempo real.

#### Legibilidad y escalabilidad

- **17 listas paralelas.** `ThetaA…GammaD` más `labels` deben mantenerse alineadas manualmente; añadir una banda o un canal implica tocar ~20 líneas. Sustituir por una matriz `(n_ventanas, n_features)` y generar los nombres de columna desde constantes: `[f"{b}{c}" for b in BANDAS for c in CANALES]`.
- **Bandas codificadas 16 veces.** Extraer a `BANDAS = {"Theta": (4, 8), "Alpha": (8, 13), ...}` y recorrerlas.
- **Código muerto.** `rms()` y `avr()` no se invocan en ninguna parte, y ambas implementan con bucles Python lo que `np.sqrt(np.mean(np.square(x)))` y `np.mean(np.abs(x))` resuelven de forma vectorizada. Eliminarlas o, si se prevén como features futuras, integrarlas al vector de características.
- **Sin trazabilidad de procedencia.** El CSV no registra de qué archivo/sujeto/sesión proviene cada ventana. Esta columna es un **requisito previo** para corregir la fuga de datos descrita en §1.3.
- **Manejo de errores.** La conversión `map(float, ...)` ocurre después de leer el archivo completo y sin `try/except`: una sola línea malformada aborta el proceso tras haber consumido todo el trabajo. Convertir y validar por línea, contabilizando y reportando las descartadas.

---

### 1.3 `MachineLearning.py` — Entrenamiento

**Función actual.** Concatena dos CSV, separa las 16 primeras columnas como `X` y la decimoséptima como `y`, entrena un `RandomForestClassifier` con una partición 80/20 e imprime la exactitud, serializando el modelo con `pickle`.

**Defectos bloqueantes.**

1. **Rutas inexistentes (B2).** `csv1="PiernaIzquierda.csv"` y `csv2="BrazoDer.csv"` no existen en el repositorio; los artefactos generados por `DatosACSVNew.py` son `left.csv` y `right.csv`. El script falla en la primera lectura.
2. **`KeyError` garantizado.** `df.drop(columns=["Unnamed: 0"])` presupone un índice serializado, pero `DatosACSVNew.py` escribe con `index=False`. Aunque se corrigieran las rutas, esta línea aborta la ejecución. Es un residuo de un formato de CSV anterior.
3. **Dataset degenerado.** Aun corrigiendo 1 y 2, la unión tendría una sola clase por el defecto B1.
4. **Nombre de artefacto desalineado.** Guarda `ModelMotores.pkl`; `ReadRealTime (1).py` carga `model.pkl`. El modelo entrenado nunca llega a inferencia.

**Rigor metodológico.**

- **Fuga de datos (impacto alto).** Las ventanas avanzan 63 muestras sobre una longitud de 125: dos ventanas consecutivas comparten aproximadamente el 50 % de sus muestras. Un `train_test_split` aleatorio coloca ventanas casi idénticas a ambos lados de la partición, de forma que el bosque memoriza en lugar de generalizar. **La exactitud impresa no es un estimador válido del rendimiento con un sujeto o sesión nuevos.** Solución: `GroupShuffleSplit` o `StratifiedGroupKFold` agrupando por archivo de origen —lo que requiere la columna de procedencia de §1.2—, idealmente por sujeto.
- **Sin `stratify=y`** en la partición, lo que con clases desbalanceadas (998 vs. 970 ventanas) añade varianza innecesaria.
- **Métrica única.** Solo se reporta `accuracy`, sin matriz de confusión, precisión/exhaustividad por clase, F1 ni ROC-AUC, y sin comparación contra una línea base (`DummyClassifier(strategy="most_frequent")`) que contextualice el número.
- **Sin validación cruzada.** Una sola partición sobre ~1 970 muestras da un intervalo de confianza amplio.
- **Sin reproducibilidad.** `RandomForestClassifier()` no fija `random_state`; cada ejecución produce un modelo distinto.
- **Sin ajuste de hiperparámetros** ni control de `max_depth` / `min_samples_leaf`, en un régimen donde el sobreajuste es probable.
- **Escala de las features.** La potencia espectral abarca varios órdenes de magnitud (de ~10⁴ a ~10⁷ en `left.csv`). RandomForest es invariante a transformaciones monótonas, pero una transformación logarítmica dejaría el dataset listo para probar modelos lineales o SVM sin rehacer el pipeline.

**Ingeniería.**

- **Selección posicional.** `iloc[:, 0:16]` e `iloc[:, 16]` se rompen silenciosamente al añadir una columna (por ejemplo, la de procedencia recomendada). Seleccionar por nombre.
- **`pd.concat` sin `ignore_index=True`** produce índices duplicados.
- **`encoding="latin-1"`** es incorrecto: `to_csv` escribe UTF-8 por defecto. Hoy es inocuo (los datos son numéricos) pero corromperá cualquier etiqueta o metadato no ASCII que se añada.
- **Serialización sin contrato.** El `.pkl` no guarda orden de columnas, versión de scikit-learn, parámetros de ventaneo, definición de bandas ni métricas. Cargar un pickle con otra versión de sklearn puede fallar o —peor— degradarse en silencio. Recomendación: persistir un diccionario `{"modelo": rf, "columnas": [...], "bandas": {...}, "fs": 125, "ventana": 125, "avance": 63, "sklearn": sklearn.__version__, "metricas": {...}, "fecha": "..."}`.
- **Todo a nivel de módulo.** Sin funciones ni guarda `__main__`: importarlo dispara un entrenamiento completo, e impide reutilizarlo o probarlo.

---

### 1.4 `ReadRealTime (1).py` — Inferencia en tiempo real

**Función actual.** Carga el modelo, mantiene cuatro búferes de 125 muestras, lanza `ConDumpAuto.exe` en bucle y, conforme llegan líneas, acumula muestras e invoca `predict()`, que replica la extracción de features de `DatosACSVNew.py` y devuelve la clase.

Es el archivo con más defectos y el más crítico, porque es el que ve el usuario final.

**Defectos bloqueantes.**

1. **`IndexError` (B3).** Los búferes tienen 125 posiciones (índices 0–124). `procesar` se incrementa sin acotar, de modo que en la muestra 126 la asignación `datosA[125] = ...` lanza `IndexError`. En consecuencia **la rama `elif procesar == 126` es código inalcanzable** y el reinicio del búfer nunca ocurre. El programa muere pocos segundos después de arrancar.
2. **`ValueError` en la construcción del DataFrame (B4).** `pd.DataFrame(dict(zip(nombres, escalares)))` con valores escalares lanza `ValueError: If using all scalar values, you must pass an index` (comportamiento verificado en el entorno). La forma correcta es `pd.DataFrame([predictData])`.
3. **FFT sobre cadenas de texto.** `datosA[procesar] = current[0]` almacena el `str` sin convertir; `np.fft.fft` sobre un array de cadenas falla. Falta el `float()`.
4. **Predicción sobre búfer a medio llenar.** La rama `procesar == 64` invoca `predict()` con `datosA[0:125]`, de los cuales las posiciones 64–124 aún contienen el centinela inicial. Además, ese centinela es el objeto `typing.Any` (`datosA = [Any] * 125`), un uso incorrecto de una anotación de tipos como valor. Sustituir por `collections.deque(maxlen=125)` o `np.zeros(125)`, y no predecir hasta que el búfer esté lleno.

**Divergencia entrenamiento/inferencia (*train–serve skew*).**

`predict()` es una copia literal de la lógica de features de `DatosACSVNew.py`, pero **omite el filtro de saturación de ±800**. Se entrena, por tanto, con señal limpia y se infiere con señal potencialmente saturada: las distribuciones no coinciden y el rendimiento en producción será inferior al medido. Más allá de este caso concreto, la duplicación garantiza que cualquier ajuste futuro de bandas, ventana o apodización desincronice modelo e inferencia sin producir ningún error visible. **Es el problema arquitectónico de mayor impacto del proyecto:** la extracción de features debe existir en un único módulo importado por ambos lados.

**Robustez y operación.**

- **`while True` sin condición de salida** relanza el ejecutable indefinidamente, sin backoff ni límite de reintentos. Un fallo persistente del dispositivo produce un bucle de arranque a máxima velocidad.
- **Sin gestión del proceso hijo.** No se cierra `process.stdout` ni se termina el proceso; cada iteración deja un `ConDumpAuto.exe` huérfano reteniendo el dispositivo USB.
- **Sin `KeyboardInterrupt`.** No hay forma limpia de detener la sesión.
- **`line[0]` sin comprobar longitud.** Una línea vacía lanza `IndexError`. `DatosACSVNew.py` sí protege este caso con `line.strip()`; aquí no.
- **`print(line)` en el bucle caliente.** Añade un salto de línea espurio (la línea ya lo trae) y ejecuta I/O sincrónica a 125 Hz.
- **Imports muertos:** `threading` y `List` no se usan.
- **Salida sin suavizado.** Se emite una predicción cruda por ventana, sin voto por mayoría ni histéresis; para accionar un motor esto se traduce en oscilaciones. Recomendación: mayoría móvil sobre las últimas N ventanas y umbral sobre `predict_proba`.
- **Nombre de archivo no importable.** `ReadRealTime (1).py` contiene espacio y paréntesis: no puede ser importado como módulo ni referenciado con `-m`. Renombrar a `inferencia_tiempo_real.py`.

---

## 2. Optimización del Proceso Global

### 2.1 Flujo end-to-end actual

```
[Neurobit Optima+ 4]
        │  125 Hz × 4 canales
        ▼
  ConDumpAuto.exe ──stdout──► CapturarDatosExe.py ──► datos_capturados/captura_N.txt
                                                              │
                                              ✗ MOVIMIENTO MANUAL Y RENOMBRADO
                                                              ▼
                                              datos/left/*.txt · datos/Right/*.txt
                                                              │
                                                              ▼
                                          DatosACSVNew.py  (parseo → ventaneo → FFT → bandas)
                                                              │
                                                              ▼
                                              left.csv · right.csv   ✗ misma etiqueta
                                                              │
                                        ✗ RENOMBRADO MANUAL A PiernaIzquierda/BrazoDer
                                                              ▼
                                                    MachineLearning.py
                                                              │
                                                    ModelMotores.pkl
                                                              │
                                        ✗ DESALINEACIÓN DE NOMBRE (espera model.pkl)
                                                              ▼
                                              ReadRealTime (1).py  ─► clase predicha
                                                     (features duplicadas)
```

El diagrama evidencia que **las cuatro etapas están conectadas por convenciones manuales, no por contratos de código**. Existen tres puntos de ruptura que exigen intervención humana y que no están documentados en ninguna parte.

### 2.2 Redundancias

| Redundancia | Ubicaciones | Riesgo |
|-------------|-------------|--------|
| Extracción de features (FFT + bandas) | `DatosACSVNew.py:71-99`, `ReadRealTime (1).py:22-55` | **Alto** — ya divergen (filtro ±800) |
| Parseo de líneas del dispositivo | `DatosACSVNew.py:44-56`, `ReadRealTime (1).py:62-69` | Alto — validación desigual |
| Lanzamiento de `ConDumpAuto.exe` | `CapturarDatosExe.py:25`, `ReadRealTime (1).py:60` | Medio — sin gestión de ciclo de vida en el segundo |
| Constantes `125`, `63`, límites de banda | Los cuatro archivos, como literales | Alto — cambio no propagable |
| Definición de las 16 columnas | `DatosACSVNew.py`, `ReadRealTime (1).py`, implícita en `iloc[:,0:16]` | Alto — el orden debe coincidir con el del entrenamiento |

### 2.3 Cuellos de botella

1. **FFT en bucle Python** (`DatosACSVNew.py`): cuatro transformadas independientes por ventana en lugar de un lote vectorizado. Es también el camino caliente de la inferencia, donde el coste sí es funcional y no solo de comodidad.
2. **`fft` completa en lugar de `rfft`**: se calcula y descarta el espectro de frecuencias negativas, redundante para señal real.
3. **Crecimiento por `append` sobre 17 listas**: reasignaciones repetidas y conversión final a DataFrame.
4. **I/O línea a línea con `print` en el bucle de adquisición**: a 125 Hz introduce latencia evitable y, si la consola es lenta, contrapresión sobre la tubería del proceso hijo.
5. **Reprocesado completo del corpus** en cada ejecución, sin caché ni detección de archivos ya procesados.

### 2.4 Acoplamientos innecesarios

- **Rutas relativas al directorio de trabajo**, no al del script: la ejecución desde otra carpeta falla.
- **Rutas y nombres de artefacto codificados** en variables de módulo, sin capa de configuración.
- **Etiqueta acoplada a la carpeta** en lugar de a metadatos explícitos.
- **Higiene del repositorio:** `__pycache__/DatosACSVNew.cpython-313.pyc` está versionado, no existe `.gitignore` ni `requirements.txt`, y el `README.md` (34 bytes, con BOM) solo contiene el título. Los CSV derivados se versionan junto al código, dificultando distinguir dato de artefacto. Se detectó también un `datos/Right/Brazo derecho 1b.docx` binario sin función aparente en el pipeline.
- **Dependencias no declaradas:** el entorno actual (Python 3.13.3) **no tiene `scikit-learn` instalado**, por lo que `MachineLearning.py` y `ReadRealTime (1).py` no arrancan ni siquiera con las rutas corregidas.

### 2.5 Estrategia de refactorización sin comprometer la estabilidad

Reorganización propuesta, respetando el estilo y la nomenclatura en español ya presentes en el proyecto:

```
digitalbrain/
├── config.py          # FS=125, VENTANA=125, AVANCE=63, BANDAS, UMBRAL_SATURACION, rutas
├── senales.py         # parsear_linea, segmentos_validos, extraer_features  ◄ ÚNICA FUENTE DE VERDAD
├── captura.py         # ejecutar_y_guardar (desde CapturarDatosExe.py) + metadatos
├── dataset.py         # construir_dataset(carpeta, etiqueta) → DataFrame con columna 'origen'
├── entrenamiento.py   # split por grupos, CV, métricas, artefacto con metadatos
├── inferencia.py      # búfer circular, importa senales.extraer_features, voto por mayoría
└── cli.py             # subcomandos: capturar / procesar / entrenar / predecir
```

**Principio rector:** `senales.extraer_features(matriz_4xN) → vector de 16` es invocada tanto por `dataset.py` como por `inferencia.py`. Esto elimina por construcción la divergencia entrenamiento/inferencia.

**Secuencia de migración segura.** Cada paso es independiente y verificable:

1. Añadir `.gitignore` (`__pycache__/`, `*.pyc`, `*.pkl`, `*.csv`) y `requirements.txt` con versiones fijadas; dejar de versionar el `.pyc`.
2. Extraer `config.py` con las constantes actuales, sin cambiar ningún valor.
3. Extraer `senales.py` a partir del código existente de `DatosACSVNew.py`, **sin alterar la lógica**, y validar con una prueba de regresión: el CSV regenerado debe coincidir numéricamente con `left.csv` actual dentro de la tolerancia de punto flotante. Esta prueba es la red de seguridad de todo lo demás.
4. Con la red puesta, aplicar las correcciones de señal (filtro numérico, segmentos contiguos, ventana de Hann, `rfft`, banda Gamma acotada), documentando el cambio de valores como intencional y regenerando la línea base.
5. Reescribir `ReadRealTime` sobre `senales.py`, corrigiendo B3 y B4.
6. Corregir el etiquetado (B1) y regenerar los CSV con la columna `origen`.
7. Corregir `MachineLearning.py` (B2), pasar a split por grupos y a un artefacto con metadatos.
8. Mantener los cuatro scripts de la raíz como envoltorios finos que importan el paquete, preservando toda invocación existente.

---

## 3. Propuesta de Interfaz de Usuario para Entrada de Datos

### 3.1 Evaluación de la entrada de datos actual

| Dato de entrada | Mecanismo actual | Problema |
|-----------------|------------------|----------|
| Ruta del ejecutable | Literal en `__main__` | Requiere editar código |
| Carpeta de salida y prefijo | Literales en `__main__` | No corresponden a lo que espera la etapa 2 |
| Clase/etiqueta | Implícita en el nombre de la carpeta | Origen directo del defecto B1 |
| Sujeto, sesión, fecha, condiciones | **No se capturan** | Impide el split por sujeto y la trazabilidad |
| Ventana y avance | Argumentos con valor por defecto, nunca pasados | Invisibles para el operador |
| Duración de la captura | No existe; se detiene con `Esc` | Sesiones de longitud irregular |
| Nombres de CSV y modelo | Literales en tres archivos, desalineados | Fuente de B2 y del fallo de carga del modelo |

En síntesis: **hoy la interfaz de entrada de datos es el editor de código**. Toda sesión experimental exige modificar y ejecutar scripts a mano, algo inviable para un operador clínico o un colaborador no programador, y que ya ha producido dos defectos reales en el repositorio (etiquetas de marcador de posición y rutas desalineadas).

### 3.2 Propuesta en dos fases

Se propone una estrategia escalonada: primero una CLI guiada de bajo coste que resuelve el grueso del problema, y después un panel visual para el operador final.

#### Fase A — CLI guiada con menús (esfuerzo bajo, impacto alto)

Comando único con subcomandos y modo interactivo cuando faltan argumentos. Dependencias ligeras y compatibles con la consola de Windows: `typer` (o `argparse`) + `rich` para el formato + `questionary` para los menús.

```
$ python -m digitalbrain capturar

  ╭──────────── DigitalBrain · Captura de sesión ────────────╮
  │                                                          │
  │  Sujeto      ❯ S03                                       │
  │  Movimiento  ❯ ● izquierda    ○ derecha    ○ reposo      │
  │  Duración    ❯ 30 s                                      │
  │  Sesión      ❯ 4  (autodetectada, editable)              │
  │                                                          │
  │  Dispositivo   ✓ ConDumpAuto.exe encontrado              │
  │  Destino       datos/izquierda/S03_s04.txt               │
  ╰──────────────────────────────────────────────────────────╯

  Capturando ██████████████████░░░░░░░  22 s / 30 s
  Muestras válidas 2 640   Saturadas 118 (4.3 %)
  Canal  A ▁▂▅▇▅▂▁   B ▁▃▆█▆▃▁   C ▁▂▄▆▄▂▁   D ▁▂▅▇▅▂▁

  [Esc] detener   [P] pausar
```

Cobertura funcional:

| Subcomando | Sustituye a | Entradas solicitadas |
|------------|-------------|----------------------|
| `capturar` | `CapturarDatosExe.py` | sujeto, movimiento, duración, sesión |
| `procesar` | `DatosACSVNew.py` | carpeta, **etiqueta (obligatoria)**, ventana, avance |
| `entrenar` | `MachineLearning.py` | CSV de entrada, estrategia de validación, nombre del modelo |
| `predecir` | `ReadRealTime (1).py` | modelo, umbral de confianza, ventana de suavizado |

Beneficios inmediatos: la etiqueta deja de ser opcional (B1 se vuelve imposible por construcción); las rutas las genera la herramienta y no el operador (B2 desaparece); se validan el dispositivo y los parámetros *antes* de arrancar; y cada sesión queda registrada con metadatos completos.

#### Fase B — Panel local (esfuerzo medio, impacto alto para el usuario final)

Aplicación **Streamlit** en `localhost`, sin servidor ni despliegue, arrancada con un `.bat` de doble clic. Cuatro pestañas alineadas con las etapas del pipeline:

1. **Capturar** — formulario de sesión, botón de inicio, gráfico en vivo de los 4 canales, indicador de saturación por canal, barra de progreso y confirmación de guardado.
2. **Dataset** — tabla de sesiones registradas (sujeto, movimiento, fecha, duración, % de muestras válidas), casillas para incluir o excluir, y un botón «Generar dataset» que muestra el recuento de ventanas por clase.
3. **Entrenar** — selección de la estrategia de validación, botón de entrenamiento, y resultados con matriz de confusión, F1 por clase y comparación contra la línea base.
4. **Predecir** — botón de inicio/parada, clase actual en grande, barra de confianza e historial reciente.

#### Formulario de captura propuesto

| Campo | Tipo | Validación | Destino en el pipeline |
|-------|------|------------|------------------------|
| Sujeto | Texto | Obligatorio, `[A-Za-z0-9_-]+` | Metadatos + columna `grupo` para el split |
| Movimiento | Selección | Obligatorio, catálogo cerrado | Parámetro `label` de `leer_datos_a_csv()` |
| Sesión | Entero | Autoincremental, editable | Nombre de archivo |
| Duración | Entero (s) | 5–300 | Timeout del subproceso |
| Notas | Texto libre | Opcional | `sesion.json` |
| Ventana / Avance | Entero | Avanzado, plegado | `tamanowindow` / `avancewindow` |

### 3.3 Por qué la interfaz no altera la lógica interna

El diseño mantiene una separación estricta: **la UI solo recopila y valida entradas; el cálculo permanece intacto.**

- Cada campo del formulario mapea 1:1 a un parámetro que las funciones actuales **ya aceptan**: `label`, `tamanowindow`, `avancewindow`, `folder_path`, `nombre_csv`, `ruta_exe`, `carpeta_salida`, `prefijo`. No se introduce ningún parámetro nuevo en la capa de cálculo.
- La UI invoca `leer_datos_a_csv()` y `ejecutar_y_guardar()` tal como existen hoy; no reimplementa parseo, FFT ni bandas. Si la UI se elimina, el pipeline sigue funcionando desde la línea de comandos.
- Los valores por defecto de todos los campos reproducen exactamente el comportamiento actual (125 / 63 / `datos/...`), de modo que una ejecución sin tocar nada es equivalente a la de hoy.
- Los metadatos se persisten en un `sesion.json` **hermano** del `.txt`, sin modificar el formato del archivo de datos que `DatosACSVNew.py` ya sabe leer.
- La UI depende del paquete; el paquete no depende de la UI. Se puede desarrollar y descartar sin riesgo para el núcleo.

### 3.4 Accesibilidad

Navegación completa por teclado con orden de tabulación lógico; validación en línea con mensajes accionables («No se encuentra `ConDumpAuto.exe`; verifica que el dispositivo esté conectado») en lugar de trazas de excepción; el estado nunca se comunica solo por color (icono + texto acompañan a cada indicador); contraste conforme a WCAG AA; etiquetas y unidades explícitas en todos los campos numéricos; y confirmación antes de cualquier acción destructiva, como sobrescribir un dataset o un modelo.

---

## 4. Hoja de Ruta Sugerida

### Prioridad 0 — Desbloqueo (el pipeline no funciona sin esto)

| # | Acción | Archivo | Impacto | Esfuerzo |
|---|--------|---------|---------|----------|
| 1 | Etiquetas reales `izquierda`/`derecha`; `label` obligatorio | `DatosACSVNew.py` | **Alto** | Bajo |
| 2 | Corregir rutas de CSV y eliminar `drop("Unnamed: 0")` | `MachineLearning.py` | **Alto** | Bajo |
| 3 | Corregir `IndexError` del búfer y usar `pd.DataFrame([...])` | `ReadRealTime (1).py` | **Alto** | Bajo |
| 4 | Convertir muestras a `float` antes de la FFT en inferencia | `ReadRealTime (1).py` | **Alto** | Bajo |
| 5 | Unificar el nombre del artefacto de modelo | `MachineLearning.py`, `ReadRealTime` | **Alto** | Bajo |
| 6 | `requirements.txt` con `scikit-learn` (hoy ausente) y `.gitignore` | raíz | **Alto** | Bajo |

### Prioridad 1 — Validez de los resultados

| # | Acción | Impacto | Esfuerzo |
|---|--------|---------|----------|
| 7 | Columna `origen` en el CSV y split por grupos (elimina la fuga de datos) | **Alto** | Medio |
| 8 | Módulo `senales.py` compartido entre entrenamiento e inferencia | **Alto** | Medio |
| 9 | Filtro de saturación numérico y segmentación en tramos contiguos | **Alto** | Medio |
| 10 | Métricas completas + línea base + `random_state` fijo | **Alto** | Bajo |
| 11 | Aplicar en inferencia el mismo filtro de saturación que en entrenamiento | **Alto** | Bajo |
| 12 | Prueba de regresión del CSV antes de tocar el procesamiento de señal | **Alto** | Medio |

### Prioridad 2 — Interfaz de usuario

| # | Acción | Impacto | Esfuerzo |
|---|--------|---------|----------|
| 13 | CLI guiada con subcomandos y menús (Fase A) | **Alto** | Medio |
| 14 | Metadatos de sesión en `sesion.json` | **Alto** | Bajo |
| 15 | Salida de captura dirigida a la carpeta que consume la etapa 2 | Medio | Bajo |
| 16 | Panel Streamlit con las cuatro pestañas (Fase B) | Medio | Alto |

### Prioridad 3 — Calidad de señal y modelo

| # | Acción | Impacto | Esfuerzo |
|---|--------|---------|----------|
| 17 | Ventana de Hann, detrend y normalización por N | Medio | Bajo |
| 18 | Acotar Gamma a 30–45 Hz y añadir notch de red | Medio | Bajo |
| 19 | Añadir banda Delta (0.5–4 Hz) | Medio | Bajo |
| 20 | Voto por mayoría / histéresis en la salida de inferencia | Medio | Bajo |
| 21 | Validación cruzada y ajuste de hiperparámetros | Medio | Medio |
| 22 | Artefacto de modelo con metadatos y verificación de esquema al cargar | Medio | Bajo |

### Prioridad 4 — Robustez y mantenibilidad

| # | Acción | Impacto | Esfuerzo |
|---|--------|---------|----------|
| 23 | Ciclo de vida del subproceso (`terminate`/`kill`, `KeyboardInterrupt`, `flush`) | Medio | Bajo |
| 24 | Vectorizar el ventaneo con `sliding_window_view` + `rfft` | Medio | Medio |
| 25 | Sustituir las 17 listas paralelas por una matriz con nombres derivados | Medio | Medio |
| 26 | Renombrar `ReadRealTime (1).py` → `inferencia_tiempo_real.py` | Medio | Bajo |
| 27 | Normalizar `datos/Right` → `datos/right` y la nomenclatura de archivos | Medio | Bajo |
| 28 | Eliminar código muerto (`rms`, `avr`, imports sin usar) | Bajo | Bajo |
| 29 | Corregir la condición del ventaneo (`sr <= tamano`) | Bajo | Bajo |
| 30 | README con instalación, protocolo de captura y flujo de ejecución | Medio | Bajo |
| 31 | Extraer constantes a `config.py` | Medio | Bajo |
| 32 | Registro estructurado (`logging`) en lugar de `print` | Bajo | Bajo |

### Secuencia recomendada

**Iteración 1 (P0).** Dejar el pipeline ejecutable de extremo a extremo. Todos los cambios son locales y de bajo riesgo. Al cerrar, se dispone por primera vez de una métrica —aún optimista— sobre datos correctamente etiquetados.

**Iteración 2 (P1).** Convertir esa métrica en una estimación confiable: split por grupos, features unificadas, filtrado correcto. Ejecutar el punto 12 *antes* que el 9, para poder distinguir una mejora deliberada de una regresión accidental. Es previsible que la exactitud **baje** al eliminar la fuga; ese descenso es el resultado válido y debe interpretarse como una corrección de medición, no como un retroceso.

**Iteración 3 (P2).** Construir la CLI guiada sobre una base ya estable. Aplazar el panel Streamlit hasta que la CLI esté en uso y su ergonomía validada.

**Iteración 4 (P3–P4).** Refinar señal, modelo y mantenibilidad de forma incremental, con la prueba de regresión como red de seguridad permanente.

---

*Reporte generado mediante revisión estática del código y de los artefactos de datos. Las afirmaciones sobre el `ValueError` en la construcción de DataFrames desde escalares, la resolución de la FFT (1 Hz), la ausencia de `scikit-learn` en el entorno y la uniformidad de las etiquetas en ambos CSV fueron verificadas empíricamente. Los defectos por inspección (`IndexError` del búfer, `KeyError` de `Unnamed: 0`) se derivan del flujo de control y del formato de los artefactos versionados. No se ejecutó ningún script del proyecto ni se modificó archivo alguno del código fuente.*
