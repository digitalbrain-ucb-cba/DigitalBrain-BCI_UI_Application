"""Flujo común para cargar datos, entrenar, evaluar y guardar un modelo."""

from pathlib import Path
import pickle

import pandas as pd
from sklearn.model_selection import train_test_split

from digital_brain.application.models import create_model
from digital_brain.domain.signal_features import FEATURE_COLUMNS, SCRIPT_FEATURE_COLUMNS


def train_classifier(
    dataset_paths: list[Path],
    model_path: Path,
    test_size: float = 0.2,
    *,
    algorithm: str = "random_forest",
) -> float:
    """Entrena un clasificador y devuelve su exactitud en la partición de prueba."""
    dataset = _load_dataset(dataset_paths)
    features = dataset.loc[:, list(FEATURE_COLUMNS)]
    labels = dataset["label"]
    train_features, test_features, train_labels, test_labels = train_test_split(
        features,
        labels,
        test_size=test_size,
        random_state=7,
        stratify=labels,
    )
    classifier = create_model(algorithm)
    classifier.fit(train_features, train_labels)
    model_path.parent.mkdir(parents=True, exist_ok=True)
    with model_path.open("wb") as model_file:
        pickle.dump(classifier, model_file)
    return float(classifier.score(test_features, test_labels))


def _read_dataset(path: Path) -> pd.DataFrame:
    """Lee un CSV del proyecto o de DatosACSVNew.py, que añade el índice y nombra las columnas ThetaA, AlphaB…"""
    return pd.read_csv(path).drop(columns="Unnamed: 0", errors="ignore").rename(columns=SCRIPT_FEATURE_COLUMNS)


def _load_dataset(dataset_paths: list[Path]) -> pd.DataFrame:
    if not dataset_paths:
        raise ValueError("Se requiere al menos un archivo de dataset.")
    dataset = pd.concat([_read_dataset(path) for path in dataset_paths], ignore_index=True)
    missing_columns = (set(FEATURE_COLUMNS) | {"label"}) - set(dataset.columns)
    if missing_columns:
        raise ValueError(f"Faltan columnas requeridas: {sorted(missing_columns)}")
    if dataset["label"].nunique() < 2:
        raise ValueError("El entrenamiento requiere al menos dos etiquetas.")
    return dataset
