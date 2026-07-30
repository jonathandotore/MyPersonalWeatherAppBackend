using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WeatherApp.Domain.Exceptions;

namespace WeatherApp.API.ErrorHandling;

/// <summary>
/// Traduz exceções de domínio em <c>ProblemDetails</c> com o status HTTP correto.
///
/// <para>Usa a abstração nativa <see cref="IExceptionHandler"/> (.NET 8+) em vez de um
/// middleware escrito à mão — o plano original mencionava as duas opções. Ganhos: integra com
/// <c>IProblemDetailsService</c>, é encadeável (vários handlers em ordem de registro) e dispensa
/// <c>try/catch</c> repetido em cada controller.</para>
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context,Exception exception,CancellationToken ct)
    {
        if (exception is not DomainException dominio)
            return false;

        var status = MapearStatus(dominio);

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(dominio, "Falha de integração tratada: {Titulo}", dominio.Titulo);
        else
            logger.LogDebug("Regra de negócio recusou a requisição: {Titulo}", dominio.Titulo);

        context.Response.StatusCode = status;

        if (status == StatusCodes.Status503ServiceUnavailable)
            context.Response.Headers.RetryAfter = "60";

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = dominio,
            ProblemDetails = new ProblemDetails
            {
                Title = dominio.Titulo,
                Detail = dominio.Message,
                Status = status,
                Instance = context.Request.Path
            }
        });
    }

    private static int MapearStatus(DomainException excecao) => excecao switch
    {
        CidadeNaoEncontradaException => StatusCodes.Status404NotFound,
        FavoritoNaoEncontradoException => StatusCodes.Status404NotFound,

        FavoritoDuplicadoException => StatusCodes.Status409Conflict,
        EmailJaCadastradoException => StatusCodes.Status409Conflict,

        CredenciaisInvalidasException => StatusCodes.Status401Unauthorized,

        // 400 enquanto a identificação vem por header; passa a 401 quando os endpoints
        // de favoritos exigirem [Authorize] (o próprio pipeline responde antes daqui).
        UsuarioNaoIdentificadoException => StatusCodes.Status400BadRequest,

        // Provedor fora do ar é indisponibilidade temporária, não erro do cliente.
        ProvedorClimaIndisponivelException => StatusCodes.Status503ServiceUnavailable,

        // Chave inválida/ausente é configuração nossa: 502, não 503 nem 500.
        FalhaIntegracaoProvedorException => StatusCodes.Status502BadGateway,

        _ => StatusCodes.Status500InternalServerError
    };
}
