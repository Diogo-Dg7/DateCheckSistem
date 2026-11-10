using DateCheck.Application.Servicos;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Servicos;
using Microsoft.Extensions.Logging;

namespace DateCheck.Desktop.Integracoes;

/// <summary>Mostra um aviso na bandeja (balão).</summary>
public interface IAvisoDeBandeja
{
    void MostrarBalao(string titulo, string mensagem);
}

/// <summary>
/// O que o app faz quando a sincronização avisa algo (os eventos vêm de thread de segundo plano; aqui voltamos
/// para a thread da interface): recarrega a tela aberta quando os dados mudam e mostra UM balão na bandeja
/// quando o Notion precisa ser reconectado.
/// </summary>
public sealed class ReacoesDaSincronizacao : IDisposable
{
    public const string MensagemDeReconexao =
        "O Notion recusou o token. Abra Configurações > Notion para reconectar.";

    private readonly ISincronizacaoExternaService _sincronizacao;
    private readonly INavegacao _navegacao;
    private readonly IAvisoDeBandeja _bandeja;
    private readonly ILogger<ReacoesDaSincronizacao> _logger;
    private readonly SynchronizationContext? _contexto = SynchronizationContext.Current;

    public ReacoesDaSincronizacao(
        ISincronizacaoExternaService sincronizacao,
        INavegacao navegacao,
        IAvisoDeBandeja bandeja,
        ILogger<ReacoesDaSincronizacao> logger)
    {
        _sincronizacao = sincronizacao;
        _navegacao = navegacao;
        _bandeja = bandeja;
        _logger = logger;

        _sincronizacao.DadosAlterados += AoAlterarDados;
        _sincronizacao.ReconexaoNecessaria += AoPrecisarReconectar;
    }

    public void Dispose()
    {
        _sincronizacao.DadosAlterados -= AoAlterarDados;
        _sincronizacao.ReconexaoNecessaria -= AoPrecisarReconectar;
    }

    private void AoAlterarDados() => NaInterface(async () =>
    {
        try
        {
            await _navegacao.RecarregarAtualAsync();
        }
        catch (Exception ex)
        {
            // Banco indisponível, por exemplo: a tela mantém o que mostrava e a atualização periódica corrige.
            _logger.LogWarning(ex, "Não foi possível recarregar a tela depois da sincronização.");
        }
    });

    private void AoPrecisarReconectar() => NaInterface(() =>
    {
        _bandeja.MostrarBalao("DateCheck", MensagemDeReconexao);
        return Task.CompletedTask;
    });

    private void NaInterface(Func<Task> acao)
    {
        if (_contexto is null)
            Iniciar(acao);
        else
            _contexto.Post(_ => Iniciar(acao), null);
    }

    private static void Iniciar(Func<Task> acao) => _ = acao();
}
