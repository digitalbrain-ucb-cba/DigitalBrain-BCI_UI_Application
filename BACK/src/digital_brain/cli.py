"""Puntos de entrada explícitos para los casos de uso."""

import argparse
import os
from pathlib import Path
import sys
from threading import Thread
from digital_brain.application.dataset import build_dataset
from digital_brain.application.models import MODEL_NAMES
from digital_brain.application.realtime import RealtimeClassifier
from digital_brain.application.training import train_classifier
from digital_brain.domain.signal_features import SpectralFeatureExtractor
from digital_brain.infrastructure.capture import ExecutableCapture


def main() -> None:
    parser = argparse.ArgumentParser(prog="digital-brain")
    commands = parser.add_subparsers(dest="command", required=True)
    dataset_command = commands.add_parser("build-dataset")
    dataset_command.add_argument("input_directory", type=Path)
    dataset_command.add_argument("output_path", type=Path)
    dataset_command.add_argument("label")
    train_command = commands.add_parser("train")
    train_command.add_argument("model_path", type=Path)
    train_command.add_argument("datasets", type=Path, nargs="+")
    train_command.add_argument("--algorithm", choices=MODEL_NAMES, default="random_forest")
    capture_command = commands.add_parser("capture", help="Ejecuta un capturador desde su carpeta externa y guarda su salida.")
    capture_command.add_argument(
        "program_directory",
        type=Path,
        help="Carpeta externa que contiene el ejecutable y sus archivos auxiliares.",
    )
    capture_command.add_argument("output_directory", type=Path, nargs="?", default=Path("datos_capturados"))
    capture_command.add_argument(
        "--executable-name",
        default="ConDump.exe",
        help="Nombre del ejecutable dentro de la carpeta (por defecto: ConDump.exe).",
    )
    capture_command.add_argument("--prefix", default="captura")
    capture_command.add_argument("--seconds", type=float, default=30)
    capture_command.add_argument("--capture-from", type=float, default=6)
    predict_command = commands.add_parser("predict", help="Clasifica en vivo la salida del capturador hasta detenerlo con Ctrl+C.")
    predict_command.add_argument(
        "program_directory",
        type=Path,
        help="Carpeta externa que contiene el ejecutable y sus archivos auxiliares.",
    )
    predict_command.add_argument("model_path", type=Path)
    predict_command.add_argument(
        "--executable-name",
        default="ConDumpAuto.exe",
        help="Nombre del ejecutable dentro de la carpeta (por defecto: ConDumpAuto.exe).",
    )
    arguments = parser.parse_args()
    if arguments.command == "build-dataset":
        dataset = build_dataset(arguments.input_directory, arguments.output_path, arguments.label, SpectralFeatureExtractor())
        print(f"Dataset creado: {arguments.output_path} ({len(dataset)} filas)")
    elif arguments.command == "capture":
        try:
            executable_path = _resolve_capture_executable(arguments.program_directory, arguments.executable_name)
        except (NotADirectoryError, FileNotFoundError, ValueError) as error:
            capture_command.error(str(error))
        ExecutableCapture(executable_path).capture_to_file(
            arguments.output_directory,
            prefix=arguments.prefix,
            total_seconds=arguments.seconds,
            capture_from=arguments.capture_from,
        )
    elif arguments.command == "predict":
        try:
            executable_path = _resolve_capture_executable(arguments.program_directory, arguments.executable_name)
            classifier = RealtimeClassifier(arguments.model_path, SpectralFeatureExtractor())
        except (NotADirectoryError, FileNotFoundError, ValueError) as error:
            predict_command.error(str(error))
        try:
            capture = ExecutableCapture(executable_path)
            Thread(target=_stop_when_requested, args=(capture,), name="parada", daemon=True).start()
            samples = capture.stream_samples(lambda message: print(message, flush=True))
            for label, confidence in classifier.predict_windows(samples):
                detail = "" if confidence is None else f" | confianza: {confidence:.1%}"
                print(f"Predicción: {label}{detail}", flush=True)
        except KeyboardInterrupt:
            pass
        except RuntimeError as error:
            predict_command.exit(1, f"{error}\n")
        print("Predicción detenida.", flush=True)
    else:
        score = train_classifier(arguments.datasets, arguments.model_path, algorithm=arguments.algorithm)
        print(f"Modelo {arguments.algorithm} guardado en {arguments.model_path}; exactitud: {score:.3f}")


def _stop_when_requested(capture: ExecutableCapture) -> None:
    """Escribir «stop» por stdin detiene la predicción dejando que el capturador libere el casco.

    Se lee del descriptor y no de ``sys.stdin``: este hilo sigue bloqueado al salir, y hacerlo sobre
    el búfer de ``sys.stdin`` puede abortar el intérprete.
    """
    try:
        while data := os.read(sys.stdin.fileno(), 64):
            if b"stop" in data:
                capture.stop()
                return
    except (AttributeError, OSError, ValueError):
        return  # Sin stdin utilizable solo queda Ctrl+C.


def _resolve_capture_executable(program_directory: Path, executable_name: str) -> Path:
    """Localiza el programa en una instalación externa completa."""
    program_directory = program_directory.expanduser().resolve()
    if not program_directory.is_dir():
        raise NotADirectoryError(f"No se encontró la carpeta del capturador: {program_directory}")
    if not executable_name or Path(executable_name).name != executable_name:
        raise ValueError("--executable-name debe ser solo el nombre de un archivo.")
    executable_path = program_directory / executable_name
    if not executable_path.is_file():
        raise FileNotFoundError(f"No se encontró {executable_name} en: {program_directory}")
    return executable_path


if __name__ == "__main__":
    main()
