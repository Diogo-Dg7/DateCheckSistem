using DateCheck.Domain.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DateCheck.Infrastructure.Persistencia.Configuracoes;

public class AgendamentoConfiguration : IEntityTypeConfiguration<Agendamento>
{
    public void Configure(EntityTypeBuilder<Agendamento> builder)
    {
        builder.ToTable("Agendamentos");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.Titulo)
            .IsRequired()
            .HasMaxLength(Agendamento.TamanhoMaximoTitulo);

        builder.Property(a => a.Descricao)
            .HasMaxLength(Agendamento.TamanhoMaximoDescricao);

        // Datas em horário local, sem fuso (uso pessoal, único fuso).
        builder.Property(a => a.Inicio).HasColumnType("datetime2");
        builder.Property(a => a.Fim).HasColumnType("datetime2");

        builder.Property(a => a.Status).IsRequired();

        builder.Property(a => a.SerieId);

        builder.HasIndex(a => a.Inicio).HasDatabaseName("IX_Agendamentos_Inicio");
        builder.HasIndex(a => a.SerieId).HasDatabaseName("IX_Agendamentos_SerieId");
    }
}
