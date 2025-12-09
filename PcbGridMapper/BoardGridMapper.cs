using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using CsvHelper.TypeConversion;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;

public class BoardGridMapper
{
    private readonly double BoardWidth;
    private readonly double BoardHeight;
    private readonly int GridRows;
    private readonly int GridCols;

    // --- NEW CONSTANT: Defines the resolution of the subdivision within each primary grid cell.
    private const int SecondaryGridRes = 3;

    public BoardGridMapper(double width, double height, int rows = 4, int cols = 4)
    {
        BoardWidth = width;
        BoardHeight = height;
        GridRows = rows;
        GridCols = cols;
    }

    private readonly Dictionary<string, ComponentData> componentMap = new Dictionary<string, ComponentData>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<string>> primaryZoneComponentMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    private class UnitStrippingDoubleConverter : DoubleConverter
    {
        public override object ConvertFromString(string text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0.0;
            }

            string cleanedText = text.Trim();
            cleanedText = cleanedText.Replace("mm", "", StringComparison.OrdinalIgnoreCase);
            cleanedText = cleanedText = cleanedText.Replace("mil", "", StringComparison.OrdinalIgnoreCase);

            return base.ConvertFromString(cleanedText, row, memberMapData);
        }
    }
    private (int headerRowIndex, double conversionFactor) GetFileConfiguration(string filePath)
    {
        double conversionFactor = 1.0; // Default to mm

        using (var reader = new StreamReader(filePath))
        {
            string line;
            int rowIndex = 0;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.TrimStart().StartsWith("\"Designator\""))
                {
                    return (rowIndex, conversionFactor);
                }

                if (line.Contains("Units used:"))
                {
                    if (line.ToLower().Contains("mil"))
                    {
                        conversionFactor = 0.0254; // 1 mil = 0.0254 mm
                    }
                }
                rowIndex++;
            }
        }
        return (-1, conversionFactor);
    }

    // --- Dynamic CsvHelper Mapping ---
    private sealed class DynamicMap : ClassMap<ComponentData>
    {
        public DynamicMap(string xHeader, string yHeader)
        {
            Map(m => m.Designator).Name("Designator");
            Map(m => m.Layer).Name("Layer");
            Map(m => m.Center_X_MM).Name(xHeader).TypeConverter<UnitStrippingDoubleConverter>();
            Map(m => m.Center_Y_MM).Name(yHeader).TypeConverter<UnitStrippingDoubleConverter>();
            Map(m => m.GridZone).Ignore();
        }
    }

    // --- MAIN METHOD: LoadAndMapData ---
    public void LoadAndMapData(string filePath)
    {
        double zoneWidth = BoardWidth / GridCols;
        double zoneHeight = BoardHeight / GridRows;

        var (headerRowIndex, conversionFactor) = GetFileConfiguration(filePath);

        string xHeaderName = (conversionFactor == 1.0) ? "Center-X(mm)" : "Center-X(mil)";
        string yHeaderName = (conversionFactor == 1.0) ? "Center-Y(mm)" : "Center-Y(mil)";

        Console.WriteLine($"Board Size: {BoardWidth}x{BoardHeight}mm. Grid Zone Size: {zoneWidth:F2}x{zoneHeight:F2}mm.");
        Console.WriteLine($"\nFile Configuration Detected:");
        Console.WriteLine($"  Header starts on line: {headerRowIndex + 1}");
        Console.WriteLine($"  Units: {(conversionFactor == 1.0 ? "mm" : "mil")} (Factor: {conversionFactor})");
        Console.WriteLine($"  X/Y Headers: {xHeaderName}, {yHeaderName}");
        Console.WriteLine("Reading Centroid file and mapping components...");

        try
        {
            using (var reader = new StreamReader(filePath))
            {
                for (int i = 0; i < headerRowIndex; i++)
                {
                    reader.ReadLine();
                }

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    HeaderValidated = null,
                    IgnoreBlankLines = true,
                    Delimiter = ",",
                };

                using (var csv = new CsvReader(reader, config))
                {
                    csv.Context.RegisterClassMap(new DynamicMap(xHeaderName, yHeaderName));
                    var records = csv.GetRecords<ComponentData>().ToList();

                    foreach (var comp in records)
                    {
                        double midX_mm = comp.Center_X_MM * conversionFactor;
                        double midY_mm = comp.Center_Y_MM * conversionFactor;

                        comp.Center_X_MM = midX_mm;
                        comp.Center_Y_MM = midY_mm;

                        // 1. PRIMARY GRID (4x4)
                        int colIndex = (int)Math.Floor(midX_mm / zoneWidth);
                        int rowIndex = (int)Math.Floor(midY_mm / zoneHeight);

                        if (colIndex >= GridCols) colIndex = GridCols - 1;
                        if (rowIndex >= GridRows) rowIndex = GridRows - 1;

                        char rowLabel = (char)('A' + rowIndex);
                        int colLabel = colIndex + 1;
                        string primaryZone = $"{rowLabel}{colLabel}";

                        // 2. SECONDARY GRID (3x3) - Calculation for high precision

                        // Calculate coordinates relative to the start of the current Primary Zone
                        double x_relative_to_zone = midX_mm - (colIndex * zoneWidth);
                        double y_relative_to_zone = midY_mm - (rowIndex * zoneHeight);

                        // Calculate Secondary Zone dimensions
                        double secondaryZoneWidth = zoneWidth / SecondaryGridRes;
                        double secondaryZoneHeight = zoneHeight / SecondaryGridRes;

                        // Calculate Secondary Grid Index (1-based label)
                        int secondaryColLabel = (int)Math.Floor(x_relative_to_zone / secondaryZoneWidth) + 1;
                        int secondaryRowLabel = (int)Math.Floor(y_relative_to_zone / secondaryZoneHeight) + 1;

                        // Safety check for components exactly on the border
                        if (secondaryColLabel > SecondaryGridRes) secondaryColLabel = SecondaryGridRes;
                        if (secondaryRowLabel > SecondaryGridRes) secondaryRowLabel = SecondaryGridRes;


                        // 3. ASSIGN FINAL TWO-TIERED LABEL (A1-23)
                        comp.GridZone = $"{primaryZone}-{secondaryRowLabel}{secondaryColLabel}";

                        if (!componentMap.TryAdd(comp.Designator, comp))
                        {
                            Console.WriteLine($"Warning: Duplicate designator found: {comp.Designator}");
                        }
                    }

                    Console.WriteLine($"Successfully mapped {componentMap.Count} unique components.");

                }
            }
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"ERROR: File not found at {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred during file parsing: {ex.Message}");
        }
    }

    public void DisplayGrid(string targetZone)
    {
        Console.WriteLine("\n--- Board Grid (4x4) ---");

        // Rows are A (bottom) to D (top). We loop from top (D) down to bottom (A)
        for (int i = GridRows - 1; i >= 0; i--)
        {
            char rowLabel = (char)('A' + i);

            // 1. Draw the top/middle border line
            Console.WriteLine("+-------+-------+-------+-------+");

            // 2. Draw the row labels and component markers
            for (int j = 0; j < GridCols; j++)
            {
                int colLabel = j + 1;
                string currentZone = $"{rowLabel}{colLabel}";

                // Get marker status:
                // '⦿' for the target component's zone (only use the primary part of the zone string)
                // '•' for a zone with other components
                // ' ' for an empty zone
                string marker = "       "; // 7 spaces wide

                if (currentZone.Equals(targetZone, StringComparison.OrdinalIgnoreCase))
                {
                    // Center the primary zone label and highlight the zone
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.ForegroundColor = ConsoleColor.White;
                    marker = $"|  {currentZone}  ";
                }
                else if (primaryZoneComponentMap.ContainsKey(currentZone))
                {
                    // Zone has components, but it's not the target
                    marker = $"|  {currentZone}•  ";
                }
                else
                {
                    // Empty zone
                    marker = $"|  {currentZone}   ";
                }

                Console.Write(marker);
                Console.ResetColor(); // Reset color after printing the cell
            }
            Console.WriteLine("|"); // End of the component markers line

            // 3. Draw the bottom row with the coordinate marker (a simple dot for now)
            for (int j = 0; j < GridCols; j++)
            {
                int colLabel = j + 1;
                string currentZone = $"{rowLabel}{colLabel}";

                if (currentZone.Equals(targetZone, StringComparison.OrdinalIgnoreCase))
                {
                    // Highlight the cell containing the target component
                    Console.BackgroundColor = ConsoleColor.DarkGreen;
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write("|   ⦿   "); // Use '⦿' for the exact component marker
                }
                else
                {
                    // Empty cell
                    Console.Write("|       ");
                }
                Console.ResetColor();
            }
            Console.WriteLine("|"); // End of the component row
        }

        // 4. Draw the final bottom border
        Console.WriteLine("+-------+-------+-------+-------+");
    }
    public ComponentData FindComponent(string designator)
    {
        componentMap.TryGetValue(designator, out var comp);
        return comp;
    }
}