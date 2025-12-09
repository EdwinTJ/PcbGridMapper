using System;

internal class Program
{
    static void Main(string[] args)
    {
        const string CentroidFilePath = "Panel 436-5002_R.csv";
        //const string CentroidFilePath = "50257_vC.0_PICK.csv";

        Console.WriteLine("--- Board Setup ---");
        Console.Write("Enter Board Width in mm (e.g., 100.0): ");

        if (!double.TryParse(Console.ReadLine(), out double width))
        {
            width = 100.0;
        }

        Console.Write("Enter Board Height in mm (e.g., 100.0): ");
        if (!double.TryParse(Console.ReadLine(), out double height))
        {
            height = 100.0;
        }

        var mapper = new BoardGridMapper(width, height);
        mapper.LoadAndMapData(CentroidFilePath);

        Console.WriteLine("\n--- Reference Designator Search ---");
        Console.WriteLine("Type 'q' to quit.");

        while (true)
        {
            Console.WriteLine("Enter RD to search (e.g., c102, R16)");
            string searchRd = Console.ReadLine();

            if (searchRd?.ToLower() == "q")
            {
                break;
            }
            if (string.IsNullOrWhiteSpace(searchRd))
            {
                continue;
            }

            var component = mapper.FindComponent(searchRd);
            if (component != null)
            {
                string primaryZone = component.GridZone!.Split('-')[0];
                mapper.DisplayGrid(primaryZone);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n✅ FOUND: {component.Designator}");

       
                Console.WriteLine($"   Precise Location: {component.GridZone} (Primary Zone-Secondary Row/Col)");
                Console.WriteLine($"      -> Primary Zone (e.g., A1): The coarse 4x4 section.");
                Console.WriteLine($"      -> Secondary Zone (e.g., -23): The fine 3x3 subdivision within that section.");
                // ------------------------------------------

                Console.WriteLine($"   Side: {component.Layer}");
                Console.WriteLine($"   Coordinates: X={component.Center_X_MM:F2}mm, Y={component.Center_Y_MM:F2}mm");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ ERROR: Reference Designator '{searchRd}' not found.");
                Console.ResetColor();
            }

        }
    }
}
// ... (ComponentData.cs remains the same) ...