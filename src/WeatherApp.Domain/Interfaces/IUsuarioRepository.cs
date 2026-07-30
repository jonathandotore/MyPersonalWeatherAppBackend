using WeatherApp.Domain.Entities;

namespace WeatherApp.Domain.Interfaces;

public interface IUsuarioRepository
{
    Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken ct = default);

    Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Garante (idempotentemente) que existe uma linha em <c>Usuarios</c> para este GUID
    /// anônimo, criando-a se necessário.
    ///
    /// <para>Existe porque <c>CidadesFavoritas.UsuarioId</c> tem FK para <c>Usuarios.Id</c>:
    /// sem essa linha, o primeiro POST de favorito de um usuário anônimo estouraria a
    /// constraint. Chamado antes de inserir favorito.</para>
    /// </summary>
    Task<Usuario> GarantirAnonimoAsync(Guid id, CancellationToken ct = default);

    Task AdicionarAsync(Usuario usuario, CancellationToken ct = default);

    /// <summary>Persiste as alterações pendentes (Unit of Work implícito do DbContext).</summary>
    Task SalvarAsync(CancellationToken ct = default);
}
