namespace WeatherApp.Application.Abstractions;

/// <summary>Resolve o usuário da requisição atual, sem o serviço saber se veio de header ou de JWT.
/// Nunca aceitar usuarioId vindo do corpo da requisição — o dono do recurso é sempre identidade
/// ambiente, nunca dado enviado pelo cliente.</summary>
public interface IUsuarioAtualProvider
{
    /// <exception cref="Domain.Exceptions.UsuarioNaoIdentificadoException">Sem identidade utilizável.</exception>
    Guid ObterUsuarioId();

    /// <summary>Como <see cref="ObterUsuarioId"/>, mas sem lançar — usado no registro, onde não
    /// ter identidade prévia é um caso válido (usuário novo, sem favoritos anônimos).</summary>
    bool TentarObterUsuarioId(out Guid id);

    bool EstaAutenticado { get; }
}
