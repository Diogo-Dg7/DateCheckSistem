namespace DateCheck.Domain.Exceptions;

/// <summary>Violação de uma regra de negócio ou invariante do domínio.</summary>
public class DomainException : Exception
{
    public DomainException(string mensagem) : base(mensagem)
    {
    }
}
