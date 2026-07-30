using System.Text.Json.Serialization;

namespace WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap.Contracts;

// Records de desserialização espelhando o JSON real da OpenWeatherMap (capturado da API ao
// vivo). Ficam internos ao Adapter de propósito: nada disso atravessa para a Application.

/// <summary>Resposta de <c>/data/2.5/weather</c>.</summary>
internal sealed record OwmClimaAtualResponse
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("main")] public OwmMain? Main { get; init; }
    [JsonPropertyName("weather")] public List<OwmWeather>? Weather { get; init; }
    [JsonPropertyName("coord")] public OwmCoord? Coord { get; init; }
    [JsonPropertyName("sys")] public OwmSys? Sys { get; init; }

    /// <summary>Deslocamento do fuso local em relação a UTC, em <b>segundos</b>.</summary>
    [JsonPropertyName("timezone")] public int Timezone { get; init; }

    /// <summary>Instante da medição, em segundos Unix (UTC).</summary>
    [JsonPropertyName("dt")] public long Dt { get; init; }
}

/// <summary>Resposta de <c>/data/2.5/forecast</c> — 40 blocos de 3 horas.</summary>
internal sealed record OwmPrevisaoResponse
{
    [JsonPropertyName("cnt")] public int Cnt { get; init; }
    [JsonPropertyName("list")] public List<OwmBloco>? List { get; init; }
    [JsonPropertyName("city")] public OwmCity? City { get; init; }
}

internal sealed record OwmBloco
{
    [JsonPropertyName("dt")] public long Dt { get; init; }
    [JsonPropertyName("main")] public OwmMain? Main { get; init; }
    [JsonPropertyName("weather")] public List<OwmWeather>? Weather { get; init; }

    /// <summary>Probabilidade de precipitação, 0 a 1.</summary>
    [JsonPropertyName("pop")] public decimal Pop { get; init; }

    [JsonPropertyName("sys")] public OwmBlocoSys? Sys { get; init; }
}

internal sealed record OwmMain
{
    [JsonPropertyName("temp")] public decimal Temp { get; init; }
    [JsonPropertyName("feels_like")] public decimal FeelsLike { get; init; }

    /// <summary>
    /// ⚠️ Em <c>/weather</c> isto é a dispersão entre estações <b>no instante atual</b>, não a
    /// mínima do dia. Em <c>/forecast</c> é a mínima <b>daquele bloco de 3 h</b>. Só o segundo
    /// caso é agregável em "mínima do dia".
    /// </summary>
    [JsonPropertyName("temp_min")] public decimal TempMin { get; init; }

    [JsonPropertyName("temp_max")] public decimal TempMax { get; init; }
    [JsonPropertyName("humidity")] public int Humidity { get; init; }
}

internal sealed record OwmWeather
{
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("icon")] public string? Icon { get; init; }
}

internal sealed record OwmCoord
{
    [JsonPropertyName("lat")] public decimal Lat { get; init; }
    [JsonPropertyName("lon")] public decimal Lon { get; init; }
}

internal sealed record OwmSys
{
    [JsonPropertyName("country")] public string? Country { get; init; }
}

internal sealed record OwmBlocoSys
{
    /// <summary>"d" (dia) ou "n" (noite).</summary>
    [JsonPropertyName("pod")] public string? Pod { get; init; }
}

internal sealed record OwmCity
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }

    /// <summary>Offset do fuso local em segundos — base para agrupar por data local.</summary>
    [JsonPropertyName("timezone")] public int Timezone { get; init; }
}
