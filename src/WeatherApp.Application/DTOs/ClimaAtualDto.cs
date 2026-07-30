namespace WeatherApp.Application.DTOs;

/// <summary>
/// Contrato de <c>GET /api/clima/{cidade}</c>. Cobre exatamente o que o frontend precisa
/// exibir: temperatura atual, condição + ícone, máxima/mínima do dia e umidade.
/// </summary>
public sealed record ClimaAtualDto
{
    public required string Cidade { get; init; }
    public string? PaisCodigo { get; init; }

    /// <summary>Temperatura atual em °C.</summary>
    public required decimal Temperatura { get; init; }

    public required decimal SensacaoTermica { get; init; }

    /// <summary>
    /// Máxima do dia em °C, derivada dos blocos da previsão — não do campo <c>temp_max</c> da
    /// leitura atual, que representa dispersão entre estações no instante e não a amplitude
    /// diária. Ver <see cref="FonteMaxMin"/>.
    /// </summary>
    public required decimal TemperaturaMaxima { get; init; }

    public required decimal TemperaturaMinima { get; init; }

    /// <summary>Umidade relativa em %.</summary>
    public required int Umidade { get; init; }

    /// <summary>Descrição da condição, em pt-BR (ex.: "nuvens dispersas").</summary>
    public required string Condicao { get; init; }

    /// <summary>Código do ícone do provedor (ex.: "03d").</summary>
    public required string Icone { get; init; }

    /// <summary>URL pronta do ícone, para o frontend não ter de conhecer o provedor.</summary>
    public required string IconeUrl { get; init; }

    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }

    /// <summary>Data/hora local da medição na cidade consultada.</summary>
    public required DateTimeOffset DataHoraLocal { get; init; }

    /// <summary>
    /// De onde vieram <see cref="TemperaturaMaxima"/>/<see cref="TemperaturaMinima"/>:
    /// <c>"previsao"</c> (caminho normal, amplitude real do dia) ou <c>"leitura-atual"</c>
    /// (degradação quando a previsão está indisponível — valores bem mais estreitos).
    /// Explicitado no contrato para que a limitação seja visível em vez de silenciosa.
    /// </summary>
    public required string FonteMaxMin { get; init; }
}
