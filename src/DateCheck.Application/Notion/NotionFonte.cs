using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Notion;

/// <summary>
/// O Notion como <see cref="IFonteDeAgendamentosExterna"/>: junta o token (cofre), a configuração e o cliente da API.
/// </summary>
public sealed class NotionFonte : IFonteDeAgendamentosExterna
{
    private readonly ICofreDeSegredos _cofre;
    private readonly IConfiguracaoNotion _configuracao;
    private readonly INotionApi _api;

    public NotionFonte(ICofreDeSegredos cofre, IConfiguracaoNotion configuracao, INotionApi api)
    {
        _cofre = cofre;
        _configuracao = configuracao;
        _api = api;
    }

    public OrigemAgendamento Origem => OrigemAgendamento.Notion;

    public bool EstaConfigurada =>
        _configuracao.Obter() is not null && !string.IsNullOrEmpty(_cofre.Ler(SegredosDoNotion.Token));

    /// <summary>
    /// Pede ao Notion de 1 dia antes de <paramref name="inicio"/> até o dia de <paramref name="fim"/> (inclusive):
    /// 1 dia de folga em cada ponta. O Notion filtra por data, sem fuso e sem dizer se compara o início ou a
    /// sobreposição do intervalo; a folga garante que nada com <c>Inicio</c> local dentro da janela deixe de vir por
    /// causa de fuso nas bordas. Quem sincroniza descarta o que sobrar fora de [inicio, fim).
    /// </summary>
    public async Task<IReadOnlyList<Agendamento>> ListarAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        var token = _cofre.Ler(SegredosDoNotion.Token);
        if (string.IsNullOrEmpty(token))
            throw new FonteExternaException(
                TipoDeFalhaExterna.NaoAutorizado, "O token do Notion não foi encontrado. Reconecte o Notion em Configurações.");

        var configuracao = _configuracao.Obter()
            ?? throw new FonteExternaException(
                TipoDeFalhaExterna.SemAcesso, "O Notion ainda não foi configurado. Abra Configurações > Notion.");

        var de = DateOnly.FromDateTime(inicio.Date.AddDays(-1));
        var ate = DateOnly.FromDateTime(fim.Date);

        return await _api.ConsultarAsync(token, configuracao, de, ate, cancellationToken);
    }
}
