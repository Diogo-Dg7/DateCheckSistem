namespace DateCheck.Application.Excecoes;

public class AgendamentoNaoEncontradoException : AplicacaoException
{
    public AgendamentoNaoEncontradoException(int id)
        : base($"Agendamento {id} não encontrado.")
    {
    }
}
