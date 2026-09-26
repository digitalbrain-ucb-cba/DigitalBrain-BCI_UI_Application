"""Puntos de entrada explícitos para los casos de uso."""

import argparse
from pathlib import Path
from digital_brain.application.dataset import build_dataset
from digital_brain.application.random_forest import train_classifier
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
    capture_command = commands.add_parser("capture", help="Ejecuta un capturador externo y guarda su salida.")
    capture_command.add_argument(
        "executable_path",
        type=Path,
        help="Ruta del ejecutable. Si indicas solo el nombre, se busca en assets/executables/.",
    )
    capture_command.add_argument("output_directory", type=Path, nargs="?", default=Path("datos_capturados"))
    capture_command.add_argument("--prefix", default="captura")
    capture_command.add_argument("--seconds", type=float, default=30)
    capture_command.add_argument("--capture-from", type=float, default=6)
    arguments = parser.parse_args()
    if arguments.command == "build-dataset":
        dataset = build_dataset(arguments.input_directory, arguments.output_path, arguments.label, SpectralFeatureExtractor())
        print(f"Dataset creado: {arguments.output_path} ({len(dataset)} filas)")
    elif arguments.command == "capture":
        ExecutableCapture(_resolve_executable_path(arguments.executable_path)).capture_to_file(
            arguments.output_directory,
            prefix=arguments.prefix,
            total_seconds=arguments.seconds,
            capture_from=arguments.capture_from,
        )
    else:
        score = train_classifier(arguments.datasets, arguments.model_path)
        print(f"Modelo guardado en {arguments.model_path}; exactitud: {score:.3f}")


def _resolve_executable_path(executable_path: Path) -> Path:
    """Resuelve nombres simples de ejecutable desde la carpeta dedicada del proyecto."""
    if executable_path.is_file():
        return executable_path
    if executable_path.name == str(executable_path):
        project_root = Path(__file__).resolve().parents[2]
        dedicated_path = project_root / "assets" / "executables" / executable_path
        if dedicated_path.is_file():
            return dedicated_path
    return executable_path


if __name__ == "__main__":
    main()
