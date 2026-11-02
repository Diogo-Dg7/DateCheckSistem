namespace DateCheck.Desktop.Navegacao;

public interface INavegacao
{
    /// <summary>Mostra a tela Hoje; com <paramref name="dia"/>, mostra os compromissos desse dia.</summary>
    Task MostrarHojeAsync(DateTime? dia = null);

    Task MostrarCalendarioAsync();
}

/// <summary>Tela que precisa saber quando entra e sai de cena (carregar dados, ligar/desligar timers).</summary>
public interface ITela
{
    Task AtivarAsync();

    void Desativar();
}
