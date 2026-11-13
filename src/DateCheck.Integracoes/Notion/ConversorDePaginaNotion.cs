using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Notion;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using DateCheck.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace DateCheck.Integracoes.Notion;

/// <summary>
/// Converte uma página do Notion em <see cref="Agendamento"/> importado.
/// <list type="table">
/// <item><term>data sem horário, sem fim</term><description>dia inteiro, 1 dia</description></item>
/// <item><term>data sem horário, com fim</term><description>dia inteiro, do início até o fim (inclusive)</description></item>
/// <item><term>com horário e fim</term><description>início e fim em horário local</description></item>
/// <item><term>com horário, sem fim</term><description>duração padrão de 1 hora</description></item>
/// <item><term>fim ≤ início</term><description>duração padrão de 1 hora e aviso no log</description></item>
/// <item><term>sem a data preenchida</term><description>ignorada (retorna nulo)</description></item>
/// </list>
/// Datas com fuso (<c>Z</c>, <c>-03:00</c>) são convertidas para o horário local da máquina.
/// </summary>
public sealed partial class ConversorDePaginaNotion
{
    public static readonly TimeSpan DuracaoPadrao = TimeSpan.FromHours(1);
    public const string TituloPadrao = "(sem título)";

    private readonly ILogger<ConversorDePaginaNotion> _logger;
    private readonly TimeZoneInfo _fusoLocal;

    public ConversorDePaginaNotion(ILogger<ConversorDePaginaNotion> logger, TimeZoneInfo? fusoLocal = null)
    {
        _logger = logger;
        _fusoLocal = fusoLocal ?? TimeZoneInfo.Local;
    }

    /// <summary>
    /// Retorna nulo para páginas que não entram (lixeira/arquivada, sem data ou impossíveis de converter).
    /// </summary>
    /// <exception cref="FonteExternaException">
    /// A propriedade configurada não existe mais na página ou mudou de tipo: configuração desatualizada. Falhar
    /// é de propósito: ignorar todas as páginas faria a sincronização remover tudo o que foi importado.
    /// </exception>
    public Agendamento? Converter(JsonElement pagina, ConfiguracaoNotion configuracao)
    {
        if (pagina.ValueKind != JsonValueKind.Object || !pagina.TryGetProperty("id", out var idJson)
            || idJson.GetString() is not { Length: > 0 } id)
            throw RespostaInvalida("Uma página do Notion veio sem identificador.");

        // Lixeira: "in_trash" (API atual); "archived" (APIs anteriores), se vier.
        if (EhVerdadeiro(pagina, "in_trash") || EhVerdadeiro(pagina, "archived"))
            return null;

        if (!pagina.TryGetProperty("properties", out var propriedades) || propriedades.ValueKind != JsonValueKind.Object)
            throw RespostaInvalida("Uma página do Notion veio sem propriedades.");

        var propriedadeDeData = ObterPropriedade(propriedades, configuracao.PropriedadeData, TiposDePropriedadeNotion.Data);
        if (!propriedadeDeData.TryGetProperty("date", out var data) || data.ValueKind != JsonValueKind.Object)
            return null; // sem a data preenchida: ignorada

        var titulo = ObterTitulo(propriedades);
        var status = ObterStatus(propriedades, configuracao.Conclusao);

        try
        {
            if (!TentarObterPeriodo(id, data, out var inicio, out var fim, out var diaInteiro))
                return null;

            return Agendamento.Importar(
                OrigemAgendamento.Notion,
                id,
                ObterUrl(pagina),
                titulo,
                descricao: null,
                inicio,
                fim,
                diaInteiro,
                status);
        }
        catch (Exception ex) when (ex is DomainException or ArgumentOutOfRangeException or FormatException)
        {
            // Dado que o app não consegue representar (ano absurdo, id enorme...): ignora só esta página.
            _logger.LogWarning(ex, "Página {PaginaId} do Notion ignorada: não pôde ser convertida.", id);
            return null;
        }
    }

    // ---------- título, status, url ----------

    private static string ObterTitulo(JsonElement propriedades)
    {
        foreach (var propriedade in propriedades.EnumerateObject())
        {
            if (propriedade.Value.ValueKind != JsonValueKind.Object
                || !propriedade.Value.TryGetProperty("type", out var tipo)
                || tipo.GetString() != TiposDePropriedadeNotion.Titulo)
                continue;

            var texto = string.Empty;
            if (propriedade.Value.TryGetProperty("title", out var partes) && partes.ValueKind == JsonValueKind.Array)
            {
                texto = string.Concat(partes.EnumerateArray()
                    .Select(p => p.TryGetProperty("plain_text", out var t) ? t.GetString() : null));
            }

            return Truncar(texto, Agendamento.TamanhoMaximoTitulo) is { Length: > 0 } limpo ? limpo : TituloPadrao;
        }

        return TituloPadrao;
    }

    private StatusAgendamento ObterStatus(JsonElement propriedades, ConclusaoNotion? conclusao)
    {
        if (conclusao is null)
            return StatusAgendamento.Pendente; // sem propriedade configurada, tudo entra como pendente

        var tipoEsperado = conclusao.Tipo == TipoDeConclusaoNotion.Checkbox
            ? TiposDePropriedadeNotion.Checkbox
            : TiposDePropriedadeNotion.Status;
        var propriedade = ObterPropriedade(propriedades, conclusao.Propriedade, tipoEsperado);

        if (conclusao.Tipo == TipoDeConclusaoNotion.Checkbox)
        {
            return propriedade.TryGetProperty("checkbox", out var marcado) && marcado.ValueKind == JsonValueKind.True
                ? StatusAgendamento.Concluido
                : StatusAgendamento.Pendente;
        }

        var nome = propriedade.TryGetProperty("status", out var opcao) && opcao.ValueKind == JsonValueKind.Object
            && opcao.TryGetProperty("name", out var n)
                ? n.GetString()
                : null;

        return nome is not null && string.Equals(nome, conclusao.ValorConcluido, StringComparison.OrdinalIgnoreCase)
            ? StatusAgendamento.Concluido
            : StatusAgendamento.Pendente;
    }

    private static string? ObterUrl(JsonElement pagina)
    {
        if (!pagina.TryGetProperty("url", out var url) || url.GetString() is not { Length: > 0 } texto)
            return null;

        // Um link maior que o limite é descartado (o item continua importado, só sem "Abrir no Notion").
        return texto.Length <= Agendamento.TamanhoMaximoUrlExterna ? texto : null;
    }

    private static JsonElement ObterPropriedade(JsonElement propriedades, string nome, string tipoEsperado)
    {
        if (!propriedades.TryGetProperty(nome, out var propriedade) || propriedade.ValueKind != JsonValueKind.Object)
            throw RespostaInvalida(
                $"A propriedade \"{nome}\" não existe mais no banco do Notion. Abra Configurações > Notion e escolha de novo.");

        if (!propriedade.TryGetProperty("type", out var tipo) || tipo.GetString() != tipoEsperado)
            throw RespostaInvalida(
                $"A propriedade \"{nome}\" do Notion mudou de tipo (era esperado \"{tipoEsperado}\"). Abra Configurações > Notion e escolha de novo.");

        return propriedade;
    }

    // ---------- datas ----------

    private bool TentarObterPeriodo(
        string paginaId, JsonElement data, out DateTime inicio, out DateTime fim, out bool diaInteiro)
    {
        inicio = fim = default;
        diaInteiro = false;

        var textoInicio = data.TryGetProperty("start", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(textoInicio))
            return false; // sem início: tratada como sem data

        var textoFim = data.TryGetProperty("end", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        var fuso = data.TryGetProperty("time_zone", out var z) && z.ValueKind == JsonValueKind.String ? z.GetString() : null;

        if (TemHorario(textoInicio))
        {
            inicio = ParaHorarioLocal(textoInicio, fuso);
            if (textoFim is null)
            {
                fim = inicio + DuracaoPadrao;
                return true;
            }

            fim = TemHorario(textoFim) ? ParaHorarioLocal(textoFim, fuso) : ParaDiaInteiro(textoFim);
            if (fim <= inicio)
            {
                _logger.LogWarning(
                    "Página {PaginaId} do Notion tem fim ({Fim}) anterior ou igual ao início ({Inicio}); usando duração padrão de 1 hora.",
                    paginaId, fim, inicio);
                fim = inicio + DuracaoPadrao;
            }

            return true;
        }

        // Só data: dia inteiro. O fim do Notion é inclusivo; o do app é a meia-noite do dia seguinte.
        diaInteiro = true;
        inicio = ParaDiaInteiro(textoInicio);
        var ultimoDia = textoFim is null ? inicio : ParaDiaInteiro(textoFim);
        if (ultimoDia < inicio)
        {
            _logger.LogWarning(
                "Página {PaginaId} do Notion tem data final ({Fim:yyyy-MM-dd}) anterior à inicial ({Inicio:yyyy-MM-dd}); usando 1 dia.",
                paginaId, ultimoDia, inicio);
            ultimoDia = inicio;
        }

        fim = ultimoDia.AddDays(1);
        return true;
    }

    private static bool TemHorario(string texto) => texto.Contains('T');

    /// <summary>Data pura ("2026-11-10"), sem conversão de fuso. Aceita ainda o dia de um texto com horário.</summary>
    private static DateTime ParaDiaInteiro(string texto) =>
        DateTime.ParseExact(texto.AsSpan(0, Math.Min(10, texto.Length)), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private DateTime ParaHorarioLocal(string texto, string? idDoFuso)
    {
        if (TemDeslocamento().IsMatch(texto))
        {
            var instante = DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None);
            return TimeZoneInfo.ConvertTime(instante, _fusoLocal).DateTime;
        }

        // Sem deslocamento: vale o fuso informado ("America/New_York"); sem ele, o horário é "flutuante" (local).
        var semFuso = DateTime.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None);
        semFuso = DateTime.SpecifyKind(semFuso, DateTimeKind.Unspecified);

        var origem = ProcurarFuso(idDoFuso);
        return origem is null ? semFuso : TimeZoneInfo.ConvertTime(semFuso, origem, _fusoLocal);
    }

    private TimeZoneInfo? ProcurarFuso(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning("Fuso horário \"{Fuso}\" desconhecido; usando o horário local.", id);
            return null;
        }
    }

    // ---------- utilidades ----------

    /// <summary>Apara e corta no limite sem partir um par substituto (emoji) ao meio.</summary>
    public static string Truncar(string? texto, int maximo)
    {
        var limpo = (texto ?? string.Empty).Trim();
        if (limpo.Length <= maximo)
            return limpo;

        var corte = maximo;
        if (char.IsHighSurrogate(limpo[corte - 1]))
            corte--;

        return limpo[..corte].TrimEnd();
    }

    private static bool EhVerdadeiro(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.True;

    private static FonteExternaException RespostaInvalida(string mensagem) =>
        new(TipoDeFalhaExterna.RespostaInvalida, mensagem);

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex TemDeslocamento();
}
