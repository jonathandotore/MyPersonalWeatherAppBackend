using System.ComponentModel.DataAnnotations;

namespace WeatherApp.Infrastructure.Auth;

public sealed class JwtSettings
{
    public const string SecaoConfiguracao = "Jwt";

    /// <summary>Chave de assinatura HMAC, Base64, gerada via dotnet user-secrets. Nunca em appsettings.json.</summary>
    [Required]
    public string Chave { get; init; } = string.Empty;

    [Required]
    public string Emissor { get; init; } = string.Empty;

    [Required]
    public string Audiencia { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int MinutosExpiracao { get; init; } = 60;
}
