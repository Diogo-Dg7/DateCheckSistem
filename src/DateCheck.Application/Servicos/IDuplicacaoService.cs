using DateCheck.Application.Dtos;

namespace DateCheck.Application.Servicos;

public interface IDuplicacaoService
{
    /// <summary>
    /// Cria uma cópia avulsa (pendente, sem série) do agendamento começando em <paramref name="novoInicio"/>,
    /// mantendo título, descrição e duração. Conflitos não bloqueiam; consulte <see cref="IConflitoService"/> antes de salvar.
    /// </summary>
    Task<AgendamentoDto> DuplicarAsync(
        int id, DateTime novoInicio, CancellationToken cancellationToken = default);
}
