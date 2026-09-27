"""Construcción de los clasificadores disponibles para entrenamiento."""

from sklearn.base import BaseEstimator
from sklearn.discriminant_analysis import LinearDiscriminantAnalysis
from sklearn.ensemble import ExtraTreesClassifier, GradientBoostingClassifier, RandomForestClassifier
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.svm import SVC


MODEL_NAMES = ("random_forest", "lda", "svm", "extra_trees", "gradient_boosting")


def create_model(algorithm: str, random_state: int = 7) -> BaseEstimator:
    """Crea el estimador solicitado sin entrenarlo."""
    if algorithm == "random_forest":
        return RandomForestClassifier(random_state=random_state)
    if algorithm == "lda":
        return LinearDiscriminantAnalysis()
    if algorithm == "svm":
        return Pipeline(
            [
                ("scaler", StandardScaler()),
                ("classifier", SVC(kernel="rbf", probability=True, random_state=random_state)),
            ]
        )
    if algorithm == "extra_trees":
        return ExtraTreesClassifier(random_state=random_state)
    if algorithm == "gradient_boosting":
        return GradientBoostingClassifier(random_state=random_state)
    raise ValueError(f"Algoritmo desconocido: {algorithm}. Opciones: {', '.join(MODEL_NAMES)}")
