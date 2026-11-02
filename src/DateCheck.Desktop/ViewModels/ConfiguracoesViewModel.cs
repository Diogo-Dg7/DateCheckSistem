using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.ViewModels;

/// <summary>Área simples de configurações: antecedência dos avisos e início com o Windows.</summary>
public sealed partial class ConfiguracoesViewModel : ViewModelBase
{
    private readonly IPreferenciasService _preferencias;
    private readonly IInicioComWindowsService _inicioComWindows;

    public ConfiguracoesViewModel(
        IPreferenciasService preferencias,
        IInicioComWindowsService inicioComWindows,
        IDialogService dialogos) : base(dialogos)
    {
        _preferencias = preferencias;
        _inicioComWindows = inicioComWindows;

        _minutosAntes = preferencias.Atual.MinutosAntes;
        _iniciarComWindows = inicioComWindows.Habilitado; // o registro é a fonte da verdade
    }

    /// <summary>Pedido para fechar a janela; o argumento indica se algo foi salvo.</summary>
    public event Action<bool>? FechamentoSolicitado;

    public static int MaximoDeMinutos => Preferencias.AntecedenciaMaximaEmMinutos;

    public string LocalDoArquivo => ArmazenamentoJsonDePreferencias.CaminhoPadrao();

    [ObservableProperty]
    private int _minutosAntes;

    [ObservableProperty]
    private bool _iniciarComWindows;

    [RelayCommand]
    private void Salvar()
    {
        var novas = new Preferencias { MinutosAntes = MinutosAntes };
        if (!novas.EhValida)
        {
            Dialogos.MostrarAviso($"A antecedência deve ficar entre 0 e {MaximoDeMinutos} minutos.");
            return;
        }

        _preferencias.Salvar(novas);

        if (IniciarComWindows != _inicioComWindows.Habilitado)
            _inicioComWindows.Definir(IniciarComWindows);

        FechamentoSolicitado?.Invoke(true);
    }

    [RelayCommand]
    private void Cancelar() => FechamentoSolicitado?.Invoke(false);
}
