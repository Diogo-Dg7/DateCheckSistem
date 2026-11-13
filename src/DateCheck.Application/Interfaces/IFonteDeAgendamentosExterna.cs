using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Interfaces;

/// <summary>Uma origem de compromissos de fora do app (hoje, o Notion), lida em uma só direção.</summary>
public interface IFonteDeAgendamentosExterna
{
    OrigemAgendamento Origem { get; }

    /// <summary>Há credenciais e configuração para consultar a origem.</summary>
    bool EstaConfigurada { get; }

    /// <summary>
    /// Lista os itens da origem na janela. A lista é completa ou a chamada falha: em QUALQUER problema (rede, 401,
    /// 429 esgotado, resposta incompleta) lança <see cref="Excecoes.FonteExternaException"/> e não devolve nada,
    /// porque uma lista parcial faria a sincronização apagar itens válidos. A fonte pode devolver itens além da
    /// janela exata (margem de segurança de fuso); quem sincroniza faz o corte exato por <c>Inicio</c>.
    /// </summary>
    Task<IReadOnlyList<Agendamento>> ListarAsync(DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);
}
