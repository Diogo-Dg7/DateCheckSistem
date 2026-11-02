using DateCheck.Application.Dtos;

namespace DateCheck.Application.Servicos;

public interface IRepeticaoService
{
    /// <summary>Calcula as cópias e seus conflitos sem salvar nada, para a tela perguntar antes de gravar.</summary>
    Task<ResultadoRepeticao> PrevisualizarAsync(
        RepetirRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grava as cópias (todas com o mesmo <c>SerieId</c>, inclusive o original) e retorna os conflitos por cópia.
    /// Conflitos nunca bloqueiam a gravação.
    /// </summary>
    Task<ResultadoRepeticao> RepetirAsync(
        RepetirRequest request, CancellationToken cancellationToken = default);
}
