using BarcodeAPI.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace BarcodeAPI.Api.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    [HttpGet(Name = "GetWeatherForecast")]
    public ActionResult<IEnumerable<WeatherForecastDto>> Get()
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
            new WeatherForecastDto
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                Summaries[Random.Shared.Next(Summaries.Length)]
            ))
            .ToArray();

        return Ok(forecast);
    }
}
