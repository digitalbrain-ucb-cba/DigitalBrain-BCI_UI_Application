"""Adaptadores de entrada para el capturador externo y sus archivos."""

from collections.abc import Iterator
import codecs
from pathlib import Path
import queue
import re
import subprocess
import sys
from threading import Thread
import time
from typing import TextIO


_NUMBER = re.compile(r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?")


def is_complete_sample(line: str) -> bool:
    """Una línea de señal válida tiene exactamente cuatro números separados por comas (canales A–D)."""
    values = line.strip().split(",")
    return len(values) == 4 and all(_NUMBER.fullmatch(value.strip()) for value in values)


def parse_sample(line: str) -> tuple[float, float, float, float] | None:
    if not is_complete_sample(line):
        return None
    sample = tuple(float(value) for value in line.strip().split(","))
    if any(value in {-800, 800} for value in sample):
        return None
    return sample


class _CaptureLineWriter:
    """Guarda solo líneas completas, recibidas enteras dentro del tramo de captura y con cuatro valores.

    La salida del capturador llega en bloques que cortan líneas por la mitad: escribir bloques tal cual dejaba
    al principio del archivo el final de una línea (uno a tres valores, o un primer número truncado) y al final
    una línea a medias. Una línea se descarta si empezó a llegar antes de ``capture_start`` o terminó después de
    ``deadline``; dentro del tramo, se descarta si no es una muestra completa (cabeceras, vacías, cortadas).
    """

    def __init__(self, output_file: TextIO, capture_start: float, deadline: float) -> None:
        self._output_file = output_file
        self._capture_start = capture_start
        self._deadline = deadline
        self._partial = ""
        self._partial_started_at: float | None = None
        self.written = 0
        self.rejected = 0

    def feed(self, text: str, received_at: float) -> None:
        while text:
            newline = text.find("\n")
            if newline < 0:
                if self._partial_started_at is None:
                    self._partial_started_at = received_at
                self._partial += text
                return
            started_at = self._partial_started_at if self._partial_started_at is not None else received_at
            line = self._partial + text[:newline]
            self._partial = ""
            self._partial_started_at = None
            text = text[newline + 1:]
            self._consider(line, started_at, received_at)

    def finish(self) -> None:
        """La última línea sin salto de línea puede estar cortada: no se guarda."""
        started_at = self._partial_started_at
        if self._partial.strip() and started_at is not None and self._capture_start <= started_at < self._deadline:
            self.rejected += 1
        self._partial = ""
        self._partial_started_at = None

    def _consider(self, line: str, started_at: float, ended_at: float) -> None:
        if started_at < self._capture_start or ended_at >= self._deadline:
            return
        if not is_complete_sample(line):
            if line.strip():
                self.rejected += 1
            return
        self._output_file.write(line.strip() + "\n")
        self.written += 1


def read_samples(file_path: Path) -> Iterator[tuple[float, float, float, float]]:
    with file_path.open(encoding="utf-8", errors="replace") as capture_file:
        for line in capture_file:
            sample = parse_sample(line)
            if sample is not None:
                yield sample


class ExecutableCapture:

    def __init__(self, executable_path: Path) -> None:
        self.executable_path = Path(executable_path).resolve()

    def capture_to_file(
        self,
        output_directory: Path = Path("datos_capturados"),
        prefix: str = "captura",
        total_seconds: float = 30,
        capture_from: float = 6,
    ) -> Path:
        if not self.executable_path.is_file():
            raise FileNotFoundError(f"No se encontró el ejecutable: {self.executable_path}")
        if total_seconds <= 0:
            raise ValueError("La duración total debe ser mayor que cero.")
        if capture_from < 0 or capture_from >= total_seconds:
            raise ValueError("El inicio de captura debe estar entre cero y la duración total.")

        output_directory = Path(output_directory)
        output_directory.mkdir(parents=True, exist_ok=True)
        output_path = _next_capture_path(output_directory, prefix)
        output_queue: queue.Queue[tuple[float, bytes] | None] = queue.Queue()

        print(f"Ejecutando: {self.executable_path}")
        print(f"Duración total: {total_seconds:g} segundos")
        print(f"Guardando la salida desde el segundo {capture_from:g}")
        print(f"Archivo de salida: {output_path}\n")

        process = subprocess.Popen(
            [str(self.executable_path)],
            cwd=str(self.executable_path.parent),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            stdin=subprocess.PIPE,
            shell=False,
            bufsize=4096,
        )
        if process.stdout is None:
            process.kill()
            raise RuntimeError("El ejecutable no proporcionó salida estándar.")

        started_at = time.monotonic()
        deadline = started_at + total_seconds
        capture_start = started_at + capture_from

        def read_output() -> None:
            try:
                while True:
                    chunk = process.stdout.read1(4096)
                    if not chunk:
                        break
                    output_queue.put((time.monotonic(), chunk))
            finally:
                output_queue.put(None)

        reader = Thread(target=read_output, name="captura-salida", daemon=True)
        reader.start()

        try:
            with output_path.open("x", encoding="utf-8", errors="replace", newline="") as output_file:
                decoder = codecs.getincrementaldecoder("utf-8")(errors="replace")
                writer = _CaptureLineWriter(output_file, capture_start, deadline)
                finished_reading = False
                while time.monotonic() < deadline and not finished_reading:
                    try:
                        item = output_queue.get(timeout=min(0.1, max(0, deadline - time.monotonic())))
                    except queue.Empty:
                        continue
                    if item is None:
                        finished_reading = True
                        continue
                    received_at, chunk = item
                    text = decoder.decode(chunk)
                    sys.stdout.write(text)
                    sys.stdout.flush()
                    writer.feed(text, received_at)
                    output_file.flush()

                if process.poll() is None:
                    try:
                        if process.stdin is not None:
                            process.stdin.write(b"stop\n")
                            process.stdin.flush()
                        process.wait(timeout=3)
                    except (BrokenPipeError, OSError, subprocess.TimeoutExpired):
                        process.terminate()
                        try:
                            process.wait(timeout=1)
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait()

                reader.join()
                while True:
                    try:
                        item = output_queue.get_nowait()
                    except queue.Empty:
                        break
                    if item is None:
                        continue
                    received_at, chunk = item
                    text = decoder.decode(chunk)
                    sys.stdout.write(text)
                    sys.stdout.flush()
                    writer.feed(text, received_at)
                remaining_text = decoder.decode(b"", final=True)
                if remaining_text:
                    sys.stdout.write(remaining_text)
                    sys.stdout.flush()
                    writer.feed(remaining_text, time.monotonic())
                writer.finish()
                output_file.flush()
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()
            if process.stdin is not None:
                process.stdin.close()
            process.stdout.close()

        print("\nTiempo cumplido. Programa detenido.")
        print(f"Líneas guardadas: {writer.written}; descartadas por estar incompletas o mal formadas: {writer.rejected}")
        print(f"Datos guardados en: {output_path}")
        return output_path


def _next_capture_path(output_directory: Path, prefix: str, extension: str = ".txt") -> Path:
    number = 1
    while True:
        candidate = output_directory / f"{prefix}_{number}{extension}"
        if not candidate.exists():
            return candidate
        number += 1
