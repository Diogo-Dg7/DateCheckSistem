using System.ComponentModel;
using System.Windows;
using DateCheck.Desktop.Navegacao;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Desktop.Servicos;

/// <summary>Ações sobre a janela principal, seguras para chamar de qualquer thread.</summary>
public interface IControleDaJanela
{
    /// <summary>Mostra a janela (se estava escondida na bandeja), restaura e a traz para frente.</summary>
    void Mostrar();

    /// <summary>Como <see cref="Mostrar"/>, e abre a tela Hoje (clique num toast).</summary>
    void MostrarHoje();

    /// <summary>Encerra o aplicativo de verdade (fechar a janela apenas a esconde).</summary>
    void Sair();

    /// <summary>Libera o fechamento real da janela (fim da sessão do Windows), sem esconder.</summary>
    void PermitirFechamento();
}

public sealed class ControleDaJanela : IControleDaJanela
{
    private readonly IServiceProvider _services;

    private Window? _janela;
    private volatile bool _encerrando;

    public ControleDaJanela(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>Passa a controlar a janela: fechar (X, Alt+F4) esconde na bandeja em vez de encerrar.</summary>
    public void Registrar(Window janela)
    {
        _janela = janela;
        janela.Closing += AoFechar;
    }

    public void Mostrar() => NaInterface(() =>
    {
        if (_janela is not { } janela)
            return; // ainda inicializando: a janela aparece sozinha quando ficar pronta

        if (!janela.IsVisible)
            janela.Show();

        if (janela.WindowState == WindowState.Minimized)
            janela.WindowState = WindowState.Normal;

        // O Windows restringe quem pode roubar o foco; alternar Topmost é o jeito confiável de trazer à frente.
        janela.Activate();
        janela.Topmost = true;
        janela.Topmost = false;
        janela.Focus();
    });

    public void MostrarHoje()
    {
        Mostrar();
        NaInterface(() =>
        {
            if (_janela is not null)
                _ = _services.GetRequiredService<INavegacao>().MostrarHojeAsync();
        });
    }

    public void Sair() => NaInterface(() =>
    {
        _encerrando = true;
        System.Windows.Application.Current.Shutdown();
    });

    public void PermitirFechamento() => _encerrando = true;

    private void AoFechar(object? sender, CancelEventArgs e)
    {
        if (_encerrando)
            return;

        e.Cancel = true;
        _janela?.Hide();
    }

    private static void NaInterface(Action acao)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            acao();
        else
            dispatcher.Invoke(acao);
    }
}
