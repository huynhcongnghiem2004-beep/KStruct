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

        Require(buttons.Count == 95, "Expected exactly 95 registered KStruct commands; found " + buttons.Count);
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
        Require(documentation.Length == 5, "KStruct Hồ sơ must have five commands");
        Require(documentation.Contains("KStruct_NumberSelectedElements"), "Missing element numbering command");
        Require(documentation.Contains("KStruct_TagElementAtPickedPoint"), "Missing element tagging command");
        Require(documentation.Contains("KStruct_AuditModel"), "Missing model audit command");
        Require(documentation.Contains("KStruct_AuditModelV2"), "Missing audit V2 command");
        Require(documentation.Contains("KStruct_V5Workspace"), "Missing V5 workspace Ribbon command");

        // Every Ribbon path must load its lightweight embedded PNG, generated in the shared palette.
        string iconRenderer = File.ReadAllText(Path.Combine(directory.FullName, "Core", "FlatRibbonIcons.cs"));
        string iconGenerator = File.ReadAllText(Path.Combine(directory.FullName, "Resources", "Generate-KStructIcons.ps1"));
        string project = File.ReadAllText(Path.Combine(directory.FullName, "KStruct.csproj"));
        Require(iconRenderer.Contains("BitmapImage") && iconRenderer.Contains("GetManifestResourceNames"),
            "Ribbon icons must load embedded PNG resources");
        Require(iconGenerator.Contains("FromArgb(23, 48, 71)") &&
                iconGenerator.Contains("FromArgb(176, 111, 76)") &&
                iconGenerator.Contains("FromArgb(248, 244, 235)"), "Navy/Earth/ivory PNG palette is missing");
        Require(project.Contains(@"Resources\Ribbon\*.png"), "Per-command Ribbon PNG resources must be embedded");
        string ribbonDirectory = Path.Combine(directory.FullName, "Resources", "Ribbon");
        string[] iconIds = buttons.Concat(new[] { "KStruct_FootingDropdown" }).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string id in iconIds)
            foreach (int size in new[] { 16, 32 })
                Require(File.Exists(Path.Combine(ribbonDirectory, id + "_" + size + ".png")),
                    "Missing " + size + "px Ribbon PNG for " + id);
        Require(Directory.GetFiles(ribbonDirectory, "*.png").Length == iconIds.Length * 2,
            "Each KStruct Ribbon command and the footing dropdown must have exactly two PNG sizes");
        Require(source.Contains("footingDropdown.Image = FlatRibbonIcons.Create") &&
                source.Contains("footingDropdown.LargeImage = FlatRibbonIcons.Create"),
                "Footing group icon must be regenerated at both native sizes");
        Require(source.Split(new[] { "data.Image = FlatRibbonIcons.Create(id, icon, 16);" }, StringSplitOptions.None).Length == 4,
                "All three command registration paths must set 16px flat icons");
        Require(source.Split(new[] { "data.LargeImage = FlatRibbonIcons.Create(id, icon, 32);" }, StringSplitOptions.None).Length == 4,
                "All three command registration paths must set 32px flat icons");
        Require(!source.Contains("data.Image = Icon(") &&
                !source.Contains("data.LargeImage = Icon("),
                "Old Ribbon PNG icon pipeline must be fully replaced");
        string workspace=File.ReadAllText(Path.Combine(directory.FullName,
            "NativeDocumentation","V5StructuralWorkspaceCommand.cs"));
        Require(workspace.Contains("internal const bool Enabled=false;"),
            "Unified release must keep unverified V5 Rebar Pilot disabled");
        Require(workspace.Contains("pilot.IsEnabled=V5PilotReleaseGate.Enabled;"),
            "V5 Pilot must stay disabled in Workspace");
        Require(workspace.Contains("if(!V5PilotReleaseGate.Enabled)"),
            "V5 Pilot command must fail closed even if invoked programmatically");
        Require(source.Contains("typeof(global::ColumnRebarPro.V3.ColumnRebarV3Command)") &&
            source.Contains("typeof(global::SDK_HD.KStruct.NativeRebar.ReinforceStraightBeamsCommand)") &&
            source.Contains("typeof(global::SDK_HD.KStruct.FootingRebar.FootingRebarCommand)"),
            "Unified release must retain V4 column, beam and footing engines");
        Console.WriteLine("PASS: All 95 commands and the footing dropdown use embedded Navy/Earth PNG icons at both sizes.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
