using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.Tests;

public class InstanciaUnicaTests
{
    // Nome único por teste: não interfere no aplicativo real nem em testes paralelos.
    private static string NomeUnico() => $"DateCheckTestes.{Guid.NewGuid():N}";

    [Fact]
    public void PrimeiraExecucao_AssumeAInstancia()
    {
        using var primeira = new InstanciaUnica(NomeUnico());

        Assert.True(primeira.TentarAssumir());
    }

    [Fact]
    public void SegundaExecucao_NaoAssumeEnquantoAPrimeiraEstaViva()
    {
        var nome = NomeUnico();
        using var primeira = new InstanciaUnica(nome);
        using var segunda = new InstanciaUnica(nome);

        Assert.True(primeira.TentarAssumir());
        Assert.False(segunda.TentarAssumir());
    }

    [Fact]
    public void DepoisQueAPrimeiraEncerra_UmaNovaExecucaoPodeAssumir()
    {
        var nome = NomeUnico();
        var primeira = new InstanciaUnica(nome);
        Assert.True(primeira.TentarAssumir());
        primeira.Dispose();

        using var nova = new InstanciaUnica(nome);

        Assert.True(nova.TentarAssumir());
    }

    [Fact]
    public void InstanciasComNomesDiferentes_NaoSeBloqueiam()
    {
        using var a = new InstanciaUnica(NomeUnico());
        using var b = new InstanciaUnica(NomeUnico());

        Assert.True(a.TentarAssumir());
        Assert.True(b.TentarAssumir());
    }

    [Fact]
    public void SegundaExecucao_AvisaAPrimeiraParaSeMostrar()
    {
        var nome = NomeUnico();
        using var primeira = new InstanciaUnica(nome);
        using var segunda = new InstanciaUnica(nome);
        using var recebeu = new ManualResetEventSlim();
        Assert.True(primeira.TentarAssumir());
        primeira.EscutarPedidosDeAbertura(recebeu.Set);
        Assert.False(segunda.TentarAssumir());

        var avisou = segunda.AvisarInstanciaExistente();

        Assert.True(avisou);
        Assert.True(recebeu.Wait(TimeSpan.FromSeconds(5)), "A primeira instância não recebeu o pedido.");
    }

    [Fact]
    public void VariosPedidos_SaoTodosEntregues()
    {
        var nome = NomeUnico();
        using var primeira = new InstanciaUnica(nome);
        using var segunda = new InstanciaUnica(nome);
        using var dois = new CountdownEvent(2);
        Assert.True(primeira.TentarAssumir());
        primeira.EscutarPedidosDeAbertura(() => dois.Signal());

        segunda.AvisarInstanciaExistente();
        Thread.Sleep(200); // o evento é de reset automático: dois sinais colados contariam como um
        segunda.AvisarInstanciaExistente();

        Assert.True(dois.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AvisarSemInstanciaRodando_RetornaFalsoSemLancar()
    {
        using var sozinha = new InstanciaUnica(NomeUnico());

        Assert.False(sozinha.AvisarInstanciaExistente());
    }

    [Fact]
    public void EscutarSemAssumir_LancaInvalidOperation()
    {
        using var instancia = new InstanciaUnica(NomeUnico());

        Assert.Throws<InvalidOperationException>(() => instancia.EscutarPedidosDeAbertura(() => { }));
    }
}
