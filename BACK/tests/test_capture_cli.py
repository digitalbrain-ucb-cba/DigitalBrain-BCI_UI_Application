from contextlib import redirect_stderr
from io import StringIO
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from digital_brain import cli


class CaptureCliTests(unittest.TestCase):
    def test_directory_selects_executable_and_cli_passes_it_to_capture(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            program_directory = Path(temporary_directory)
            executable = program_directory / "ConDump.exe"
            executable.touch()

            self.assertEqual(cli._resolve_capture_executable(program_directory, "ConDump.exe"), executable)
            arguments = ["digital-brain", "capture", str(program_directory), "recordings", "--seconds", "12"]
            with patch("sys.argv", arguments), patch.object(cli, "ExecutableCapture") as capture:
                cli.main()

            capture.assert_called_once_with(executable)
            capture.return_value.capture_to_file.assert_called_once_with(
                Path("recordings"), prefix="captura", total_seconds=12, capture_from=6
            )

    def test_name_can_be_changed_without_moving_the_program(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            program_directory = Path(temporary_directory) / "capturador con espacios"
            program_directory.mkdir()
            executable = program_directory / "ConDumpAuto.exe"
            executable.touch()
            self.assertEqual(cli._resolve_capture_executable(program_directory, "ConDumpAuto.exe"), executable)
            arguments = [
                "digital-brain", "capture", str(program_directory),
                "--executable-name", "ConDumpAuto.exe",
            ]
            with patch("sys.argv", arguments), patch.object(cli, "ExecutableCapture") as capture:
                cli.main()
            capture.assert_called_once_with(executable)

    def test_missing_directory_or_executable_fails_before_capture(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            program_directory = Path(temporary_directory)
            with self.assertRaises(NotADirectoryError):
                cli._resolve_capture_executable(program_directory / "missing", "ConDump.exe")
            with self.assertRaises(FileNotFoundError):
                cli._resolve_capture_executable(program_directory, "ConDump.exe")
            with self.assertRaises(ValueError):
                cli._resolve_capture_executable(program_directory, "../ConDump.exe")

            arguments = ["digital-brain", "capture", str(program_directory / "missing")]
            with patch("sys.argv", arguments), redirect_stderr(StringIO()) as errors:
                with self.assertRaises(SystemExit) as failure:
                    cli.main()
            self.assertEqual(failure.exception.code, 2)
            self.assertIn("No se encontró la carpeta", errors.getvalue())


if __name__ == "__main__":
    unittest.main()
