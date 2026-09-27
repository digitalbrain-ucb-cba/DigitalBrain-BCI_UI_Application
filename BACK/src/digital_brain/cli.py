"""Puntos de entrada explícitos para los casos de uso."""

import argparse
from pathlib import Path
from digital_brain.application.dataset import build_dataset
from digital_brain.application.models import MODEL_NAMES
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
    else:
        score = train_classifier(arguments.datasets, arguments.model_path, algorithm=arguments.algorithm)
        print(f"Modelo {arguments.algorithm} guardado en {arguments.model_path}; exactitud: {score:.3f}")


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
