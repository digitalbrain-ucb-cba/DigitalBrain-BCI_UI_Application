# Arquitectura

La aplicación sigue una Arquitectura Limpia ligera, adecuada para un proyecto de procesamiento de señales:

- `domain`: cálculo de características, constantes y validaciones; no conoce archivos, procesos ni modelos almacenados.
- `application`: orquesta los casos de uso (`build_dataset`, entrenamiento por modelo y `RealtimeClassifier`).
- `infrastructure`: implementa el borde con el sistema operativo y el formato de captura; aquí vive el adaptador del ejecutable.
- `cli.py`: interfaz para ejecutar los casos de uso.

El ejecutable externo se ubica en `assets/executables/ConDumpAuto.exe`. No se incluye ni se inicia de forma automática. `ExecutableCapture` lo inicia cuando se invoca el comando de captura.

Los datos originales permanecen en `datos/`; los resultados generados se guardan en `artifacts/datasets/` y `artifacts/models/`.

## Uso

```powershell
python -m pip install -e .
python -m digital_brain.cli build-dataset datos/left artifacts/datasets/left.csv pierna_izquierda
python -m digital_brain.cli train artifacts/models/random_forest.pkl artifacts/datasets/left.csv artifacts/datasets/right.csv
python -m digital_brain.cli capture assets/executables/ConDumpAuto.exe datos_capturados --prefix "Hombro izquierdo" --seconds 30 --capture-from 6
```

El comando `capture` muestra la salida recibida en la terminal y guarda en un archivo `.txt` los mismos caracteres recibidos desde el segundo indicado hasta completar la duración. Envía `stop` por la entrada estándar al terminar y detiene el proceso si no se cierra.

Los módulos para entrenar los clasificadores son `random_forest.py`, `lda.py`, `svm.py`, `extra_trees.py` y `gradient_boosting.py`.
