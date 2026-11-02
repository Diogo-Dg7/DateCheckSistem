using CommunityToolkit.Mvvm.Input;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    public MainViewModel(NavegacaoService navegacao, IDialogService dialogos) : base(dialogos)
    {
        Navegacao = navegacao;
    }

    /// <summary>Expõe a tela atual (<c>Navegacao.Atual</c>) para o ContentControl da janela.</summary>
    public NavegacaoService Navegacao { get; }

    public Task InicializarAsync() => Navegacao.MostrarHojeAsync();

    [RelayCommand]
    private Task IrParaHojeAsync() => Navegacao.MostrarHojeAsync();

    [RelayCommand]
    private Task IrParaCalendarioAsync() => Navegacao.MostrarCalendarioAsync();
}
