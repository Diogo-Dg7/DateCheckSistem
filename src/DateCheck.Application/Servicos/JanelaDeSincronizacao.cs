namespace DateCheck.Application.Servicos;

/// <summary>
/// Janela de sincronização: de 90 dias atrás até 365 dias à frente. <c>Inicio</c> é inclusivo e <c>Fim</c> é
/// exclusivo (meia-noite do dia seguinte ao último dia incluído). Este é o ÚNICO critério da sincronização: só
/// entra, atualiza ou é removido o que tem <c>Inicio</c> local em [Inicio, Fim).
/// </summary>
public readonly record struct JanelaDeSincronizacao(DateTime Inicio, DateTime Fim)
{
    public const int DiasParaTras = 90;
    public const int DiasParaFrente = 365;

    public static JanelaDeSincronizacao Calcular(DateTime agoraLocal)
    {
        var hoje = agoraLocal.Date;
        return new JanelaDeSincronizacao(hoje.AddDays(-DiasParaTras), hoje.AddDays(DiasParaFrente + 1));
    }

    public bool Contem(DateTime inicio) => inicio >= Inicio && inicio < Fim;
}
