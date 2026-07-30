using WeatherApp.Domain.Entities;

namespace WeatherApp.Domain.Interfaces;

public interface IUsuarioRepository
{
    Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken ct = default);

    Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Cria o usuário anônimo se não existir (a FK de CidadesFavoritas exige a linha).
    /// Só agenda a inserção — quem persiste é o AdicionarAsync do favorito, na mesma transação.</summary>
    Task<Usuario> GarantirAnonimoAsync(Guid id, CancellationToken ct = default);

    Task AdicionarAsync(Usuario usuario, CancellationToken ct = default);
}
