namespace WeatherApp.Domain.Exceptions;

/// <summary>
/// Base das exceções de regra de negócio. O handler global na API traduz cada subtipo
/// em um <c>ProblemDetails</c> com o status correto, o que mantém os controllers livres
/// de <c>try/catch</c> repetido.
/// </summary>
public abstract class DomainException(string mensagem) : Exception(mensagem)
{
    /// <summary>Título curto exposto ao cliente no campo <c>title</c> do ProblemDetails.</summary>
    public abstract string Titulo { get; }
}
