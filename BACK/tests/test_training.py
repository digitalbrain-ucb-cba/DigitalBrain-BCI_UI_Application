from contextlib import redirect_stdout
from io import StringIO
import random
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import pandas as pd

from digital_brain.application.models import MODEL_NAMES, create_model
from digital_brain.application.training import train_classifier
from digital_brain.domain.signal_features import FEATURE_COLUMNS
from digital_brain import cli


class TrainingTests(unittest.TestCase):
    def test_all_algorithms_train_and_save_a_model(self) -> None:
        generator = random.Random(7)
        rows = []
        for label, offset in (("left", 0.0), ("right", 3.0)):
            for _ in range(20):
                rows.append({
                    **{column: offset + generator.random() for column in FEATURE_COLUMNS},
                    "label": label,
                })

        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            dataset_path = directory / "dataset.csv"
            pd.DataFrame(rows).to_csv(dataset_path, index=False)

            for algorithm in MODEL_NAMES:
                with self.subTest(algorithm=algorithm):
                    model_path = directory / f"{algorithm}.pkl"
                    score = train_classifier([dataset_path], model_path, algorithm=algorithm)
                    self.assertGreaterEqual(score, 0.0)
                    self.assertLessEqual(score, 1.0)
                    self.assertTrue(model_path.is_file())

    def test_unknown_algorithm_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "Algoritmo desconocido"):
            create_model("unknown")

    def test_cli_selects_algorithm_and_keeps_random_forest_as_default(self) -> None:
        for extra_args, expected in (([], "random_forest"), (["--algorithm", "svm"], "svm")):
            with self.subTest(algorithm=expected):
                with patch("sys.argv", ["digital-brain", "train", "model.pkl", "data.csv", *extra_args]):
                    with patch.object(cli, "train_classifier", return_value=0.75) as training:
                        with redirect_stdout(StringIO()):
                            cli.main()
                training.assert_called_once_with(
                    [Path("data.csv")], Path("model.pkl"), algorithm=expected
                )


if __name__ == "__main__":
    unittest.main()
