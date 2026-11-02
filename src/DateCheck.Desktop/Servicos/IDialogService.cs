namespace DateCheck.Desktop.Servicos;

/// <summary>Interações modais com o usuário; permite testar ViewModels sem abrir janelas.</summary>
public interface IDialogService
{
    void MostrarErro(string mensagem);

    void MostrarAviso(string mensagem);

    /// <summary>Pergunta sim/não; retorna true para "sim".</summary>
    bool Confirmar(string titulo, string mensagem);

    /// <summary>Retorna true para "tentar novamente" e false para "sair".</summary>
    bool PerguntarTentarNovamente(string titulo, string mensagem);

    /// <summary>
    /// Abre o formulário modal para criar (<paramref name="agendamentoId"/> nulo) ou editar um compromisso.
    /// <paramref name="diaSugerido"/> pré-preenche a data de um novo compromisso. Retorna true se algo foi salvo.
    /// </summary>
    bool AbrirFormulario(int? agendamentoId, DateTime? diaSugerido = null);

    /// <summary>Abre a janela de configurações; retorna true se o usuário salvou.</summary>
    bool AbrirConfiguracoes();

    /// <summary>Pede uma data e hora; retorna nulo se o usuário cancelar.</summary>
    DateTime? PedirDataHora(string titulo, string rotulo, DateTime inicial);
}
