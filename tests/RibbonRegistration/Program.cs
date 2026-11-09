using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal static class Program
{
    private static void Main()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && (!File.Exists(Path.Combine(directory.FullName, "App.cs")) ||
            !File.Exists(Path.Combine(directory.FullName, "KStruct.csproj"))))
            directory = directory.Parent;
        if (directory == null) throw new InvalidOperationException("Cannot locate the KStruct repository root.");

        string source = File.ReadAllText(Path.Combine(directory.FullName, "App.cs"));
        var buttons = Regex.Matches(source, @"\bAdd\(\s*\w+\s*,\s*dll\s*,\s*""(?<id>KStruct_[^""]+)""")
            .Cast<Match>().Select(match => match.Groups["id"].Value).ToList();
        var panels = Regex.Matches(source, @"app\.CreateRibbonPanel\(tab,\s*""(?<panel>[^""]+)""\)")
            .Cast<Match>().Select(match => match.Groups["panel"].Value).ToList();

        Require(buttons.Count == 92, "Expected exactly 92 registered KStruct commands; found " + buttons.Count);
        Require(buttons.Distinct(StringComparer.Ordinal).Count() == buttons.Count, "Duplicate Ribbon command IDs");
        Require(panels.Count == 7, "Expected exactly 7 registered panels; found " + panels.Count);
        Require(panels.Distinct(StringComparer.Ordinal).Count() == panels.Count, "Duplicate Ribbon panels");

        // Footing must occupy exactly one Ribbon surface control with seven drop-down entries.
        Require(Regex.IsMatch(source,
            @"footingRebarPanel\.AddItem\(\s*new PulldownButtonData\(""KStruct_FootingDropdown"""),
            "Footing panel must contain the single pull-down button");
        var footingIds = Regex.Matches(source,
            @"\bAdd\(footingDropdown,\s*dll,\s*""(?<id>KStruct_[^""]+)""")
            .Cast<Match>().Select(match => match.Groups["id"].Value).ToArray();
        string[] expectedFootingIds = {
            "KStruct_FootingRebar", "KStruct_UpdateFootingRebar",
            "KStruct_CreateStripFootingDrawings", "KStruct_UpdateStripFootingDrawings",
            "KStruct_CreateIsolatedFootingSheets", "KStruct_UpdateIsolatedFootingSheets",
            "KStruct_DeleteFoundationDrawingViews"
        };
        Require(footingIds.SequenceEqual(expectedFootingIds),
            "Footing pull-down must preserve seven commands in their original order");
        Require(!Regex.IsMatch(source, @"\bAdd\(footingRebarPanel,\s*dll,"),
            "Footing panel must not show standalone command buttons");

        var removedPanels = new[]
        {
            "KStruct Khối lượng", "KStruct Mô hình", "KStruct Quản lý cấu kiện",
            "KStruct Tải trọng", "KStruct Phân tích"
        };
        foreach (string removed in removedPanels)
            Require(!panels.Contains(removed), "A removed panel was reintroduced: " + removed);

        string[] removedButtons =
        {
            "KStruct_CreateRetainingWallSheets", "KStruct_BeamRebarAxis", "KStruct_CutBeamRebar"
        };
        foreach (string removed in removedButtons)
            Require(!buttons.Contains(removed), "A removed command was reintroduced: " + removed);

        var documentation = Regex.Matches(source,
            @"\bAdd\(nativeDocumentationPanel,\s*dll,\s*""(?<id>KStruct_[^""]+)""")
            .Cast<Match>().Select(match => match.Groups["id"].Value).ToArray();
        Require(documentation.Length == 2, "KStruct Hồ sơ must have two commands");
        Require(documentation.Contains("KStruct_NumberSelectedElements"), "Missing element numbering command");
        Require(documentation.Contains("KStruct_TagElementAtPickedPoint"), "Missing element tagging command");

        Console.WriteLine("PASS: Ribbon registration has 92 unique commands and 7 panels.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
