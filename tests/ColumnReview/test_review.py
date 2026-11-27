import importlib.util
import math
import pathlib
import unittest
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
PATH = ROOT / "DevTools/KStructDev.extension/lib/column_review_math.py"
spec = importlib.util.spec_from_file_location("column_review_math", PATH)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

class ProjectionTests(unittest.TestCase):
    def test_fits_and_preserves_aspect(self):
        mapped = mod.fit_projection([[(0, 0), (4, 2)], [(2, 1)]], 200, 120, 10)
        self.assertEqual(len(mapped), 2)
        a, b = mapped[0]
        self.assertAlmostEqual((b[0] - a[0]) / (a[1] - b[1]), 2)
        self.assertTrue(all(10 <= x <= 190 and 10 <= y <= 110 for line in mapped for x, y in line))
        self.assertAlmostEqual(mapped[1][0][0], 100)
        self.assertAlmostEqual(mapped[1][0][1], 60)

    def test_point_and_line_stay_finite(self):
        for lines in ([[(5, 5)]], [[(2, 0), (2, 20)]], [[(1, 3), (9, 3)]]):
            mapped = mod.fit_projection(lines, 200, 120)
            self.assertTrue(all(math.isfinite(v) for line in mapped for point in line for v in point))

    def test_empty_and_small_view(self):
        self.assertEqual(mod.fit_projection([], 200, 100), [])
        self.assertEqual(mod.fit_projection([[(1, 2)]], 30, 30), [])

    def test_xaml_controls_and_click_handlers_exist(self):
        bundle = ROOT / "DevTools/KStructDev.extension/KStruct Dev.tab/Diagnostics.panel/Column Review.pushbutton"
        root = ET.parse(bundle / "ColumnReview.xaml").getroot()
        xname = "{http://schemas.microsoft.com/winfx/2006/xaml}Name"
        names = {node.attrib[xname] for node in root.iter() if xname in node.attrib}
        self.assertTrue({"HostText", "BarGrid", "PlanCanvas", "PreviewText", "SummaryText"} <= names)
        source = (bundle / "script.py").read_text(encoding="utf-8-sig")
        for node in root.iter():
            for name in ("Click", "SizeChanged"):
                if name in node.attrib:
                    self.assertIn("def " + node.attrib[name] + "(", source)

if __name__ == "__main__":
    unittest.main()
