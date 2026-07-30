using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WeatherApp.Domain.Entities;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Infrastructure.Auth;

public sealed class JwtTokenService(IOptions<JwtSettings> opcoes) : IJwtTokenService
{
    private static readonly JsonWebTokenHandler Handler = new();
    private readonly JwtSettings _cfg = opcoes.Value;

    public (string Token, DateTime ExpiraEmUtc) GerarToken(Usuario usuario)
    {
        var expiraEm = DateTime.UtcNow.AddMinutes(_cfg.MinutosExpiracao);
        var chave = new SymmetricSecurityKey(Convert.FromBase64String(_cfg.Chave));

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _cfg.Emissor,
            Audience = _cfg.Audiencia,
            Expires = expiraEm,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = usuario.Id.ToString(),
                ["email"] = usuario.Email ?? string.Empty
            },
            SigningCredentials = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256)
        });

        return (token, expiraEm);
    }
}
