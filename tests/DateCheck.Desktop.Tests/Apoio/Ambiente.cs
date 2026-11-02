using System.Globalization;
using DateCheck.Application.Servicos;
using DateCheck.Desktop.ViewModels;
using DateCheck.Domain.Entidades;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Desktop.Tests.Apoio;

/// <summary>
/// Monta ViewModels sobre os serviços reais da Application, com repositório em memória,
/// relógio controlável e diálogos falsos. Por padrão, "agora" é 02/11/2026 13:30.
/// </summary>
public sealed class Ambiente
{
    public static readonly DateTime Agora = new(2026, 11, 2, 13, 30, 0);

    public Ambiente()
    {
        // As telas formatam nomes de dia e mês pela cultura atual (o app define pt-BR na inicialização).
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");

        Relogio.SetLocalTimeZone(TimeZoneInfo.Utc);
        DefinirAgora(Agora);

        Agendamentos = new AgendamentoService(Repositorio, Relogio);
        Conflitos = new ConflitoService(Repositorio);
        Duplicacao = new DuplicacaoService(Repositorio, Relogio);
        Repeticao = new RepeticaoService(Repositorio, Conflitos, Relogio);
    }

    public FakeAgendamentoRepository Repositorio { get; } = new();

    public FakeTimeProvider Relogio { get; } = new();

    public FakeDialogService Dialogos { get; } = new();

    public FakeNavegacao Navegacao { get; } = new();

    public IAgendamentoService Agendamentos { get; }

    public IConflitoService Conflitos { get; }

    public IDuplicacaoService Duplicacao { get; }

    public IRepeticaoService Repeticao { get; }

    public void DefinirAgora(DateTime agora) =>
        Relogio.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(agora, DateTimeKind.Utc)));

    public Agendamento Semear(string titulo, DateTime inicio, DateTime fim, string? descricao = null) =>
        Repositorio.Semear(new Agendamento(titulo, inicio, fim, descricao));

    public HojeViewModel CriarHoje()
    {
        // O timer da tela posta o refresh no SynchronizationContext capturado ao ativar. O xunit instala um
        // contexto próprio (assíncrono); sem ele o refresh roda direto e os testes ficam determinísticos.
        // Precisa ser chamado de forma síncrona dentro do teste, antes de qualquer await.
        SynchronizationContext.SetSynchronizationContext(null);

        return new(Agendamentos, Conflitos, Duplicacao, Navegacao, Dialogos, Relogio);
    }

    public CalendarioViewModel CriarCalendario() =>
        new(Agendamentos, Navegacao, Dialogos, Relogio);

    public AgendamentoFormViewModel CriarFormulario() =>
        new(Agendamentos, Conflitos, Repeticao, Dialogos, Relogio);
}
