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

        // Every Ribbon path must use the shared flat, negative-space image renderer.
        string iconRenderer = File.ReadAllText(Path.Combine(directory.FullName, "Core", "FlatRibbonIcons.cs"));
        Require(iconRenderer.Contains("#183248") && iconRenderer.Contains("#B57B56") &&
                iconRenderer.Contains("#F8F2E7"), "Navy/Earth/ivory inverse palette is missing");
        Require(iconRenderer.Contains("RenderTargetBitmap") &&
                iconRenderer.Contains("DrawingVisual"), "Icons must render as sharp vector glyphs");
        Require(source.Contains("footingDropdown.Image = FlatRibbonIcons.Create") &&
                source.Contains("footingDropdown.LargeImage = FlatRibbonIcons.Create"),
                "Footing group icon must be regenerated at both native sizes");
        Require(Regex.Matches(source, @"data\\.Image = FlatRibbonIcons\\.Create\\(id, icon, 16\\);").Count == 3,
                "All three command registration paths must set 16px flat icons");
        Require(Regex.Matches(source, @"data\\.LargeImage = FlatRibbonIcons\\.Create\\(id, icon, 32\\);").Count == 3,
                "All three command registration paths must set 32px flat icons");
        Require(!source.Contains("data.Image = Icon(") &&
                !source.Contains("data.LargeImage = Icon("),
                "Old Ribbon PNG icon pipeline must be fully replaced");
        Console.WriteLine("PASS: All 92 commands and both group images use the flat Navy/Earth icon system.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
