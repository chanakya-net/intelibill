namespace BarcodeAPI.Infrastructure.OpenFoodFacts;

public sealed class OpenFoodFactsOptions
{
    public string BaseUrl { get; set; } = "https://world.openfoodfacts.net/";
    public int TimeoutSeconds { get; set; } = 10;
}
