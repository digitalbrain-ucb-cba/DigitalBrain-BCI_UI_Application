from contextlib import redirect_stdout
from io import StringIO
import pickle
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier

from digital_brain import cli
from digital_brain.application.dataset import build_dataset
from digital_brain.application.realtime import Prediction, RealtimeClassifier
from digital_brain.application.training import train_classifier
from digital_brain.domain.signal_features import FEATURE_COLUMNS, SCRIPT_FEATURE_COLUMNS, SpectralFeatureExtractor
from digital_brain.infrastructure.capture import ExecutableCapture

SCRIPT_COLUMNS = tuple(SCRIPT_FEATURE_COLUMNS)


def script_features(window: np.ndarray) -> list[float]:
    """Mismo cálculo de DatosACSVNew.py para una ventana de 125 muestras por 4 canales."""
    hz = np.fft.fftfreq(125, 1 / 125)
    powers = [np.abs(np.fft.fft(window[:, channel])) ** 2 for channel in range(4)]
    masks = [(hz >= 4) & (hz < 8), (hz >= 8) & (hz < 13), (hz >= 13) & (hz < 30), hz >= 30]
    return [float(np.sum(power[mask])) for mask in masks for power in powers]


def signal(frequency: float, samples: int) -> list[tuple[float, float, float, float]]:
    time = np.arange(samples) / 125
    wave = 50 * np.sin(2 * np.pi * frequency * time)
    return [(float(value), float(value) / 2, float(-value), 1.0) for value in wave]


def save_model(path: Path, columns: tuple[str, ...]) -> None:
    extractor = SpectralFeatureExtractor()
    rows, labels = [], []
    for label, frequency in (("lento", 6), ("rapido", 20)):
        for offset in range(5):
            window = signal(frequency + offset / 10, 125)
            features = extractor.extract([list(channel) for channel in zip(*window)])
            rows.append(list(features.values()))
            labels.append(label)
    model = RandomForestClassifier(n_estimators=10, random_state=4)
    model.fit(pd.DataFrame(rows, columns=columns), labels)
    with path.open("wb") as model_file:
        pickle.dump(model, model_file)


class ScriptParityTests(unittest.TestCase):
    def test_features_match_the_script_calculation_in_the_same_order(self) -> None:
        window = np.random.default_rng(7).normal(size=(125, 4)) * 40
        features = SpectralFeatureExtractor().extract(window.T.tolist())
        np.testing.assert_allclose(list(features.values()), script_features(window))
        self.assertEqual([SCRIPT_FEATURE_COLUMNS[name] for name in SCRIPT_COLUMNS], list(FEATURE_COLUMNS))
        self.assertEqual(SCRIPT_COLUMNS[:5], ("ThetaA", "ThetaB", "ThetaC", "ThetaD", "AlphaA"))

    def test_default_step_is_the_script_window_advance(self) -> None:
        self.assertEqual(SpectralFeatureExtractor().default_step, 63)

    def test_dataset_advances_63_samples_and_drops_saturated_lines(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            lines = ["Channel A\tB\tC\tD", "-800,799.976,-800,-800"]
            lines += [f"{index},1,2,3" for index in range(251)]
            (directory / "captura.txt").write_text("\n".join(lines), encoding="utf-8")
            dataset = build_dataset(directory, directory / "out.csv", "x", SpectralFeatureExtractor())
        self.assertEqual(len(dataset), 3)

    def test_training_accepts_csv_written_by_the_scripts_for_each_movement(self) -> None:
        generator = np.random.default_rng(7)
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            paths = []
            for label, offset in (("BrazoDerecho", 0.0), ("BrazoIzquierdo", 5.0)):
                frame = pd.DataFrame(generator.random((20, 16)) + offset, columns=SCRIPT_COLUMNS)
                frame["label"] = label
                paths.append(directory / f"{label}.csv")
                frame.to_csv(paths[-1])  # con índice, igual que DatosACSVNew.py
            score = train_classifier(paths, directory / "model.pkl")
        self.assertEqual(score, 1.0)


class RealtimeClassifierTests(unittest.TestCase):
    def classify(self, columns: tuple[str, ...], samples: list[tuple[float, float, float, float]]) -> list[Prediction]:
        with tempfile.TemporaryDirectory() as temporary_directory:
            model_path = Path(temporary_directory) / "model.pkl"
            save_model(model_path, columns)
            classifier = RealtimeClassifier(model_path, SpectralFeatureExtractor())
        return list(classifier.predict_windows(iter(samples)))

    def test_models_from_the_project_and_from_the_scripts_give_the_same_decisions(self) -> None:
        samples = signal(6, 125) + signal(20, 251)
        project = self.classify(FEATURE_COLUMNS, samples)
        self.assertEqual(project, self.classify(SCRIPT_COLUMNS, samples))
        self.assertEqual(project[0].label, "lento")
        self.assertEqual(project[-1].label, "rapido")

    def test_confidence_is_the_probability_of_the_predicted_class(self) -> None:
        for prediction in self.classify(FEATURE_COLUMNS, signal(6, 125) + signal(20, 251)):
            self.assertGreaterEqual(prediction.confidence, 0.5)
            self.assertLessEqual(prediction.confidence, 1.0)

    def test_confidence_belongs_to_the_label_even_if_another_class_is_more_likely(self) -> None:
        class Disagrees:
            classes_ = np.array(["a", "b"])
            feature_names_in_ = np.array(FEATURE_COLUMNS)

            def predict(self, _row: pd.DataFrame) -> np.ndarray:
                return np.array(["a"])

            def predict_proba(self, _row: pd.DataFrame) -> np.ndarray:
                return np.array([[0.4, 0.6]])

        self.assertEqual(self.classify_with(Disagrees()), [Prediction("a", 0.4)])

    def test_model_without_probabilities_still_predicts(self) -> None:
        class OnlyPredict:
            feature_names_in_ = np.array(FEATURE_COLUMNS)

            def predict(self, _row: pd.DataFrame) -> np.ndarray:
                return np.array(["a"])

        self.assertEqual(self.classify_with(OnlyPredict()), [Prediction("a", None)])

    def classify_with(self, model: object) -> list[Prediction]:
        with tempfile.TemporaryDirectory() as temporary_directory:
            model_path = Path(temporary_directory) / "model.pkl"
            model_path.touch()
            with patch("pickle.load", return_value=model):
                classifier = RealtimeClassifier(model_path, SpectralFeatureExtractor())
        return list(classifier.predict_windows(iter(signal(6, 125))))

    def test_first_decision_needs_a_full_window_and_then_one_every_63_samples(self) -> None:
        self.assertEqual(len(self.classify(FEATURE_COLUMNS, signal(6, 124))), 0)
        self.assertEqual(len(self.classify(FEATURE_COLUMNS, signal(6, 125))), 1)
        self.assertEqual(len(self.classify(FEATURE_COLUMNS, signal(6, 187))), 1)
        self.assertEqual(len(self.classify(FEATURE_COLUMNS, signal(6, 188))), 2)

    def test_unknown_model_columns_are_rejected_when_loading(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            model_path = Path(temporary_directory) / "model.pkl"
            save_model(model_path, tuple(f"x{index}" for index in range(16)))
            with self.assertRaisesRegex(ValueError, "columnas desconocidas"):
                RealtimeClassifier(model_path, SpectralFeatureExtractor())


class FakeCapturer:
    """Sustituye ConDumpAuto.exe por un proceso real que imprime ``lines`` y termina."""

    def __init__(self, directory: Path, lines: list[str]) -> None:
        self.executable = directory / "ConDumpAuto.exe"
        self.executable.touch()
        self.script = directory / "fake_capturer.py"
        self.script.write_text(f"print({chr(10).join(lines)!r}, flush=True)\n", encoding="utf-8")
        self.launches = 0
        self._popen = subprocess.Popen

    def __call__(self, command: list[str], **options: object) -> subprocess.Popen[bytes]:
        self.launches += 1
        return self._popen([sys.executable, str(self.script)], **options)


class StreamSamplesTests(unittest.TestCase):
    def test_streams_valid_samples_reports_status_and_relaunches_when_the_program_ends(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            fake = FakeCapturer(Path(temporary_directory), ["I(0) LINK: Link CONNECTED", "-800,799.976,-800,-800", "1.5,2,3,4", "1,2,3"])
            messages: list[str] = []
            with patch("subprocess.Popen", fake):
                stream = ExecutableCapture(fake.executable).stream_samples(messages.append)
                samples = [next(stream), next(stream)]
                stream.close()
        self.assertEqual(samples, [(1.5, 2.0, 3.0, 4.0)] * 2)
        self.assertEqual(fake.launches, 2)
        self.assertEqual(messages.count("I(0) LINK: Link CONNECTED"), 2)
        self.assertEqual(messages.count("1,2,3"), 1)  # el segundo lanzamiento se cerró antes de llegar a esa línea
        self.assertNotIn("-800,799.976,-800,-800", messages)

    def test_stops_instead_of_relaunching_forever_when_nothing_valid_arrives(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            fake = FakeCapturer(Path(temporary_directory), ["Connecting to device. Please wait..."])
            with patch("subprocess.Popen", fake):
                with self.assertRaisesRegex(RuntimeError, "sin muestras válidas"):
                    list(ExecutableCapture(fake.executable).stream_samples(lambda _: None))
        self.assertEqual(fake.launches, 1)


class PredictCliTests(unittest.TestCase):
    def test_predict_prints_one_labelled_line_per_window_until_interrupted(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            model_path = directory / "model.pkl"
            save_model(model_path, SCRIPT_COLUMNS)
            (directory / "ConDumpAuto.exe").touch()

            def stream(_self: ExecutableCapture, _on_message: object) -> object:
                yield from signal(6, 188)
                raise KeyboardInterrupt

            arguments = ["digital-brain", "predict", str(directory), str(model_path)]
            with patch("sys.argv", arguments), patch.object(ExecutableCapture, "stream_samples", stream):
                with redirect_stdout(StringIO()) as output:
                    cli.main()
        lines = output.getvalue().splitlines()
        self.assertEqual(len(lines), 3)
        for line in lines[:2]:
            self.assertRegex(line, r"^Predicción: lento \| confianza: \d+\.\d%$")
        self.assertEqual(lines[2], "Predicción detenida.")

    def test_missing_model_fails_before_starting_the_capturer(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            directory = Path(temporary_directory)
            (directory / "ConDumpAuto.exe").touch()
            arguments = ["digital-brain", "predict", str(directory), str(directory / "missing.pkl")]
            with patch("sys.argv", arguments), patch("subprocess.Popen") as popen:
                with patch("sys.stderr", StringIO()) as errors, self.assertRaises(SystemExit):
                    cli.main()
            popen.assert_not_called()
            self.assertIn("No se encontró el modelo", errors.getvalue())


if __name__ == "__main__":
    unittest.main()
