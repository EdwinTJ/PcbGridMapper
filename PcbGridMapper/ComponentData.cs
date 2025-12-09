using CsvHelper.Configuration.Attributes;
public class ComponentData
{
    public string? Designator { get; set; }

    public double Center_X_MM { get; set; }

    public double Center_Y_MM { get; set; }

    public string? Layer { get; set; }

    public string? GridZone { get; set; }
}