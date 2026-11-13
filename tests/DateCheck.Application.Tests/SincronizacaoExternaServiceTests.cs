using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;
using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Application.Tests;

public class SincronizacaoExternaServiceTests
{
    // Hoje = 10/11/2026 12:00. Janela: [12/08/2026, 11/11/2027) = 90 dias atrás até 365 dias à frente.
    private static readonly DateTime Agora = new(2026, 11, 10, 12, 0, 0);
    private static readonly DateTime InicioDaJanela = new(2026, 8, 12);
    private static readonly DateTime FimDaJanela = new(2027, 11, 11);

    private readonly FakeAgendamentoRepository _repositorio = new();
    private readonly FonteFalsa _fonte = new();
    private readonly FakeTimeProvider _relogio = new();
    private readonly SincronizacaoExternaService _servico;

    public SincronizacaoExternaServiceTests()
    {
        _relogio.SetLocalTimeZone(TimeZoneInfo.Utc);
        _relogio.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(Agora, DateTimeKind.Utc)));
        _servico = new SincronizacaoExternaService(_fonte, _repositorio, _relogio, NullLogger<SincronizacaoExternaService>.Instance);
    }

    private static Agendamento Notion(string id, string titulo, DateTime inicio, DateTime? fim = null, bool diaInteiro = false) =>
        Agendamento.Importar(
            OrigemAgendamento.Notion, id, $"https://www.notion.so/{id}", titulo, null,
            inicio, fim ?? inicio.AddHours(1), diaInteiro, StatusAgendamento.Pendente);

    private Agendamento SemearNotion(string id, string titulo, DateTime inicio) =>
        _repositorio.Semear(Notion(id, titulo, inicio));

    private IEnumerable<string> IdsLocais => _repositorio.Itens.Select(i => i.IdExterno ?? "(local)").Order();

    // ---------- janela ----------

    [Fact]
    public void Janela_ENoventaDiasParaTrasAte365ParaFrente_ComFimExclusivo()
    {
        var janela = JanelaDeSincronizacao.Calcular(Agora);

        Assert.Equal(InicioDaJanela, janela.Inicio);
        Assert.Equal(FimDaJanela, janela.Fim);
        Assert.True(janela.Contem(InicioDaJanela));
        Assert.False(janela.Contem(InicioDaJanela.AddTicks(-1)));
        Assert.True(janela.Contem(FimDaJanela.AddTicks(-1)));
        Assert.False(janela.Contem(FimDaJanela));
        Assert.True(janela.Contem(Agora.Date.AddDays(365).AddHours(23))); // o 365º dia à frente entra inteiro
    }

    [Fact]
    public async Task PedeAFonteAJanelaCalculadaPeloRelogioInjetado()
    {
        await _servico.SincronizarAsync();

        Assert.Equal([(InicioDaJanela, FimDaJanela)], _fonte.Chamadas);
    }

    // ---------- inserir, atualizar, remover ----------

    [Fact]
    public async Task ItemNovo_Insere_ItemJaImportado_Atualiza_ItemQueSumiuDaJanela_Remove()
    {
        var existente = SemearNotion("atualiza", "Antes", new DateTime(2026, 11, 12, 9, 0, 0));
        SemearNotion("some", "Some", new DateTime(2026, 11, 13, 9, 0, 0));
        _fonte.Itens =
        [
            Notion("atualiza", "Depois", new DateTime(2026, 11, 12, 14, 0, 0)),
            Notion("novo", "Novo", new DateTime(2026, 11, 14, 9, 0, 0))
        ];

        var estado = await _servico.SincronizarAsync();

        Assert.Equal(SituacaoDaSincronizacao.Ok, estado.Situacao);
        Assert.Equal(1, estado.UltimoResultado!.Inseridos);
        Assert.Equal(1, estado.UltimoResultado.Atualizados);
        Assert.Equal(1, estado.UltimoResultado.Removidos);
        Assert.Equal(["atualiza", "novo"], IdsLocais);
        Assert.Equal("Depois", existente.Titulo);
        Assert.Equal(new DateTime(2026, 11, 12, 14, 0, 0), existente.Inicio);
    }

    [Fact]
    public async Task ItemDeDiaInteiroEStatusConcluido_SincronizaNormalmente()
    {
        var concluido = Agendamento.Importar(OrigemAgendamento.Notion, "ferias", null, "Férias", null,
            new DateTime(2026, 11, 20), new DateTime(2026, 11, 25), true, StatusAgendamento.Concluido);
        _fonte.Itens = [concluido];

        await _servico.SincronizarAsync();

        var salvo = Assert.Single(_repositorio.Itens);
        Assert.True(salvo.DiaInteiro);
        Assert.Equal(StatusAgendamento.Concluido, salvo.Status);
    }

    [Fact]
    public async Task SemMudancas_NaoDisparaDadosAlterados_ComMudancas_Dispara()
    {
        var disparos = 0;
        _servico.DadosAlterados += () => disparos++;
        _fonte.Itens = [Notion("a", "A", new DateTime(2026, 11, 12, 9, 0, 0))];

        await _servico.SincronizarAsync();
        Assert.Equal(1, disparos); // inseriu

        await _servico.SincronizarAsync();
        Assert.Equal(1, disparos); // idêntico: nada mudou, as telas não precisam recarregar

        _fonte.Itens = [];
        await _servico.SincronizarAsync();
        Assert.Equal(2, disparos); // removeu
    }

    [Fact]
    public async Task NuncaTocaEmItensLocais()
    {
        _repositorio.Semear(new Agendamento("Meu compromisso", new DateTime(2026, 11, 12, 9, 0, 0), new DateTime(2026, 11, 12, 10, 0, 0)));
        _fonte.Itens = [];

        await _servico.SincronizarAsync();

        Assert.Single(_repositorio.Itens);
        Assert.Equal(OrigemAgendamento.Local, _repositorio.Itens[0].Origem);
    }

    [Fact]
    public async Task ItemImportadoForaDaJanela_NuncaERemovido()
    {
        SemearNotion("antigo", "Antigo", InicioDaJanela.AddDays(-1));
        SemearNotion("distante", "Distante", FimDaJanela.AddDays(30));
        SemearNotion("dentro", "Dentro", new DateTime(2026, 11, 12, 9, 0, 0));
        _fonte.Itens = [];

        await _servico.SincronizarAsync();

        Assert.Equal(["antigo", "distante"], IdsLocais);
    }

    [Fact]
    public async Task IdRepetidoNaListaDaFonte_ContaUmaVez_SemFalhar()
    {
        _fonte.Itens =
        [
            Notion("x", "Primeira versão", new DateTime(2026, 11, 12, 9, 0, 0)),
            Notion("X", "Segunda versão", new DateTime(2026, 11, 12, 9, 0, 0))
        ];

        var estado = await _servico.SincronizarAsync();

        Assert.Equal(SituacaoDaSincronizacao.Ok, estado.Situacao);
        Assert.Equal("Segunda versão", Assert.Single(_repositorio.Itens).Titulo);
    }

    // ---------- corte exato por Inicio (folga de fuso) ----------

    [Fact]
    public async Task ItemQueVeioNaFolgaMasEstaForaDaJanelaExata_EDescartadoSemImportar()
    {
        _fonte.Itens =
        [
            Notion("folga-antes", "Folga antes", InicioDaJanela.AddHours(-1)), // 11/08 23:00: dentro da folga, fora da janela
            Notion("folga-depois", "Folga depois", FimDaJanela.AddHours(1)),    // 11/11/2027 01:00
            Notion("dentro", "Dentro", new DateTime(2026, 11, 12, 9, 0, 0))
        ];

        var estado = await _servico.SincronizarAsync();

        Assert.Equal(["dentro"], IdsLocais);
        Assert.Equal(1, estado.UltimoResultado!.Inseridos);
    }

    [Fact]
    public async Task ItemDaFolgaQueJaExisteLocalmenteFora_NaoEAtualizado()
    {
        var existente = SemearNotion("folga", "Título antigo", InicioDaJanela.AddHours(-2));
        _fonte.Itens = [Notion("folga", "Título novo", InicioDaJanela.AddHours(-1))];

        var estado = await _servico.SincronizarAsync();

        Assert.Equal("Título antigo", existente.Titulo);
        Assert.Equal(InicioDaJanela.AddHours(-2), existente.Inicio);
        Assert.Equal(0, estado.UltimoResultado!.Total);
    }

    [Fact]
    public async Task ItemDaFolga_NaoAfetaARemocaoDosDaJanela()
    {
        SemearNotion("sumiu", "Sumiu do Notion", new DateTime(2026, 11, 12, 9, 0, 0));
        var foraDaJanela = SemearNotion("fora", "Fora", InicioDaJanela.AddHours(-3));
        _fonte.Itens = [Notion("folga", "Só na folga", FimDaJanela.AddHours(2))];

        var estado = await _servico.SincronizarAsync();

        Assert.Equal(1, estado.UltimoResultado!.Removidos); // "sumiu" saiu: está na janela e não veio
        Assert.Equal(0, estado.UltimoResultado.Inseridos);  // "folga" foi descartado
        Assert.Equal(["fora"], IdsLocais);
        Assert.Same(foraDaJanela, _repositorio.Itens.Single());
    }

    [Fact]
    public async Task BordasExatas_InicioDaJanelaEntra_FimDaJanelaNao()
    {
        _fonte.Itens =
        [
            Notion("no-inicio", "No início", InicioDaJanela),
            Notion("no-fim", "No fim (exclusivo)", FimDaJanela),
            Notion("ultimo-instante", "Último instante", FimDaJanela.AddMinutes(-1))
        ];

        await _servico.SincronizarAsync();

        Assert.Equal(["no-inicio", "ultimo-instante"], IdsLocais);
    }

    // ---------- falhas: nunca removem nada ----------

    public static TheoryData<TipoDeFalhaExterna> TiposDeFalha => new()
    {
        TipoDeFalhaExterna.NaoAutorizado,
        TipoDeFalhaExterna.SemAcesso,
        TipoDeFalhaExterna.LimiteExcedido,
        TipoDeFalhaExterna.Indisponivel,
        TipoDeFalhaExterna.RespostaInvalida,
        TipoDeFalhaExterna.Incompleta
    };

    [Theory]
    [MemberData(nameof(TiposDeFalha))]
    public async Task FalhaDaFonte_NaoRemoveNemAlteraNada_NemChegaAoRepositorio(TipoDeFalhaExterna tipo)
    {
        var existente = SemearNotion("a", "A", new DateTime(2026, 11, 12, 9, 0, 0));
        SemearNotion("b", "B", new DateTime(2026, 11, 13, 9, 0, 0));
        _fonte.Itens = [Notion("a", "A alterada", new DateTime(2026, 11, 12, 9, 0, 0))]; // lista "parcial" que NÃO deve ser usada
        _fonte.Falha = new FonteExternaException(tipo, "mensagem do erro");

        var estado = await _servico.SincronizarAsync();

        Assert.Equal(["a", "b"], IdsLocais);
        Assert.Equal("A", existente.Titulo);
        Assert.Equal(0, _repositorio.Sincronizacoes); // o repositório nem foi chamado
        Assert.Equal("mensagem do erro", estado.Erro);
        Assert.Equal(
            tipo == TipoDeFalhaExterna.NaoAutorizado ? SituacaoDaSincronizacao.ReconecteNecessario : SituacaoDaSincronizacao.Erro,
            estado.Situacao);
    }

    [Fact]
    public async Task FalhaDaFonte_RegistraNoLogOMotivoCompleto_ComAMesmaMensagemDasConfiguracoes()
    {
        var log = new LogCapturado<SincronizacaoExternaService>();
        var servico = new SincronizacaoExternaService(_fonte, _repositorio, _relogio, log);
        const string mensagem = "O Notion não entregou todos os itens: ele limita cada consulta a 10.000 resultados. Nada foi alterado no DateCheck.";
        _fonte.Falha = new FonteExternaException(TipoDeFalhaExterna.Incompleta, mensagem);

        var estado = await servico.SincronizarAsync();

        Assert.Equal(mensagem, estado.Erro); // o que aparece nas Configurações
        var (_, texto) = Assert.Single(log.Entradas, e => e.Nivel == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains(mensagem, texto); // e o que vai para o log, o mesmo texto
        Assert.Contains("Incompleta", texto);
    }

    [Fact]
    public async Task FalhaDepoisDeUmaBoaSincronizacao_MantemAUltimaSincronizacaoNoEstado()
    {
        _fonte.Itens = [Notion("a", "A", new DateTime(2026, 11, 12, 9, 0, 0))];
        await _servico.SincronizarAsync();
        var horaDoSucesso = _servico.Estado.UltimaSincronizacao;
        Assert.Equal(Agora, horaDoSucesso);

        _relogio.Advance(TimeSpan.FromMinutes(10));
        _fonte.Falha = new FonteExternaException(TipoDeFalhaExterna.Indisponivel, "sem rede");
        var estado = await _servico.SincronizarAsync();

        Assert.Equal(SituacaoDaSincronizacao.Erro, estado.Situacao);
        Assert.Equal(horaDoSucesso, estado.UltimaSincronizacao);
        Assert.Single(_repositorio.Itens);
    }

    [Fact]
    public async Task ErroInesperadoNoBanco_ViraErroGenericoSemDerrubarNemVazarDetalhes()
    {
        var repositorioQuebrado = new RepositorioQueFalhaNoSync();
        var servico = new SincronizacaoExternaService(_fonte, repositorioQuebrado, _relogio, NullLogger<SincronizacaoExternaService>.Instance);

        var estado = await servico.SincronizarAsync();

        Assert.Equal(SituacaoDaSincronizacao.Erro, estado.Situacao);
        Assert.DoesNotContain("senha-do-banco", estado.Erro);
        Assert.NotNull(estado.Erro);
    }

    [Fact]
    public async Task Cancelamento_RelancaEDevolveOEstadoAnterior_SemFicarEmSincronizando()
    {
        _fonte.Liberacao = new TaskCompletionSource();
        using var cancelamento = new CancellationTokenSource();

        var tarefa = _servico.SincronizarAsync(cancellationToken: cancelamento.Token);
        Assert.Equal(SituacaoDaSincronizacao.Sincronizando, _servico.Estado.Situacao);
        await cancelamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
        Assert.NotEqual(SituacaoDaSincronizacao.Sincronizando, _servico.Estado.Situacao);
        Assert.Equal(0, _repositorio.Sincronizacoes);
    }

    // ---------- 401: reconectar, uma única vez ----------

    [Fact]
    public async Task Http401_ViraReconecteNecessario_EDisparaOAvisoUmaUnicaVez()
    {
        var avisos = 0;
        _servico.ReconexaoNecessaria += () => avisos++;
        _fonte.Falha = new FonteExternaException(TipoDeFalhaExterna.NaoAutorizado, "token recusado");

        await _servico.SincronizarAsync();
        await _servico.SincronizarAsync(); // 10 minutos depois, automática
        await _servico.SincronizarAsync();

        Assert.Equal(SituacaoDaSincronizacao.ReconecteNecessario, _servico.Estado.Situacao);
        Assert.Equal(1, avisos);
        Assert.Single(_fonte.Chamadas); // as seguintes nem consultaram o Notion com um token recusado
    }

    [Fact]
    public async Task SincronizarAgoraManual_TentaDeNovoNoEstadoReconecte_SemRepetirOAviso()
    {
        var avisos = 0;
        _servico.ReconexaoNecessaria += () => avisos++;
        _fonte.Falha = new FonteExternaException(TipoDeFalhaExterna.NaoAutorizado, "token recusado");
        await _servico.SincronizarAsync();

        await _servico.SincronizarAsync(manual: true);

        Assert.Equal(2, _fonte.Chamadas.Count);
        Assert.Equal(1, avisos);
    }

    [Fact]
    public async Task DepoisDeReconectar_ReiniciarLiberaAsAutomaticasEUmSucessoLimpaOErro()
    {
        _fonte.Falha = new FonteExternaException(TipoDeFalhaExterna.NaoAutorizado, "token recusado");
        await _servico.SincronizarAsync();

        _servico.Reiniciar();
        Assert.Equal(SituacaoDaSincronizacao.Aguardando, _servico.Estado.Situacao);

        _fonte.Falha = null;
        _fonte.Itens = [Notion("a", "A", new DateTime(2026, 11, 12, 9, 0, 0))];
        var estado = await _servico.SincronizarAsync(); // automática

        Assert.Equal(SituacaoDaSincronizacao.Ok, estado.Situacao);
        Assert.Null(estado.Erro);
    }

    [Fact]
    public async Task NovoAvisoDeReconexao_DepoisDeReconectarEFalharDeNovo()
    {
        var avisos = 0;
        _servico.ReconexaoNecessaria += () => avisos++;
        _fonte.Falha = new FonteExternaException(TipoDeFalhaExterna.NaoAutorizado, "recusado");
        await _servico.SincronizarAsync();

        _servico.Reiniciar(); // o usuário reconectou...
        await _servico.SincronizarAsync(); // ...e o token novo também foi recusado

        Assert.Equal(2, avisos);
    }

    [Fact]
    public async Task AvisoDeReconexao_NaoDisparaParaErrosQueNaoSaoDeToken()
    {
        var avisos = 0;
        _servico.ReconexaoNecessaria += () => avisos++;
        _fonte.Falha = new FonteExternaException(TipoDeFalhaExterna.Indisponivel, "sem rede");

        await _servico.SincronizarAsync();

        Assert.Equal(0, avisos);
    }

    // ---------- configuração ----------

    [Fact]
    public async Task SemConfiguracao_NaoRodaNada_EFicaNaoConfigurado()
    {
        _fonte.EstaConfigurada = false;
        SemearNotion("a", "A", new DateTime(2026, 11, 12, 9, 0, 0));

        var estado = await _servico.SincronizarAsync(manual: true);

        Assert.Equal(SituacaoDaSincronizacao.NaoConfigurado, estado.Situacao);
        Assert.Empty(_fonte.Chamadas);
        Assert.Single(_repositorio.Itens); // nada foi apagado por falta de configuração
    }

    [Fact]
    public void Reiniciar_DependeDaConfiguracao()
    {
        _fonte.EstaConfigurada = false;
        _servico.Reiniciar();
        Assert.Equal(SituacaoDaSincronizacao.NaoConfigurado, _servico.Estado.Situacao);

        _fonte.EstaConfigurada = true;
        _servico.Reiniciar();
        Assert.Equal(SituacaoDaSincronizacao.Aguardando, _servico.Estado.Situacao);
    }

    // ---------- nunca duas ao mesmo tempo ----------

    [Fact]
    public async Task DuasChamadasJuntas_SoUmaSincronizacaoRoda()
    {
        _fonte.Liberacao = new TaskCompletionSource();
        _fonte.Itens = [Notion("a", "A", new DateTime(2026, 11, 12, 9, 0, 0))];

        var primeira = _servico.SincronizarAsync();
        var segunda = await _servico.SincronizarAsync(manual: true); // chega com a primeira em andamento

        Assert.Equal(SituacaoDaSincronizacao.Sincronizando, segunda.Situacao);
        Assert.Single(_fonte.Chamadas);

        _fonte.Liberacao.SetResult();
        var resultado = await primeira;

        Assert.Equal(SituacaoDaSincronizacao.Ok, resultado.Situacao);
        Assert.Single(_fonte.Chamadas);
        Assert.Equal(1, _repositorio.Sincronizacoes);
    }

    [Fact]
    public async Task ExecutarSemSincronizacao_EsperaASincronizacaoEmAndamentoTerminar()
    {
        _fonte.Liberacao = new TaskCompletionSource();
        var sincronizacao = _servico.SincronizarAsync();
        var executou = false;

        var exclusivo = _servico.ExecutarSemSincronizacaoAsync(() =>
        {
            executou = true;
            return Task.FromResult(42);
        });

        await Task.Delay(50);
        Assert.False(executou); // ainda esperando a sincronização acabar

        _fonte.Liberacao.SetResult();
        await sincronizacao;
        Assert.Equal(42, await exclusivo);
        Assert.True(executou);
    }

    [Fact]
    public async Task OuvinteDeEventoComDefeito_NaoDerrubaASincronizacao()
    {
        _servico.EstadoAlterado += _ => throw new InvalidOperationException("ouvinte quebrado");
        _servico.DadosAlterados += () => throw new InvalidOperationException("ouvinte quebrado");
        _fonte.Itens = [Notion("a", "A", new DateTime(2026, 11, 12, 9, 0, 0))];

        var estado = await _servico.SincronizarAsync();

        Assert.Equal(SituacaoDaSincronizacao.Ok, estado.Situacao);
        Assert.Single(_repositorio.Itens);
    }

    private sealed class RepositorioQueFalhaNoSync : IAgendamentoRepository
    {
        public Task<ResultadoSincronizacao> SincronizarAsync(
            OrigemAgendamento origem, DateTime inicio, DateTime fim, IReadOnlyList<Agendamento> itensRecebidos,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Login failed for user; senha-do-banco");

        public Task<Agendamento?> ObterPorIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Agendamento>> ListarPorPeriodoAsync(DateTime inicio, DateTime fim, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AdicionarAsync(Agendamento agendamento, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AtualizarAsync(Agendamento agendamento, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AdicionarSerieAsync(Agendamento original, IReadOnlyList<Agendamento> copias, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Agendamento>> ListarPorOrigemAsync(OrigemAgendamento origem, DateTime inicio, DateTime fim, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
