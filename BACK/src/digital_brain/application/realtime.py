"""Caso de uso para inferencia sobre un flujo de muestras."""

from collections.abc import Iterator
from pathlib import Path
import pickle
import pandas as pd
from digital_brain.domain.signal_features import FEATURE_COLUMNS, SCRIPT_FEATURE_COLUMNS, SpectralFeatureExtractor


class RealtimeClassifier:
    def __init__(self, model_path: Path, extractor: SpectralFeatureExtractor) -> None:
        if not model_path.is_file():
            raise FileNotFoundError(f"No se encontró el modelo: {model_path}")
        with model_path.open("rb") as model_file:
            self._model = pickle.load(model_file)
        self._extractor = extractor
        # sklearn exige los nombres con los que se entrenó: el proyecto usa theta_A y MachineLearning.py ThetaA.
        self._columns = [str(name) for name in getattr(self._model, "feature_names_in_", FEATURE_COLUMNS)]
        unknown = [name for name in self._columns if SCRIPT_FEATURE_COLUMNS.get(name, name) not in FEATURE_COLUMNS]
        if unknown:
            raise ValueError(f"El modelo usa columnas desconocidas: {unknown}")

    def predict_windows(self, samples: Iterator[tuple[float, float, float, float]], step_size: int | None = None) -> Iterator[object]:
        step_size = step_size or self._extractor.default_step
        buffer: list[tuple[float, float, float, float]] = []
        for sample in samples:
            buffer.append(sample)
            if len(buffer) < self._extractor.window_size:
                continue
            channels = [list(channel) for channel in zip(*buffer)]
            features = self._extractor.extract(channels)
            row = [features[SCRIPT_FEATURE_COLUMNS.get(name, name)] for name in self._columns]
            yield self._model.predict(pd.DataFrame([row], columns=self._columns))[0]
            del buffer[:step_size]
