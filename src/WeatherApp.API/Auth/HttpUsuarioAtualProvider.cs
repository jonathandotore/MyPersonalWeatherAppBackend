using System.Security.Claims;
using WeatherApp.Application.Abstractions;
using WeatherApp.Domain.Exceptions;

namespace WeatherApp.API.Auth;

/// <summary>Resolve o usuário: claim "sub" se autenticado, senão o header X-Usuario-Id.
/// ⚠️ Sem JWT, X-Usuario-Id é um identificador de partição de dados, não uma credencial —
/// vem do cliente e é forjável. A confiança real só existe a partir da fase 3 (JWT).</summary>
public sealed class HttpUsuarioAtualProvider(IHttpContextAccessor accessor) : IUsuarioAtualProvider
{
    public const string HeaderUsuarioAnonimo = "X-Usuario-Id";

    public bool EstaAutenticado => accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public Guid ObterUsuarioId()
    {
        var contexto = accessor.HttpContext ?? throw new UsuarioNaoIdentificadoException();

        if (TentarObterDaClaim(contexto.User, out var doToken))
        {
            return doToken;
        }

        if (contexto.Request.Headers.TryGetValue(HeaderUsuarioAnonimo, out var valores)
            && Guid.TryParse(valores.ToString(), out var doHeader)
            && doHeader != Guid.Empty)
        {
            return doHeader;
        }

        throw new UsuarioNaoIdentificadoException();
    }

    private static bool TentarObterDaClaim(ClaimsPrincipal principal, out Guid id)
    {
        id = Guid.Empty;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        // "sub" ou NameIdentifier: JwtBearer remapeia "sub" por default (MapInboundClaims=true).
        var valor = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(valor, out id);
    }
}
