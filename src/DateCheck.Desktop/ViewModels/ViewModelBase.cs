using CommunityToolkit.Mvvm.ComponentModel;
using DateCheck.Application.Excecoes;
using DateCheck.Desktop.Servicos;
using DateCheck.Domain.Exceptions;

namespace DateCheck.Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase(IDialogService dialogos)
    {
        Dialogos = dialogos;
    }

    protected IDialogService Dialogos { get; }

    /// <summary>
    /// Executa uma ação mostrando ao usuário as mensagens das regras de negócio violadas
    /// (<see cref="DomainException"/> e <see cref="AplicacaoException"/>). Outras exceções seguem
    /// para o tratamento global do aplicativo. Retorna false se uma regra foi violada.
    /// </summary>
    protected async Task<bool> ExecutarAsync(Func<Task> acao)
    {
        try
        {
            await acao();
            return true;
        }
        catch (Exception ex) when (ex is DomainException or AplicacaoException)
        {
            Dialogos.MostrarErro(ex.Message);
            return false;
        }
    }
}
