using System.Data.Common;
using System.IO;

namespace DateCheck.Desktop.Servicos;

/// <summary>
/// Isola a build de desenvolvimento do aplicativo instalado. Com <c>DOTNET_ENVIRONMENT=Development</c> o app usa
/// outro Mutex, outra pasta de dados (<c>%LOCALAPPDATA%\DateCheck-Dev</c>, onde também ficarão os segredos das
/// integrações), outro banco (<c>DateCheckDb_Dev</c>) e nunca escreve na chave Run do Windows. Assim rodar pelo
/// Visual Studio não reescreve o "Iniciar com o Windows" para a pasta <c>bin\Debug</c> nem mistura dados e tokens.
/// </summary>
public sealed class AmbienteDeExecucao
{
    public const string VariavelDeAmbiente = "DOTNET_ENVIRONMENT";
    public const string NomeDoBancoReal = "DateCheckDb";

    private AmbienteDeExecucao(bool ehDesenvolvimento)
    {
        EhDesenvolvimento = ehDesenvolvimento;
    }

    /// <summary>Lido uma vez na inicialização do processo.</summary>
    public static AmbienteDeExecucao Atual { get; } = De(Environment.GetEnvironmentVariable(VariavelDeAmbiente));

    /// <summary>Sem a variável (ou com qualquer outro valor) é produção; o Host usa o mesmo padrão.</summary>
    public static AmbienteDeExecucao De(string? valorDaVariavel) =>
        new(string.Equals(valorDaVariavel?.Trim(), "Development", StringComparison.OrdinalIgnoreCase));

    public bool EhDesenvolvimento { get; }

    /// <summary>Base dos nomes do Mutex e do evento da instância única.</summary>
    public string NomeDaInstancia => EhDesenvolvimento ? "DateCheckSistem.Dev" : "DateCheckSistem";

    /// <summary>Nome do app nas janelas, no ícone da bandeja e nos diálogos.</summary>
    public string TituloDoApp => EhDesenvolvimento ? "DateCheck (Dev)" : "DateCheck";

    /// <summary>%LOCALAPPDATA%\DateCheck (ou DateCheck-Dev): preferências e, nas próximas versões, os segredos.</summary>
    public string PastaDeDados => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        EhDesenvolvimento ? "DateCheck-Dev" : "DateCheck");

    /// <summary>Em desenvolvimento o registro é descartável: nunca toca na chave Run do usuário (nem para ler).</summary>
    public IRegistroDeInicializacao CriarRegistroDeInicializacao() =>
        EhDesenvolvimento ? new RegistroDeInicializacaoEmMemoria() : new RegistroDeInicializacaoDoWindows();

    /// <summary>
    /// Trava de segurança: em desenvolvimento, recusa uma connection string que aponte para o banco real,
    /// mesmo que alguém sobrescreva a configuração (arquivo ou variável de ambiente) por engano.
    /// </summary>
    /// <exception cref="InvalidOperationException">Em desenvolvimento, se o banco é o real ou não foi informado.</exception>
    public void GarantirBancoIsolado(string? connectionString)
    {
        if (!EhDesenvolvimento)
            return;

        var banco = NomeDoBanco(connectionString);
        if (string.IsNullOrWhiteSpace(banco)
            || string.Equals(banco, NomeDoBancoReal, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Em desenvolvimento o app não usa o banco real ({NomeDoBancoReal}). " +
                "Configure outro banco (por padrão, DateCheckDb_Dev) em appsettings.Development.json.");
    }

    public static string? NomeDoBanco(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (var chave in new[] { "Database", "Initial Catalog" })
            {
                if (builder.TryGetValue(chave, out var valor))
                    return valor?.ToString();
            }
        }
        catch (ArgumentException)
        {
            // Connection string malformada: tratada como "sem banco" pela trava.
        }

        return null;
    }
}

/// <summary>Faz de conta que grava o "iniciar com o Windows": exercita a tela sem tocar no registro.</summary>
public sealed class RegistroDeInicializacaoEmMemoria : IRegistroDeInicializacao
{
    private readonly Dictionary<string, string> _valores = new(StringComparer.OrdinalIgnoreCase);

    public string? Ler(string nome) => _valores.GetValueOrDefault(nome);

    public void Gravar(string nome, string valor) => _valores[nome] = valor;

    public void Remover(string nome) => _valores.Remove(nome);
}
