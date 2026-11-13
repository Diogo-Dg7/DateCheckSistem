namespace DateCheck.Desktop.Navegacao;

public interface INavegacao
{
    /// <summary>Mostra a tela Hoje; com <paramref name="dia"/>, mostra os compromissos desse dia.</summary>
    Task MostrarHojeAsync(DateTime? dia = null);

    Task MostrarCalendarioAsync();

    /// <summary>Recarrega os dados da tela aberta (depois de uma sincronização que mudou compromissos).</summary>
    Task RecarregarAtualAsync();
}

/// <summary>Tela que precisa saber quando entra e sai de cena (carregar dados, ligar/desligar timers).</summary>
public interface ITela
{
    Task AtivarAsync();

    void Desativar();

    /// <summary>Relê os dados sem mudar de tela nem de dia/mês.</summary>
    Task RecarregarAsync();
}
