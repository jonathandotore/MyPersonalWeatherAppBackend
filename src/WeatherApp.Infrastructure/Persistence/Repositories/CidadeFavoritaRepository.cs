using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WeatherApp.Domain.Entities;
using WeatherApp.Domain.Exceptions;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Infrastructure.Persistence.Repositories;

internal sealed class CidadeFavoritaRepository(WeatherAppDbContext db) : ICidadeFavoritaRepository
{
    public async Task<IReadOnlyList<CidadeFavorita>> ListarPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken ct = default) =>
        await db.CidadesFavoritas
            .AsNoTracking()
            .Where(c => c.UsuarioId == usuarioId)
            .OrderBy(c => c.Nome)
            .ToListAsync(ct);

    // Sempre filtra também por usuário: favorito de outra pessoa devolve null, igual a
    // um Id inexistente — o serviço traduz os dois em 404, sem confirmar que o Id existe.
    public Task<CidadeFavorita?> ObterPorIdAsync(Guid id, Guid usuarioId, CancellationToken ct = default) =>
        db.CidadesFavoritas.FirstOrDefaultAsync(c => c.Id == id && c.UsuarioId == usuarioId, ct);

    public Task<bool> ExisteAsync(Guid usuarioId, string nome, CancellationToken ct = default) =>
        db.CidadesFavoritas.AnyAsync(
            c => c.UsuarioId == usuarioId && EF.Functions.Like(c.Nome, nome.Trim()), ct);

    public async Task AdicionarAsync(CidadeFavorita favorita, CancellationToken ct = default)
    {
        await db.CidadesFavoritas.AddAsync(favorita, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Backstop contra corrida entre requisições concorrentes e contra nome canônico
            // (vindo do provedor) que já estava favoritado sob outra grafia.
            throw new FavoritoDuplicadoException(favorita.Nome);
        }
    }

    public async Task RemoverAsync(CidadeFavorita favorita, CancellationToken ct = default)
    {
        db.CidadesFavoritas.Remove(favorita);
        await db.SaveChangesAsync(ct);
    }
}
