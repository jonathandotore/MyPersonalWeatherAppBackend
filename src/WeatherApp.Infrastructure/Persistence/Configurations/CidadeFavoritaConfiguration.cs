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

        builder.Property(c => c.PaisCodigo)
            .HasMaxLength(5);

        builder.Property(c => c.Latitude)
            .HasPrecision(9, 6);

        builder.Property(c => c.Longitude)
            .HasPrecision(9, 6);

        builder.Property(c => c.DataCriacao)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(c => c.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_CidadesFavoritas_Usuarios");

        // Impede o mesmo usuário de favoritar a mesma cidade duas vezes, no banco e não só na aplicação
        builder.HasIndex(c => new { c.UsuarioId, c.Nome })
            .IsUnique()
            .HasDatabaseName("UQ_Usuario_Cidade");
    }
}
