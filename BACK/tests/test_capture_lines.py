from io import StringIO
import unittest

from digital_brain.infrastructure.capture import _CaptureLineWriter, is_complete_sample, parse_sample


class CompleteSampleTests(unittest.TestCase):
    def test_only_four_numbers_are_a_complete_sample(self) -> None:
        self.assertTrue(is_complete_sample("1.5,-2,3e-2,.4\r\n"))
        self.assertTrue(is_complete_sample(" -800, 800 ,0,1"))
        for line in ["35", "5.7904,-103.891", "11.9495,-64.6528,-80.5221", "1,2,3,4,5", "1,2,,4",
                     "Channel A\tB\tC\tD", "Min\t-800\t-800\t-800\t-800", "", "nan,1,2,3", "inf,1,2,3", "1_0,1,2,3"]:
            self.assertFalse(is_complete_sample(line), line)

    def test_parse_sample_requires_exactly_four_values(self) -> None:
        self.assertEqual(parse_sample("1,2,3,4"), (1.0, 2.0, 3.0, 4.0))
        self.assertIsNone(parse_sample("1,2,3,4,5"))
        self.assertIsNone(parse_sample("1,2,3"))


class CaptureLineWriterTests(unittest.TestCase):
    def write(self, chunks: list[tuple[str, float]], capture_start: float = 5, deadline: float = 30) -> tuple[list[str], _CaptureLineWriter]:
        output = StringIO()
        writer = _CaptureLineWriter(output, capture_start, deadline)
        for text, received_at in chunks:
            writer.feed(text, received_at)
        writer.finish()
        return output.getvalue().splitlines(), writer

    def test_line_started_before_capture_start_is_dropped_even_if_it_ends_later(self) -> None:
        # «-12.35,…» empieza a llegar en el segundo 4.9 y termina en el 5.1: antes se guardaba «35,…».
        lines, _ = self.write([("1,1,1,1\r\n-12.", 4.9), ("35,-3.3,-91.3,-103.0\r\n2,2,2,2\r\n", 5.1)])
        self.assertEqual(lines, ["2,2,2,2"])

    def test_lines_split_across_chunks_are_joined(self) -> None:
        lines, _ = self.write([("1.5,2", 6), (".5,3,", 6.1), ("4\r\n", 6.2)])
        self.assertEqual(lines, ["1.5,2.5,3,4"])

    def test_incomplete_and_header_lines_inside_the_window_are_rejected(self) -> None:
        lines, writer = self.write([("Channel A\tB\tC\tD\r\n\r\n5.79,-103.8\r\n1,2,3\r\n9,9,9,9\r\n", 6)])
        self.assertEqual(lines, ["9,9,9,9"])
        self.assertEqual(writer.rejected, 3)
        self.assertEqual(writer.written, 1)

    def test_trailing_line_without_newline_or_after_deadline_is_dropped(self) -> None:
        lines, writer = self.write([("1,1,1,1\r\n11.7,12.1,-19.0,-3.4", 29.9), ("81\r\n", 30.1)])
        self.assertEqual(lines, ["1,1,1,1"])
        lines, writer = self.write([("1,1,1,1\r\n2,2,2,", 29.9)])
        self.assertEqual(lines, ["1,1,1,1"])
        self.assertEqual(writer.rejected, 1)

    def test_realistic_stream_split_in_fixed_blocks_keeps_only_whole_lines(self) -> None:
        source = "".join(f"{i * 0.37 - 20:.5f},{-i * 1.1:.4f},{i % 97 - 48.25:.3f},{(i * 7) % 801 - 400:.6f}\r\n" for i in range(3000))
        data = "Channel A\tB\tC\tD\r\nMin\t-800\t-800\t-800\t-800\r\n" + source
        chunks = [(data[i:i + 4096], 0.01 * (i // 4096)) for i in range(0, len(data), 4096)]
        lines, writer = self.write(chunks, capture_start=0.03, deadline=0.2)
        self.assertGreater(len(lines), 100)
        self.assertTrue(all(is_complete_sample(line) for line in lines))
        expected = {line.strip() for line in source.splitlines()}
        self.assertTrue(set(lines) <= expected)
        # Son líneas consecutivas del original: no falta ninguna en medio.
        original = [line.strip() for line in source.splitlines()]
        start = original.index(lines[0])
        self.assertEqual(lines, original[start:start + len(lines)])


if __name__ == "__main__":
    unittest.main()
