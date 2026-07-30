using Microsoft.EntityFrameworkCore;
using WeatherApp.Domain.Entities;

namespace WeatherApp.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core da aplicação. Funciona também como Unit of Work implícito: o
/// <c>SaveChangesAsync</c> comita as mudanças dos repositórios na mesma transação, o que
/// dispensa formalizar um <c>IUnitOfWork</c> separado num domínio deste tamanho.
/// </summary>
public sealed class WeatherAppDbContext(DbContextOptions<WeatherAppDbContext> options)
    : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<CidadeFavorita> CidadesFavoritas => Set<CidadeFavorita>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Aplica todas as IEntityTypeConfiguration deste assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WeatherAppDbContext).Assembly);
    }
}
