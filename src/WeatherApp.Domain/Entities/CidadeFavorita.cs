namespace WeatherApp.Domain.Entities;

/// <summary>
/// Cidade marcada como favorita por um usuário.
///
/// <para><see cref="Latitude"/>/<see cref="Longitude"/> são opcionais mas guardadas quando o
/// provedor as devolve: desambiguam cidades homônimas ("Springfield") e permitem consultar a
/// previsão por coordenada, sem depender do endpoint de busca por nome — que a OpenWeatherMap
/// marca como deprecated.</para>
/// </summary>
public sealed class CidadeFavorita
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;

    /// <summary>
    /// Código ISO do país, ex.: "BR". Evita ambiguidade de cidades homônimas.
    /// </summary>
    public string? PaisCodigo { get; private set; }

    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }

    public Guid UsuarioId { get; private set; }
    public DateTime DataCriacao { get; private set; }

    // Construtor sem parâmetros exigido pelo EF Core.
    private CidadeFavorita() { }

    public static CidadeFavorita Criar(
        Guid usuarioId,
        string nome,
        string? paisCodigo = null,
        decimal? latitude = null,
        decimal? longitude = null) => new()
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Nome = nome.Trim(),
            PaisCodigo = string.IsNullOrWhiteSpace(paisCodigo)
                ? null
                : paisCodigo.Trim().ToUpperInvariant(),
            Latitude = latitude,
            Longitude = longitude,
            DataCriacao = DateTime.UtcNow
        };

    /// <summary>
    /// Completa as coordenadas quando elas só ficam conhecidas depois de consultar o provedor.
    /// </summary>
    public void DefinirCoordenadas(decimal latitude, decimal longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }
}
