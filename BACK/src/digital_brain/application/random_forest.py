"""Caso de uso para entrenamiento y almacenamiento de modelos."""

import argparse
from pathlib import Path
import pickle
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.model_selection import train_test_split
from digital_brain.domain.signal_features import FEATURE_COLUMNS


def train_classifier(dataset_paths: list[Path], model_path: Path, test_size: float = 0.2) -> float:
    datasets = [pd.read_csv(path) for path in dataset_paths]
    dataset = pd.concat(datasets, ignore_index=True)
    _validate_dataset(dataset)
    features, labels = dataset.loc[:, FEATURE_COLUMNS], dataset["label"]
    train_features, test_features, train_labels, test_labels = train_test_split(features, labels, test_size=test_size, random_state=7, stratify=labels)
    classifier = RandomForestClassifier(random_state=7)
    classifier.fit(train_features, train_labels)
    model_path.parent.mkdir(parents=True, exist_ok=True)
    with model_path.open("wb") as model_file:
        pickle.dump(classifier, model_file)
    return float(classifier.score(test_features, test_labels))


def _validate_dataset(dataset: pd.DataFrame) -> None:
    missing_columns = (set(FEATURE_COLUMNS) | {"label"}) - set(dataset.columns)
    if missing_columns:
        raise ValueError(f"Faltan columnas requeridas: {sorted(missing_columns)}")
    if dataset["label"].nunique() < 2:
        raise ValueError("El entrenamiento requiere al menos dos etiquetas.")


def main() -> None:
    parser = argparse.ArgumentParser(description="Entrena DigitalBrain con Random Forest.")
    parser.add_argument("model_path", type=Path)
    parser.add_argument("datasets", type=Path, nargs="+")
    arguments = parser.parse_args()
    score = train_classifier(arguments.datasets, arguments.model_path)
    print(f"Modelo Random Forest guardado en {arguments.model_path}; exactitud: {score:.3f}")


if __name__ == "__main__":
    main()
