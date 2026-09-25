"""Entrenamiento alternativo con Extra Trees."""

import argparse
from pathlib import Path
import pickle

import pandas as pd
from sklearn.ensemble import ExtraTreesClassifier
from sklearn.model_selection import train_test_split

from digital_brain.domain.signal_features import FEATURE_COLUMNS


def train_extra_trees_classifier(
    dataset_paths: list[Path],
    model_path: Path,
    test_size: float = 0.2,
) -> float:
    """Entrena, evalua y guarda un clasificador Extra Trees."""
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

    classifier = ExtraTreesClassifier(random_state=7)
    classifier.fit(train_features, train_labels)
    _save_model(classifier, model_path)
    return float(classifier.score(test_features, test_labels))


def _load_dataset(dataset_paths: list[Path]) -> pd.DataFrame:
    if not dataset_paths:
        raise ValueError("Se requiere al menos un archivo de dataset.")
    dataset = pd.concat([pd.read_csv(path) for path in dataset_paths], ignore_index=True)
    missing_columns = (set(FEATURE_COLUMNS) | {"label"}) - set(dataset.columns)
    if missing_columns:
        raise ValueError(f"Faltan columnas requeridas: {sorted(missing_columns)}")
    if dataset["label"].nunique() < 2:
        raise ValueError("El entrenamiento requiere al menos dos etiquetas.")
    return dataset


def _save_model(classifier: ExtraTreesClassifier, model_path: Path) -> None:
    model_path.parent.mkdir(parents=True, exist_ok=True)
    with model_path.open("wb") as model_file:
        pickle.dump(classifier, model_file)


def main() -> None:
    parser = argparse.ArgumentParser(description="Entrena DigitalBrain con Extra Trees.")
    parser.add_argument("model_path", type=Path)
    parser.add_argument("datasets", type=Path, nargs="+")
    arguments = parser.parse_args()
    score = train_extra_trees_classifier(arguments.datasets, arguments.model_path)
    print(f"Modelo Extra Trees guardado en {arguments.model_path}; exactitud: {score:.3f}")


if __name__ == "__main__":
    main()
