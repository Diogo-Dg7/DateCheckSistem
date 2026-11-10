using System.IO;
using System.Text.Json;

namespace DateCheck.Desktop.Configuracoes;

public interface IPreferenciasService
{
    Preferencias Atual { get; }

    /// <exception cref="ArgumentException">Se os valores estiverem fora dos limites permitidos.</exception>
    void Salvar(Preferencias preferencias);

    /// <summary>Disparado após salvar (o serviço de notificações passa a usar o novo valor na hora).</summary>
    event Action? Alterado;
}

/// <summary>Onde as preferências ficam guardadas (abstraído para testar sem tocar no disco do usuário).</summary>
public interface IArmazenamentoDePreferencias
{
    /// <summary>Retorna nulo se não há preferências salvas ou se o arquivo está ilegível.</summary>
    Preferencias? Carregar();

    void Salvar(Preferencias preferencias);
}

public sealed class PreferenciasService : IPreferenciasService
{
    private readonly IArmazenamentoDePreferencias _armazenamento;

    private volatile Preferencias _atual;

    public PreferenciasService(IArmazenamentoDePreferencias armazenamento)
    {
        _armazenamento = armazenamento;

        // Arquivo ausente, corrompido ou com valores absurdos: volta ao padrão em vez de quebrar o app.
        var salvas = armazenamento.Carregar();
        _atual = salvas is { EhValida: true } ? salvas : Preferencias.Padrao;
    }

    public event Action? Alterado;

    public Preferencias Atual => _atual;

    public void Salvar(Preferencias preferencias)
    {
        ArgumentNullException.ThrowIfNull(preferencias);

        if (!preferencias.EhValida)
            throw new ArgumentException(
                $"A antecedência deve ficar entre 0 e {Preferencias.AntecedenciaMaximaEmMinutos} minutos.",
                nameof(preferencias));

        _armazenamento.Salvar(preferencias);
        _atual = preferencias;
        Alterado?.Invoke();
    }
}

public sealed class ArmazenamentoJsonDePreferencias : IArmazenamentoDePreferencias
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _caminho;

    public ArmazenamentoJsonDePreferencias(string caminho)
    {
        _caminho = caminho;
    }

    /// <summary>preferencias.json dentro da pasta de dados do ambiente (%LOCALAPPDATA%\DateCheck ou DateCheck-Dev).</summary>
    public static string CaminhoPadrao(string pastaDeDados) => Path.Combine(pastaDeDados, "preferencias.json");

    public Preferencias? Carregar()
    {
        try
        {
            if (!File.Exists(_caminho))
                return null;

            return JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(_caminho), Opcoes);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Salvar(Preferencias preferencias)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);

        // Grava num temporário e troca: uma queda no meio da gravação não corrompe o arquivo existente.
        var temporario = _caminho + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(preferencias, Opcoes));
        File.Move(temporario, _caminho, overwrite: true);
    }
}
