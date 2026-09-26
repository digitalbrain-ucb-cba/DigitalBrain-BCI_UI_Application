"""Caso de uso para inferencia sobre un flujo de muestras."""

from collections.abc import Iterator
from pathlib import Path
import pickle
import pandas as pd
from digital_brain.domain.signal_features import FEATURE_COLUMNS, SpectralFeatureExtractor


class RealtimeClassifier:
    def __init__(self, model_path: Path, extractor: SpectralFeatureExtractor) -> None:
        with model_path.open("rb") as model_file:
            self._model = pickle.load(model_file)
        self._extractor = extractor

    def predict_windows(self, samples: Iterator[tuple[float, float, float, float]], step_size: int | None = None) -> Iterator[object]:
        step_size = step_size or self._extractor.window_size // 2
        buffer: list[tuple[float, float, float, float]] = []
        for sample in samples:
            buffer.append(sample)
            if len(buffer) < self._extractor.window_size:
                continue
            channels = [list(channel) for channel in zip(*buffer)]
            features = self._extractor.extract(channels)
            yield self._model.predict(pd.DataFrame([features], columns=FEATURE_COLUMNS))[0]
            del buffer[:step_size]
