using DateCheck.Application.Dtos;

namespace DateCheck.Application.Servicos;

public enum SituacaoDaSincronizacao
{
    /// <summary>Sem token ou sem banco escolhido: nada roda em segundo plano.</summary>
    NaoConfigurado,

    /// <summary>Configurado, ainda sem nenhuma sincronização nesta sessão.</summary>
    Aguardando,

    Sincronizando,

    Ok,

    /// <summary>Token recusado (401): o usuário precisa reconectar. O segundo plano para de tentar.</summary>
    ReconecteNecessario,

    /// <summary>Rede, API fora do ar ou outro erro; a próxima rodada tenta de novo. Nada foi apagado.</summary>
    Erro
}

/// <param name="UltimaSincronizacao">Fim da última sincronização que deu certo (horário local).</param>
/// <param name="Erro">Mensagem para o usuário; nunca contém segredos.</param>
public sealed record EstadoDaSincronizacao(
    SituacaoDaSincronizacao Situacao,
    DateTime? UltimaSincronizacao = null,
    string? Erro = null,
    ResultadoSincronizacao? UltimoResultado = null)
{
    public static EstadoDaSincronizacao NaoConfigurado { get; } = new(SituacaoDaSincronizacao.NaoConfigurado);
}

/// <summary>Sincroniza uma origem externa (Notion → DateCheck, mão única) e expõe o estado para as Configurações.</summary>
public interface ISincronizacaoExternaService
{
    EstadoDaSincronizacao Estado { get; }

    /// <summary>A cada mudança de estado (pode vir de thread de segundo plano).</summary>
    event Action<EstadoDaSincronizacao>? EstadoAlterado;

    /// <summary>Depois de uma sincronização que inseriu, atualizou ou removeu algo: as telas devem recarregar.</summary>
    event Action? DadosAlterados;

    /// <summary>Disparado uma única vez ao entrar em "Reconecte o Notion" (para o balão da bandeja).</summary>
    event Action? ReconexaoNecessaria;

    /// <summary>
    /// Roda uma sincronização. Nunca duas ao mesmo tempo: se já há uma em andamento, retorna o estado atual sem
    /// iniciar outra. Automática (<paramref name="manual"/> falso) não insiste enquanto o estado é
    /// <see cref="SituacaoDaSincronizacao.ReconecteNecessario"/>. Nunca lança por falha da origem: vira estado.
    /// </summary>
    Task<EstadoDaSincronizacao> SincronizarAsync(bool manual = false, CancellationToken cancellationToken = default);

    /// <summary>Volta ao estado inicial (depois de conectar, reconectar ou desconectar).</summary>
    void Reiniciar();

    /// <summary>Executa <paramref name="acao"/> sem nenhuma sincronização rodando (espera a atual terminar).</summary>
    Task<TResultado> ExecutarSemSincronizacaoAsync<TResultado>(
        Func<Task<TResultado>> acao, CancellationToken cancellationToken = default);
}
