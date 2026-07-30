using Microsoft.EntityFrameworkCore;
using WeatherApp.Domain.Entities;
using WeatherApp.Domain.Interfaces;

namespace WeatherApp.Infrastructure.Persistence.Repositories;

internal sealed class UsuarioRepository(WeatherAppDbContext db) : IUsuarioRepository
{
    public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken ct = default) =>
        db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken ct = default)
    {
        var normalizado = Usuario.NormalizarEmail(email);
        return db.Usuarios.FirstOrDefaultAsync(u => u.Email == normalizado, ct);
    }

    public async Task<Usuario> GarantirAnonimoAsync(Guid id, CancellationToken ct = default)
    {
        var existente = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (existente is not null)
        {
            return existente;
        }

        var anonimo = Usuario.CriarAnonimo(id);
        await db.Usuarios.AddAsync(anonimo, ct);
        return anonimo;
    }

    public async Task AdicionarAsync(Usuario usuario, CancellationToken ct = default)
    {
        await db.Usuarios.AddAsync(usuario, ct);
        await db.SaveChangesAsync(ct);
    }
}
