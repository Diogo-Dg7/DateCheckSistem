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
}
