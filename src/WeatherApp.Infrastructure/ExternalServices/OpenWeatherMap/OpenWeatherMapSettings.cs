using System.ComponentModel.DataAnnotations;

namespace WeatherApp.Infrastructure.ExternalServices.OpenWeatherMap;

/// <summary>
/// Configuração tipada do provedor (Options Pattern), validada no startup.
///
/// <para>A <see cref="ApiKey"/> vem de <c>dotnet user-secrets</c>, nunca de
/// <c>appsettings.json</c>. Como user-secrets só é carregado em Development, validar no boot
/// evita o modo de falha mais confuso possível: subir normalmente e só descobrir a chave
/// ausente quando a primeira requisição volta 401 do provedor.</para>
/// </summary>
public sealed class OpenWeatherMapSettings
{
    public const string SecaoConfiguracao = "OpenWeatherMap";

    [Required(ErrorMessage = "OpenWeatherMap:BaseUrl é obrigatório.")]
    [Url]
    public string BaseUrl { get; init; } = "https://api.openweathermap.org/";

    [Required(ErrorMessage =
        "OpenWeatherMap:ApiKey não configurada. Rode: " +
        "dotnet user-secrets set \"OpenWeatherMap:ApiKey\" \"<sua-chave>\" --project src/WeatherApp.API")]
    [MinLength(16, ErrorMessage = "OpenWeatherMap:ApiKey parece curta demais para ser válida.")]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>"metric" devolve Celsius, que é o que o frontend exibe.</summary>
    public string Unidades { get; init; } = "metric";

    /// <summary>"pt_br" faz o provedor devolver a descrição da condição já em português.</summary>
    public string Idioma { get; init; } = "pt_br";
}
