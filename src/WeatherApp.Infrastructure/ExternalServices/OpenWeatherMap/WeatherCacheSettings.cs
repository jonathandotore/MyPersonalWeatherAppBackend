using System.ComponentModel.DataAnnotations;

namespace WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap;

public sealed class WeatherCacheSettings
{
    public const string SecaoConfiguracao = "WeatherCache";

    [Range(1, 1440)]
    public int TtlClimaAtualMinutos { get; init; } = 10;

    [Range(1, 1440)]
    public int TtlPrevisaoMinutos { get; init; } = 15;
}
