namespace DateCheck.Application.Excecoes;

/// <summary>Violação de uma regra dos casos de uso (não das invariantes do domínio).</summary>
public class AplicacaoException : Exception
{
    public AplicacaoException(string mensagem) : base(mensagem)
    {
    }
}
