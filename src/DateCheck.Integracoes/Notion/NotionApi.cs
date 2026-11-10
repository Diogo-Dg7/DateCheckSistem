using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;
using DateCheck.Application.Notion;
using DateCheck.Domain.Entidades;
using Microsoft.Extensions.Logging;

namespace DateCheck.Integracoes.Notion;

/// <summary>
/// Cliente da API REST do Notion com <see cref="HttpClient"/> e <c>System.Text.Json</c> (sem SDK de terceiros, que
/// ficam atrás das mudanças da API). Fala em uma só direção: só lê. Toda falha vira <see cref="FonteExternaException"/>
/// com mensagem para o usuário; o token nunca aparece em mensagem, exceção ou log.
/// </summary>
public sealed class NotionApi : INotionApi
{
    /// <summary>Versão fixada da API (header obrigatório <c>Notion-Version</c>). Ver "API do Notion" no CLAUDE.md.</summary>
    public const string VersaoDaApi = "2026-03-11";

    public const string NomeDoHttpClient = "notion";

    /// <summary>Máximo aceito pelo Notion por página de resultados.</summary>
    public const int TamanhoDaPagina = 100;

    /// <summary>
    /// Máximo de páginas de resultados que o DateCheck percorre numa consulta (150 × 100 = 15.000 itens). O Notion
    /// já limita uma consulta a 10.000 resultados e avisa com <c>request_status: incomplete</c>; este teto é só a
    /// rede de segurança contra um laço sem fim se o cursor nunca acabar. Passou dele, a sincronização falha inteira.
    /// </summary>
    public const int LimiteDePaginas = 150;

    public const int LimiteDeItens = LimiteDePaginas * TamanhoDaPagina;

    /// <summary>Limite de resultados de uma consulta, imposto pelo próprio Notion.</summary>
    public const int LimiteDoNotionPorConsulta = 10_000;

    private static readonly JsonSerializerOptions OpcoesDeEscrita = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _fabrica;
    private readonly ConversorDePaginaNotion _conversor;
    private readonly ILogger<NotionApi> _logger;

    public NotionApi(IHttpClientFactory fabrica, ConversorDePaginaNotion conversor, ILogger<NotionApi> logger)
    {
        _fabrica = fabrica;
        _conversor = conversor;
        _logger = logger;
    }

    // ---------- identidade ----------

    public async Task<string> ObterIdentidadeAsync(string token, CancellationToken cancellationToken = default)
    {
        using var documento = await EnviarAsync(HttpMethod.Get, "v1/users/me", token, corpo: null, cancellationToken);
        var raiz = documento.RootElement;

        var nome = Texto(raiz, "name");
        var workspace = raiz.TryGetProperty("bot", out var bot) && bot.ValueKind == JsonValueKind.Object
            ? Texto(bot, "workspace_name")
            : null;

        return (nome, workspace) switch
        {
            ({ Length: > 0 } n, { Length: > 0 } w) => $"{n} ({w})",
            ({ Length: > 0 } n, _) => n,
            (_, { Length: > 0 } w) => w,
            _ => "integração do Notion"
        };
    }

    // ---------- estrutura do banco ----------

    public async Task<EstruturaDoBancoNotion> ObterEstruturaAsync(
        string token, string databaseId, CancellationToken cancellationToken = default)
    {
        using var banco = await EnviarAsync(
            HttpMethod.Get, $"v1/databases/{Uri.EscapeDataString(databaseId)}", token, corpo: null, cancellationToken);

        var titulo = TextoRico(banco.RootElement, "title");
        var fontes = new List<FonteDeDadosNotion>();

        if (banco.RootElement.TryGetProperty("data_sources", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            foreach (var fonte in lista.EnumerateArray())
            {
                var id = Texto(fonte, "id");
                if (string.IsNullOrEmpty(id))
                    continue;

                var nome = Texto(fonte, "name") ?? id;
                fontes.Add(await ObterFonteDeDadosAsync(token, id, nome, cancellationToken));
            }
        }

        if (fontes.Count == 0)
            throw new FonteExternaException(
                TipoDeFalhaExterna.RespostaInvalida, "O Notion não informou nenhuma fonte de dados para esse banco.");

        return new EstruturaDoBancoNotion(databaseId, string.IsNullOrWhiteSpace(titulo) ? "(sem nome)" : titulo, fontes);
    }

    private async Task<FonteDeDadosNotion> ObterFonteDeDadosAsync(
        string token, string id, string nome, CancellationToken cancellationToken)
    {
        using var documento = await EnviarAsync(
            HttpMethod.Get, $"v1/data_sources/{Uri.EscapeDataString(id)}", token, corpo: null, cancellationToken);

        var propriedades = new List<PropriedadeNotion>();
        if (documento.RootElement.TryGetProperty("properties", out var mapa) && mapa.ValueKind == JsonValueKind.Object)
        {
            foreach (var entrada in mapa.EnumerateObject())
            {
                var tipo = Texto(entrada.Value, "type") ?? "desconhecido";
                var nomeDaPropriedade = Texto(entrada.Value, "name") ?? entrada.Name;
                propriedades.Add(new PropriedadeNotion(nomeDaPropriedade, tipo, OpcoesDeStatus(entrada.Value, tipo)));
            }
        }

        return new FonteDeDadosNotion(id, nome, propriedades);
    }

    private static IReadOnlyList<string> OpcoesDeStatus(JsonElement propriedade, string tipo)
    {
        if (tipo != TiposDePropriedadeNotion.Status
            || !propriedade.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object
            || !status.TryGetProperty("options", out var opcoes) || opcoes.ValueKind != JsonValueKind.Array)
            return [];

        return opcoes.EnumerateArray().Select(o => Texto(o, "name")).OfType<string>().ToList();
    }

    // ---------- consulta ----------

    public async Task<IReadOnlyList<Agendamento>> ConsultarAsync(
        string token, ConfiguracaoNotion configuracao, DateOnly de, DateOnly ate, CancellationToken cancellationToken = default)
    {
        var caminho = $"v1/data_sources/{Uri.EscapeDataString(configuracao.DataSourceId)}/query" + MontarFiltroDePropriedades(configuracao);
        var itens = new List<Agendamento>();
        string? cursor = null;

        for (var pagina = 1; pagina <= LimiteDePaginas; pagina++)
        {
            var corpo = JsonSerializer.Serialize(MontarCorpoDaConsulta(configuracao.PropriedadeData, de, ate, cursor), OpcoesDeEscrita);
            using var documento = await EnviarConsultaAsync(caminho, token, corpo, configuracao, cancellationToken);
            var raiz = documento.RootElement;

            // Resultado incompleto (ex.: passou de 10 mil itens): é uma lista parcial, que nunca pode alimentar a remoção.
            if (raiz.TryGetProperty("request_status", out var situacao) && situacao.ValueKind == JsonValueKind.Object
                && Texto(situacao, "type") == "incomplete")
                throw new FonteExternaException(
                    TipoDeFalhaExterna.Incompleta,
                    $"O Notion não entregou todos os itens: ele limita cada consulta a {LimiteDoNotionPorConsulta:N0} resultados e esse banco tem mais "
                    + "do que isso entre 90 dias atrás e 365 dias à frente. Nada foi alterado no DateCheck. Para resolver, reduza o banco: "
                    + "arquive ou mova para outro banco os itens que você não precisa ver no calendário.");

            if (!raiz.TryGetProperty("results", out var resultados) || resultados.ValueKind != JsonValueKind.Array)
                throw new FonteExternaException(TipoDeFalhaExterna.RespostaInvalida, "Resposta inesperada do Notion (sem resultados).");

            foreach (var resultado in resultados.EnumerateArray())
            {
                if (resultado.ValueKind == JsonValueKind.Object && Texto(resultado, "object") is "page" or null
                    && _conversor.Converter(resultado, configuracao) is { } item)
                    itens.Add(item);
            }

            var temMais = raiz.TryGetProperty("has_more", out var m) && m.ValueKind == JsonValueKind.True;
            if (!temMais)
                return itens;

            cursor = raiz.TryGetProperty("next_cursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            if (string.IsNullOrEmpty(cursor))
                throw new FonteExternaException(
                    TipoDeFalhaExterna.RespostaInvalida, "Resposta inesperada do Notion (há mais páginas, mas sem cursor).");
        }

        throw new FonteExternaException(
            TipoDeFalhaExterna.Incompleta,
            $"A consulta ao Notion passou de {LimiteDePaginas} páginas ({LimiteDeItens:N0} itens), o máximo que o DateCheck aceita de uma vez. "
            + "Nada foi alterado no DateCheck. Para resolver, reduza o banco: arquive ou mova para outro banco "
            + "os itens que você não precisa ver no calendário.");
    }

    /// <summary>
    /// Envia uma página da consulta. Se o Notion recusar com 400, o motivo mais comum é a propriedade configurada
    /// ter sido renomeada, apagada ou trocada de tipo no Notion (o filtro aponta para algo que não existe): o próprio
    /// Notion recusa a consulta, então a checagem do conversor nem chega a rodar. Nesse caso lê a estrutura do banco
    /// (uma requisição) para dizer ao usuário qual propriedade e o que fazer. Se a estrutura estiver intacta, ou a
    /// leitura falhar, vale o erro original.
    /// </summary>
    private async Task<JsonDocument> EnviarConsultaAsync(
        string caminho, string token, string corpo, ConfiguracaoNotion configuracao, CancellationToken cancellationToken)
    {
        try
        {
            return await EnviarAsync(HttpMethod.Post, caminho, token, corpo, cancellationToken);
        }
        catch (FonteExternaException ex) when (ex.StatusHttp == (int)HttpStatusCode.BadRequest)
        {
            var explicacao = await ExplicarPropriedadeDesatualizadaAsync(token, configuracao, cancellationToken);
            if (explicacao is null)
                throw;

            throw new FonteExternaException(TipoDeFalhaExterna.RespostaInvalida, explicacao, ex);
        }
    }

    private async Task<string?> ExplicarPropriedadeDesatualizadaAsync(
        string token, ConfiguracaoNotion configuracao, CancellationToken cancellationToken)
    {
        FonteDeDadosNotion fonte;
        try
        {
            fonte = await ObterFonteDeDadosAsync(token, configuracao.DataSourceId, configuracao.DataSourceId, cancellationToken);
        }
        catch (FonteExternaException)
        {
            return null;
        }

        var esperadas = new List<(string Nome, string Tipo)> { (configuracao.PropriedadeData, TiposDePropriedadeNotion.Data) };
        if (configuracao.Conclusao is { } conclusao)
            esperadas.Add((conclusao.Propriedade, conclusao.Tipo == TipoDeConclusaoNotion.Status
                ? TiposDePropriedadeNotion.Status
                : TiposDePropriedadeNotion.Checkbox));

        foreach (var (nome, tipo) in esperadas)
        {
            var atual = fonte.Propriedades.FirstOrDefault(p => p.Nome == nome);
            if (atual is null)
                return $"A propriedade \"{nome}\" não existe mais no banco do Notion (foi renomeada ou apagada?). "
                    + "Nada foi alterado no DateCheck. Abra Configurações > Notion e escolha de novo.";

            if (atual.Tipo != tipo)
                return $"A propriedade \"{nome}\" do Notion mudou de tipo (era esperado \"{tipo}\"). "
                    + "Nada foi alterado no DateCheck. Abra Configurações > Notion e escolha de novo.";
        }

        return null;
    }

    private static object MontarCorpoDaConsulta(string propriedadeDeData, DateOnly de, DateOnly ate, string? cursor) => new
    {
        filter = new
        {
            and = new object[]
            {
                new { property = propriedadeDeData, date = new { on_or_after = de.ToString("yyyy-MM-dd") } },
                new { property = propriedadeDeData, date = new { on_or_before = ate.ToString("yyyy-MM-dd") } }
            }
        },
        page_size = TamanhoDaPagina,
        start_cursor = cursor
    };

    /// <summary>Só traz título, data e conclusão: menos dados trafegando.</summary>
    private static string MontarFiltroDePropriedades(ConfiguracaoNotion configuracao)
    {
        var nomes = new List<string> { "title", configuracao.PropriedadeData };
        if (configuracao.Conclusao is { } conclusao)
            nomes.Add(conclusao.Propriedade);

        return "?" + string.Join("&", nomes.Distinct().Select(n => "filter_properties=" + Uri.EscapeDataString(n)));
    }

    // ---------- HTTP ----------

    private async Task<JsonDocument> EnviarAsync(
        HttpMethod metodo, string caminho, string token, string? corpo, CancellationToken cancellationToken)
    {
        using var requisicao = new HttpRequestMessage(metodo, caminho);
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        requisicao.Headers.Add("Notion-Version", VersaoDaApi);
        requisicao.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (corpo is not null)
            requisicao.Content = new StringContent(corpo, Encoding.UTF8, "application/json");

        try
        {
            using var cliente = _fabrica.CreateClient(NomeDoHttpClient);
            using var resposta = await cliente.SendAsync(requisicao, cancellationToken).ConfigureAwait(false);

            if (!resposta.IsSuccessStatusCode)
                throw await TraduzirErroAsync(resposta, caminho, cancellationToken).ConfigureAwait(false);

            await using var fluxo = await resposta.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(fluxo, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new FonteExternaException(TipoDeFalhaExterna.RespostaInvalida, "O Notion respondeu algo que o DateCheck não entende.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new FonteExternaException(TipoDeFalhaExterna.Indisponivel, "Não foi possível falar com o Notion. Verifique a conexão com a internet.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Não foi o chamador que cancelou: estourou o tempo do HttpClient.
            throw new FonteExternaException(TipoDeFalhaExterna.Indisponivel, "O Notion demorou demais para responder.", ex);
        }
    }

    private async Task<FonteExternaException> TraduzirErroAsync(
        HttpResponseMessage resposta, string caminho, CancellationToken cancellationToken)
    {
        var codigo = await LerCodigoDoErroAsync(resposta, cancellationToken).ConfigureAwait(false);
        var status = (int)resposta.StatusCode;

        // Só status e código vão para o log: nunca cabeçalhos, corpo da requisição ou token.
        _logger.LogWarning("Notion respondeu {Status} ({Codigo}) em {Caminho}.", status, codigo ?? "-", CaminhoSemIds(caminho));

        return resposta.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new(TipoDeFalhaExterna.NaoAutorizado,
                "O Notion recusou o token. Reconecte o Notion em Configurações."),
            HttpStatusCode.Forbidden => new(TipoDeFalhaExterna.SemAcesso,
                "A integração não tem permissão para isso no Notion. Confira as permissões dela em notion.so/my-integrations."),
            HttpStatusCode.NotFound => new(TipoDeFalhaExterna.SemAcesso,
                "O Notion não encontrou o banco. Confira o link e se a integração está conectada a ele (menu \"…\" > Conexões)."),
            HttpStatusCode.TooManyRequests => new(TipoDeFalhaExterna.LimiteExcedido,
                "O Notion pediu para diminuir o ritmo das consultas. O DateCheck tentará de novo mais tarde."),
            >= HttpStatusCode.InternalServerError => new(TipoDeFalhaExterna.Indisponivel,
                "O Notion está indisponível no momento. O DateCheck tentará de novo mais tarde."),
            _ => new(TipoDeFalhaExterna.RespostaInvalida,
                $"O Notion recusou o pedido ({status}{(codigo is null ? string.Empty : ", " + codigo)}).",
                causa: null, statusHttp: status)
        };
    }

    private static async Task<string?> LerCodigoDoErroAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
        try
        {
            await using var fluxo = await resposta.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var documento = await JsonDocument.ParseAsync(fluxo, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Texto(documento.RootElement, "code");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string CaminhoSemIds(string caminho) =>
        caminho.Split('?')[0].Split('/') is [var a, var b, ..] ? $"{a}/{b}" : caminho;

    // ---------- JSON ----------

    private static string? Texto(JsonElement objeto, string nome) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    private static string TextoRico(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var partes) && partes.ValueKind == JsonValueKind.Array
            ? string.Concat(partes.EnumerateArray().Select(p => Texto(p, "plain_text")))
            : string.Empty;
}
