using WeatherApp.Domain.Entities;

namespace WeatherApp.Domain.Interfaces;

/// <summary>
/// Repositório de favoritos. Note que toda consulta recebe <c>usuarioId</c>: não existe
/// "listar todos os favoritos" nem "obter por id" sem escopo de usuário, o que torna o
/// vazamento de dados entre usuários difícil de escrever por acidente.
/// </summary>
public interface ICidadeFavoritaRepository
{
    Task<IReadOnlyList<CidadeFavorita>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Retorna <c>null</c> se o favorito não existe <b>ou</b> é de outro usuário.</summary>
    Task<CidadeFavorita?> ObterPorIdAsync(Guid id, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Checagem de duplicidade (UQ_Usuario_Cidade), case-insensitive no nome.</summary>
    Task<bool> ExisteAsync(Guid usuarioId, string nome, CancellationToken ct = default);

    Task AdicionarAsync(CidadeFavorita favorita, CancellationToken ct = default);

    Task RemoverAsync(CidadeFavorita favorita, CancellationToken ct = default);
}
