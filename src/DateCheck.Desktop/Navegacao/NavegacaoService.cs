using CommunityToolkit.Mvvm.ComponentModel;
using DateCheck.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Desktop.Navegacao;

/// <summary>
/// Troca o ViewModel exibido pela janela principal. Resolve as telas sob demanda para evitar dependência
/// circular (as telas pedem a navegação, e a navegação criaria as telas).
/// </summary>
public sealed partial class NavegacaoService : ObservableObject, INavegacao
{
    private readonly IServiceProvider _services;

    public NavegacaoService(IServiceProvider services)
    {
        _services = services;
    }

    [ObservableProperty]
    private object? _atual;

    public async Task MostrarHojeAsync(DateTime? dia = null)
    {
        var hoje = _services.GetRequiredService<HojeViewModel>();
        hoje.DefinirDia(dia);
        await IrParaAsync(hoje);
    }

    public Task MostrarCalendarioAsync() => IrParaAsync(_services.GetRequiredService<CalendarioViewModel>());

    public Task RecarregarAtualAsync() => Atual is ITela tela ? tela.RecarregarAsync() : Task.CompletedTask;

    private async Task IrParaAsync(ITela destino)
    {
        if (Atual is ITela anterior && !ReferenceEquals(anterior, destino))
            anterior.Desativar();

        Atual = destino;
        await destino.AtivarAsync();
    }
}
