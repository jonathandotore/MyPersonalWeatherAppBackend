using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeatherApp.Domain.Entities;

namespace WeatherApp.Infrastructure.Persistence.Configurations;

internal sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("Usuarios");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Nome)
            .HasMaxLength(150)
            .IsRequired();

        // Nullable porque o usuário anônimo (criado a partir do GUID do LocalStorage) não tem
        // e-mail nem senha até se registrar.
        builder.Property(u => u.Email).HasMaxLength(200);
        builder.Property(u => u.SenhaHash).HasMaxLength(300);

        builder.Property(u => u.DataCriacao)
            .HasColumnType("datetime2")
            .IsRequired();

        // Índice único FILTRADO — detalhe essencial no SQL Server.
        //
        // Um índice UNIQUE comum trata NULLs como iguais entre si e aceita apenas UM NULL na
        // coluna. Como todo usuário anônimo tem Email NULL, o segundo visitante do site
        // estouraria violação de chave única. O filtro tira os NULLs do índice: e-mails
        // continuam únicos entre usuários registrados, e anônimos convivem sem restrição.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL")
            .HasDatabaseName("UQ_Usuarios_Email");

        // Propriedade calculada; não existe como coluna.
        builder.Ignore(u => u.EhAnonimo);
    }
}
