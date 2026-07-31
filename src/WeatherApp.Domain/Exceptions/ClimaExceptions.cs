namespace WeatherApp.Domain.Exceptions;

/// <summary>
/// Provedor externo indisponível: timeout, circuito aberto, 5xx ou rate limit estourado.
/// Vira HTTP 503.
///
/// <para>A mensagem original do provedor <b>nunca</b> deve ser repassada ao cliente — a query
/// string da OpenWeatherMap carrega <c>appid=&lt;chave&gt;</c>. Ela vai só para o log.</para>
/// </summary>
public sealed class ProvedorClimaIndisponivelException(string mensagem, Exception? inner = null) : DomainException(mensagem)
{
    public override string Titulo => "Serviço de clima indisponível";
    public Exception? Causa { get; } = inner;
}

/// <summary>
/// Falha de integração atribuível à nossa configuração — tipicamente 401/403 por chave
/// inválida ou ainda não ativada. Vira HTTP 502, porque o problema não é do cliente.
/// </summary>
public sealed class FalhaIntegracaoProvedorException(string mensagem) : DomainException(mensagem)
{
    public override string Titulo => "Falha na integração com o provedor de clima";
}
