"""Caso de uso para convertir capturas en conjuntos de entrenamiento."""

from collections.abc import Iterator
from pathlib import Path
import pandas as pd
from digital_brain.domain.signal_features import FEATURE_COLUMNS, SpectralFeatureExtractor
from digital_brain.infrastructure.capture import read_samples


def build_dataset(input_directory: Path, output_path: Path, label: str, extractor: SpectralFeatureExtractor, step_size: int | None = None) -> pd.DataFrame:
    capture_files = sorted(input_directory.glob("*.txt"))
    if not capture_files:
        raise FileNotFoundError(f"No se encontraron archivos .txt en: {input_directory}")
    step_size = step_size or extractor.window_size // 2
    if step_size <= 0:
        raise ValueError("El avance de ventana debe ser positivo.")
    rows = [{**extractor.extract(window), "label": label} for file_path in capture_files for window in _windows(read_samples(file_path), extractor.window_size, step_size)]
    dataset = pd.DataFrame(rows, columns=[*FEATURE_COLUMNS, "label"])
    output_path.parent.mkdir(parents=True, exist_ok=True)
    dataset.to_csv(output_path, index=False)
    return dataset


def _windows(samples: Iterator[tuple[float, float, float, float]], window_size: int, step_size: int) -> Iterator[list[list[float]]]:
    buffered_samples = list(samples)
    for start in range(0, len(buffered_samples) - window_size + 1, step_size):
        yield [list(channel) for channel in zip(*buffered_samples[start:start + window_size])]
