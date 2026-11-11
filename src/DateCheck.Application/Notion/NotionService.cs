using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;
using DateCheck.Application.Servicos;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Notion;

public sealed class NotionService : INotionService
{
    private readonly ICofreDeSegredos _cofre;
    private readonly IConfiguracaoNotion _configuracao;
    private readonly INotionApi _api;
    private readonly IAgendamentoRepository _repositorio;
    private readonly ISincronizacaoExternaService _sincronizacao;

    public NotionService(
        ICofreDeSegredos cofre,
        IConfiguracaoNotion configuracao,
        INotionApi api,
        IAgendamentoRepository repositorio,
        ISincronizacaoExternaService sincronizacao)
    {
        _cofre = cofre;
        _configuracao = configuracao;
        _api = api;
        _repositorio = repositorio;
        _sincronizacao = sincronizacao;
    }

    public bool EstaConectado =>
        _configuracao.Obter() is not null && !string.IsNullOrEmpty(_cofre.Ler(SegredosDoNotion.Token));

    public bool TemTokenSalvo => !string.IsNullOrWhiteSpace(_cofre.Ler(SegredosDoNotion.Token));

    public ConfiguracaoNotion? ConfiguracaoAtual => _configuracao.Obter();

    public Task<EstruturaDoBancoNotion> InspecionarAsync(
        string token, string linkOuIdDoBanco, CancellationToken cancellationToken = default)
    {
        var tokenLimpo = ResolverToken(token);

        var id = NotionLinks.ExtrairIdDoBanco(linkOuIdDoBanco)
            ?? throw new AplicacaoException(
                "Não encontrei o id de um banco do Notion nesse link. Copie o link do banco no Notion (menu \"…\" > Copiar link) e cole aqui.");

        return _api.ObterEstruturaAsync(tokenLimpo, id, cancellationToken);
    }

    public Task ConectarAsync(
        string token,
        EstruturaDoBancoNotion estrutura,
        string dataSourceId,
        string propriedadeData,
        ConclusaoNotion? conclusao,
        CancellationToken cancellationToken = default)
    {
        var tokenLimpo = ResolverToken(token);
        var tokenNovo = !string.IsNullOrWhiteSpace(token);

        var fonte = estrutura.FontesDeDados.FirstOrDefault(f => f.Id == dataSourceId)
            ?? throw new AplicacaoException("Escolha um dos data sources do banco.");

        if (!fonte.PropriedadesDeData.Any(p => p.Nome == propriedadeData))
            throw new AplicacaoException("Escolha a propriedade de data (tipo \"date\") do banco.");

        if (conclusao is not null)
            ValidarConclusao(fonte, conclusao);

        // Só grava depois de validar tudo: uma escolha inválida não deixa token nem configuração pela metade.
        // Sem token digitado, o do cofre foi o usado: não há o que regravar.
        if (tokenNovo)
            _cofre.Gravar(SegredosDoNotion.Token, tokenLimpo);
        _configuracao.Salvar(new ConfiguracaoNotion(estrutura.DatabaseId, dataSourceId, propriedadeData, conclusao));
        _sincronizacao.Reiniciar();
        return Task.CompletedTask;
    }

    public async Task<string> TestarConexaoAsync(CancellationToken cancellationToken = default)
    {
        var token = _cofre.Ler(SegredosDoNotion.Token);
        var configuracao = _configuracao.Obter();
        if (string.IsNullOrEmpty(token) || configuracao is null)
            throw new AplicacaoException("O Notion ainda não está conectado.");

        var identidade = await _api.ObterIdentidadeAsync(token, cancellationToken);
        var estrutura = await _api.ObterEstruturaAsync(token, configuracao.DatabaseId, cancellationToken);

        var fonte = estrutura.FontesDeDados.FirstOrDefault(f => f.Id == configuracao.DataSourceId)
            ?? throw new AplicacaoException("O data source escolhido não existe mais. Reconecte o banco em Configurações > Notion.");

        if (!fonte.PropriedadesDeData.Any(p => p.Nome == configuracao.PropriedadeData))
            throw new AplicacaoException(
                $"A propriedade de data \"{configuracao.PropriedadeData}\" não existe mais no banco. Escolha de novo em Configurações > Notion.");

        if (configuracao.Conclusao is { } conclusao)
            ValidarConclusao(fonte, conclusao);

        return $"Conectado como {identidade}. Banco \"{estrutura.Titulo}\" acessível.";
    }

    public Task<int> DesconectarAsync(CancellationToken cancellationToken = default)
    {
        // Sem sincronização rodando: uma em andamento poderia reinserir itens logo depois da remoção.
        return _sincronizacao.ExecutarSemSincronizacaoAsync(async () =>
        {
            // Remove os itens primeiro: se falhar, nada mais mudou e o usuário pode tentar de novo. A janela inteira
            // (de DateTime.MinValue a MaxValue) e a lista vazia removem todos os itens importados do Notion, e só eles.
            var resultado = await _repositorio.SincronizarAsync(
                OrigemAgendamento.Notion, DateTime.MinValue, DateTime.MaxValue, [], cancellationToken);

            _cofre.Apagar(SegredosDoNotion.Token);
            _configuracao.Limpar();
            _sincronizacao.Reiniciar();

            return resultado.Removidos;
        }, cancellationToken);
    }

    /// <summary>
    /// O token digitado, se houver; senão o que já está no cofre (ele nunca volta para a tela, então reconfigurar
    /// o banco ou as propriedades não exige colar de novo). Sem nenhum dos dois, pede o token.
    /// </summary>
    private string ResolverToken(string? digitado)
    {
        if (!string.IsNullOrWhiteSpace(digitado))
            return digitado.Trim();

        var salvo = _cofre.Ler(SegredosDoNotion.Token);
        if (!string.IsNullOrWhiteSpace(salvo))
            return salvo;

        throw new AplicacaoException("Cole o token de API da conexão do Notion (aba Configuration em app.notion.com/developers/connections).");
    }

    private static void ValidarConclusao(FonteDeDadosNotion fonte, ConclusaoNotion conclusao)
    {
        var propriedade = fonte.Propriedades.FirstOrDefault(p => p.Nome == conclusao.Propriedade)
            ?? throw new AplicacaoException($"A propriedade \"{conclusao.Propriedade}\" não existe no banco.");

        if (conclusao.Tipo == TipoDeConclusaoNotion.Checkbox)
        {
            if (propriedade.Tipo != TiposDePropriedadeNotion.Checkbox)
                throw new AplicacaoException($"A propriedade \"{conclusao.Propriedade}\" não é um checkbox.");
            return;
        }

        if (propriedade.Tipo != TiposDePropriedadeNotion.Status)
            throw new AplicacaoException($"A propriedade \"{conclusao.Propriedade}\" não é um status.");

        if (string.IsNullOrWhiteSpace(conclusao.ValorConcluido)
            || !propriedade.Opcoes.Contains(conclusao.ValorConcluido, StringComparer.OrdinalIgnoreCase))
            throw new AplicacaoException("Escolha qual valor do status significa \"concluído\".");
    }
}
