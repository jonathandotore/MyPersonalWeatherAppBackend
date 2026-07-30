using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeatherApp.Domain.Entities;

namespace WeatherApp.Infrastructure.Persistence.Configurations;

internal sealed class CidadeFavoritaConfiguration : IEntityTypeConfiguration<CidadeFavorita>
{
    public void Configure(EntityTypeBuilder<CidadeFavorita> builder)
    {
        builder.ToTable("CidadesFavoritas");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Nome)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(c => c.PaisCodigo).HasMaxLength(5);

        // decimal(9,6): ~0,1 m de resolução, suficiente para coordenadas de cidade.
        builder.Property(c => c.Latitude).HasPrecision(9, 6);
        builder.Property(c => c.Longitude).HasPrecision(9, 6);

        builder.Property(c => c.DataCriacao)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(c => c.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_CidadesFavoritas_Usuarios");

        // Impede o mesmo usuário de favoritar a mesma cidade duas vezes, no banco e não só na
        // aplicação — o serviço checa antes para devolver 409 com mensagem amigável, mas a
        // constraint é a garantia real contra corrida entre requisições concorrentes.
        builder.HasIndex(c => new { c.UsuarioId, c.Nome })
            .IsUnique()
            .HasDatabaseName("UQ_Usuario_Cidade");
    }
}
