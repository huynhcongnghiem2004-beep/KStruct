#!/usr/bin/env python3
"""Static safety checks for the KStruct Column V4 WPF four-tab migration.

Run from repository root: python tests/check_column_v4_ui.py
Does not replace a Windows/MSBuild or Revit 2024 runtime test.
"""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
XAML = ROOT / "ColumnRebarPro/V3/ColumnRebarV3Window.xaml"
CS = ROOT / "ColumnRebarPro/V3/ColumnRebarV3Window.xaml.cs"
ns = {"w": "http://schemas.microsoft.com/winfx/2006/xaml/presentation"}
xns = "{http://schemas.microsoft.com/winfx/2006/xaml}Name"

def main():
    tree = ET.parse(XAML)
    root = tree.getroot()
    text = XAML.read_text(encoding="utf-8")
    code = CS.read_text(encoding="utf-8")
    main = next(t for t in root.iter(f"{{{ns['w']}}}TabControl") if t.get(xns) == "MainTabs")
    tabs = [t.attrib.get("Header", "") for t in main.findall("w:TabItem", ns)]
    expected = [
        "1. Hình học & bố trí thép",
        "2. Thép đai",
        "3. Thép móng & nối tầng",
        "4. Cài đặt chung & bản vẽ",
    ]
    assert tabs == expected, f"Unexpected tab order: {tabs}"
    names = [e.attrib[xns] for e in root.iter() if xns in e.attrib]
    assert len(names) == len(set(names)), "Duplicate WPF x:Name"
    assert "DeploymentHost" in names, "Existing drawing deployment host lost"
    assert "MainTabs.SelectedIndex = caption.Contains(\"Hook\") ? 1 :" in code
    assert "caption.Contains(\"Neo\") ? 2 : 0;" in code
    for key in ("KStructNavy", "KStructEarth", "KStructCream"):
        assert key in text, f"Missing color resource: {key}"
    handlers = set(re.findall(
        r'\b(?:Click|Checked|Unchecked|SelectionChanged|LostFocus|SizeChanged)="([A-Za-z_]\w*)"',
        text))
    missing = sorted(h for h in handlers if not re.search(r'\b' + re.escape(h) + r'\s*\(', code))
    assert not missing, f"XAML handlers missing from code-behind: {missing}"
    print("PASS: 4 tab order, unique names, existing deployment host, routing, palette, handler references")
    print("NOT VERIFIED: .NET compilation, actual Revit transactions, geometry, visual parity, V5 integration")

if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ET.ParseError, OSError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        sys.exit(1)
