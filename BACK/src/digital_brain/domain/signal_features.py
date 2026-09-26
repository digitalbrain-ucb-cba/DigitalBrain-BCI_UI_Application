"""Extracción determinista de características espectrales."""

from collections.abc import Sequence

import numpy as np

CHANNEL_NAMES = ("A", "B", "C", "D")
FREQUENCY_BANDS = {"theta": (4, 8), "alpha": (8, 13), "beta": (13, 30), "gamma": (30, None)}
FEATURE_COLUMNS = tuple(f"{band}_{channel}" for band in FREQUENCY_BANDS for channel in CHANNEL_NAMES)


class InvalidSignalWindowError(ValueError):
    """La ventana no tiene los cuatro canales y tamaño requerido."""

    
class SpectralFeatureExtractor:
    """Calcula potencias por banda para cuatro canales de señal."""

    def __init__(self, sample_rate_hz: float = 125.0, window_size: int = 125) -> None:
        if sample_rate_hz <= 0 or window_size <= 0:
            raise ValueError("La frecuencia de muestreo y la ventana deben ser positivas.")
        self.sample_rate_hz = sample_rate_hz
        self.window_size = window_size
        self._frequencies = np.fft.fftfreq(window_size, d=1 / sample_rate_hz)

    def extract(self, channels: Sequence[Sequence[float]]) -> dict[str, float]:
        self._validate_window(channels)
        features: dict[str, float] = {}
        spectra = [
            np.abs(np.fft.fft(np.asarray(samples, dtype=float))) ** 2
            for samples in channels
        ]
        for band_name, (lower, upper) in FREQUENCY_BANDS.items():
            for channel_name, spectrum in zip(CHANNEL_NAMES, spectra, strict=True):
                mask = self._frequencies >= lower
                if upper is not None:
                    mask &= self._frequencies < upper
                features[f"{band_name}_{channel_name}"] = float(np.sum(spectrum[mask]))
        return features

    def _validate_window(self, channels: Sequence[Sequence[float]]) -> None:
        if len(channels) != len(CHANNEL_NAMES):
            raise InvalidSignalWindowError("Se requieren exactamente cuatro canales.")
        if any(len(channel) != self.window_size for channel in channels):
            raise InvalidSignalWindowError(f"Cada canal debe tener {self.window_size} muestras.")
