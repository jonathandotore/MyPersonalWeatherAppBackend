namespace WeatherApp.Application.DTOs;

/// <summary>
/// Um dia da previsão, já agregado a partir dos blocos de 3 horas do provedor.
/// </summary>
public sealed record PrevisaoDiaDto
{
    /// <summary>
    /// Data <b>local</b> da cidade consultada (não UTC).
    /// </summary>
    public required DateOnly Data { get; init; }

    public required decimal TemperaturaMaxima { get; init; }
    public required decimal TemperaturaMinima { get; init; }

    public required string Condicao { get; init; }
    public required string Icone { get; init; }
    public required string IconeUrl { get; init; }

    /// <summary>
    /// Maior probabilidade de precipitação do dia, de 0 a 1.
    /// </summary>
    public required decimal ProbabilidadeChuva { get; init; }
}

/// <summary>
/// Contrato de <c>GET /api/clima/{cidade}/previsao</c>.
/// </summary>
public sealed record PrevisaoDto
{
    public required string Cidade { get; init; }
    public string? PaisCodigo { get; init; }

    /// <summary>
    /// Cinco dias, em ordem cronológica crescente.
    /// </summary>
    public required IReadOnlyList<PrevisaoDiaDto> Dias { get; init; }
}
