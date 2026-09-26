import unittest

from digital_brain.domain.signal_features import FEATURE_COLUMNS, InvalidSignalWindowError, SpectralFeatureExtractor
from digital_brain.infrastructure.capture import parse_sample


class SpectralFeatureExtractorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.extractor = SpectralFeatureExtractor(window_size=8, sample_rate_hz=8)

    def test_extract_returns_all_named_features(self) -> None:
        features = self.extractor.extract([[1.0] * 8 for _ in range(4)])
        self.assertEqual(tuple(features), FEATURE_COLUMNS)

    def test_extract_rejects_incomplete_window(self) -> None:
        with self.assertRaises(InvalidSignalWindowError):
            self.extractor.extract([[1.0] * 7 for _ in range(4)])


class CaptureParserTests(unittest.TestCase):
    def test_parse_sample_discards_invalid_and_sentinel_samples(self) -> None:
        self.assertEqual(parse_sample("1,2,3,4\n"), (1.0, 2.0, 3.0, 4.0))
        self.assertIsNone(parse_sample("-800,2,3,4"))
        self.assertIsNone(parse_sample("encabezado"))


if __name__ == "__main__":
    unittest.main()
