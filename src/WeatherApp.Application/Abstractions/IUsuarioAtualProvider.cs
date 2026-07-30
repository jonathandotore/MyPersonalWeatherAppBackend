namespace WeatherApp.Application.Abstractions;

/// <summary>Resolve o usuário da requisição atual, sem o serviço saber se veio de header ou de JWT.
/// Nunca aceitar usuarioId vindo do corpo da requisição — o dono do recurso é sempre identidade
/// ambiente, nunca dado enviado pelo cliente.</summary>
public interface IUsuarioAtualProvider
{
    /// <exception cref="Domain.Exceptions.UsuarioNaoIdentificadoException">Sem identidade utilizável.</exception>
    Guid ObterUsuarioId();

    bool EstaAutenticado { get; }
}
