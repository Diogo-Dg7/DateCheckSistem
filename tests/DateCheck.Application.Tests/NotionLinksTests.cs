using DateCheck.Application.Notion;

namespace DateCheck.Application.Tests;

public class NotionLinksTests
{
    private const string IdHex = "0123456789abcdef0123456789abcdef";
    private const string IdComHifens = "01234567-89ab-cdef-0123-456789abcdef";
    private const string IdDaVisao = "ffffffffffffffffffffffffffffffff";

    [Theory]
    [InlineData($"https://www.notion.so/meu-workspace/Tarefas-{IdHex}")]
    [InlineData($"https://www.notion.so/meu-workspace/Tarefas-{IdHex}?v={IdDaVisao}")]
    [InlineData($"https://www.notion.so/meu-workspace/Tarefas-{IdHex}?v={IdDaVisao}&pvs=4")]
    [InlineData($"https://www.notion.so/{IdHex}")]
    [InlineData($"https://www.notion.so/{IdHex}?v={IdDaVisao}")]
    [InlineData($"https://www.notion.so/Tarefas-{IdHex}#{IdDaVisao}")]
    [InlineData($"https://notion.so/Tarefas-{IdHex}")]
    [InlineData($"https://meu-site.notion.site/Tarefas-{IdHex}?v={IdDaVisao}")]
    [InlineData($"  https://www.notion.so/ws/Tarefas-{IdHex}  ")]
    [InlineData($"https://www.notion.so/ws/Tarefas-0123456789ABCDEF0123456789ABCDEF")]
    [InlineData(IdHex)]
    [InlineData(IdComHifens)]
    [InlineData($"{IdHex}?v={IdDaVisao}")]
    [InlineData($"Tarefas-{IdHex}")]
    public void ExtraiOIdDoBancoNoFormatoComHifens(string entrada)
    {
        Assert.Equal(IdComHifens, NotionLinks.ExtrairIdDoBanco(entrada));
    }

    [Fact]
    public void NuncaConfundeOIdDaVisaoComOIdDoBanco()
    {
        var id = NotionLinks.ExtrairIdDoBanco($"https://www.notion.so/ws/Tarefas-{IdHex}?v={IdDaVisao}");

        Assert.NotEqual("ffffffff-ffff-ffff-ffff-ffffffffffff", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("isto não é um link")]
    [InlineData("https://www.notion.so/ws/Tarefas-semid")]
    [InlineData("0123456789abcdef")] // curto demais
    [InlineData("0123456789abcdef0123456789abcdef0")] // longo demais
    [InlineData("https://www.exemplo.com/Tarefas-0123456789abcdef0123456789abcdef")] // outro domínio
    [InlineData("https://notion.so.evil.com/Tarefas-0123456789abcdef0123456789abcdef")]
    [InlineData("ftp://www.notion.so/Tarefas-0123456789abcdef0123456789abcdef")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void EntradasInvalidas_RetornamNulo(string? entrada)
    {
        Assert.Null(NotionLinks.ExtrairIdDoBanco(entrada));
    }
}
