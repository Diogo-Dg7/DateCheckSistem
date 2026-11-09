namespace DateCheck.Domain.Enums;

public static class OrigemAgendamentoExtensoes
{
    /// <summary>Nome da origem como aparece para o usuário (selo e mensagens).</summary>
    public static string Nome(this OrigemAgendamento origem) => origem switch
    {
        OrigemAgendamento.Local => "DateCheck",
        OrigemAgendamento.Notion => "Notion",
        _ => origem.ToString()
    };
}
