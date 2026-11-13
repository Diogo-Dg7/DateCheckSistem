using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DateCheck.Application.Notion;
using DateCheck.Application.Servicos;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.ViewModels;

/// <summary>Uma escolha para "o que indica concluído": uma propriedade do banco, ou nenhuma.</summary>
public sealed record OpcaoDeConclusao(PropriedadeNotion? Propriedade, string Texto)
{
    public bool EhNenhuma => Propriedade is null;

    public bool EhStatus => Propriedade?.Tipo == TiposDePropriedadeNotion.Status;
}

/// <summary>
/// Aba "Notion" das Configurações: conectar (token + link do banco), escolher a propriedade de data e a de
/// "concluído", testar, sincronizar agora e desconectar. O token só existe em memória enquanto é digitado: ao
/// conectar vai para o cofre e a caixa é limpa; nada secreto é mostrado ("Conectado como …" e só).
/// </summary>
public sealed partial class NotionConfiguracoesViewModel : ViewModelBase, IDisposable
{
    public static readonly OpcaoDeConclusao Nenhuma = new(null, "(nenhuma: tudo entra como pendente)");

    private readonly INotionService _notion;
    private readonly ISincronizacaoExternaService _sincronizacao;
    private readonly IPreferenciasService _preferencias;
    private readonly INavegacao _navegacao;
    private readonly SynchronizationContext? _contexto = SynchronizationContext.Current;

    private EstruturaDoBancoNotion? _estrutura;

    public NotionConfiguracoesViewModel(
        INotionService notion,
        ISincronizacaoExternaService sincronizacao,
        IPreferenciasService preferencias,
        INavegacao navegacao,
        IDialogService dialogos) : base(dialogos)
    {
        _notion = notion;
        _sincronizacao = sincronizacao;
        _preferencias = preferencias;
        _navegacao = navegacao;

        _intervaloEmMinutos = preferencias.Atual.Notion.IntervaloEmMinutos;
        _conclusaoEscolhida = Nenhuma;
        AtualizarConexao();
        AtualizarLinhasDoEstado(sincronizacao.Estado);

        _sincronizacao.EstadoAlterado += AoMudarOEstado;
    }

    public void Dispose() => _sincronizacao.EstadoAlterado -= AoMudarOEstado;

    // ---------- formulário ----------

    /// <summary>Em memória só enquanto é digitado; apagado depois de conectar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsandoTokenSalvo))]
    private string _token = string.Empty;

    private bool _temTokenSalvo;

    /// <summary>
    /// Campo do token em branco e já há um token no cofre: "Carregar propriedades" e "Conectar" usam esse. O valor do
    /// token nunca volta para a tela; só este aviso.
    /// </summary>
    public bool UsandoTokenSalvo => Token.Length == 0 && _temTokenSalvo;

    [ObservableProperty]
    private string _linkDoBanco = string.Empty;

    [ObservableProperty]
    private int _intervaloEmMinutos;

    public ObservableCollection<FonteDeDadosNotion> FontesDeDados { get; } = [];

    public ObservableCollection<PropriedadeNotion> PropriedadesDeData { get; } = [];

    public ObservableCollection<OpcaoDeConclusao> OpcoesDeConclusao { get; } = [];

    public ObservableCollection<string> OpcoesDeStatus { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarEscolhas))]
    private string _nomeDoBancoCarregado = string.Empty;

    [ObservableProperty]
    private FonteDeDadosNotion? _fonteEscolhida;

    [ObservableProperty]
    private PropriedadeNotion? _propriedadeDataEscolhida;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarValorConcluido))]
    private OpcaoDeConclusao _conclusaoEscolhida;

    [ObservableProperty]
    private string? _valorConcluidoEscolhido;

    public bool MostrarEscolhas => _estrutura is not null;

    public bool MostrarEscolhaDeFonte => FontesDeDados.Count > 1;

    public bool MostrarValorConcluido => ConclusaoEscolhida.EhStatus;

    // ---------- estado ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeDesconectar))]
    private bool _conectado;

    [ObservableProperty]
    private string _resumoDaConexao = string.Empty;

    [ObservableProperty]
    private string _resultadoDoTeste = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CarregarBancoCommand), nameof(ConectarCommand), nameof(TestarConexaoCommand),
        nameof(SincronizarAgoraCommand), nameof(DesconectarCommand), nameof(SalvarIntervaloCommand))]
    private bool _ocupado;

    [ObservableProperty]
    private string _linhaDaSincronizacao = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemErro))]
    private string _linhaDeErro = string.Empty;

    [ObservableProperty]
    private bool _precisaReconectar;

    public bool TemErro => LinhaDeErro.Length > 0;

    public bool PodeDesconectar => Conectado && !Ocupado;

    // ---------- reações às escolhas ----------

    partial void OnFonteEscolhidaChanged(FonteDeDadosNotion? value)
    {
        PropriedadesDeData.Clear();
        OpcoesDeConclusao.Clear();
        OpcoesDeConclusao.Add(Nenhuma);

        if (value is not null)
        {
            foreach (var propriedade in value.PropriedadesDeData)
                PropriedadesDeData.Add(propriedade);

            foreach (var propriedade in value.PropriedadesDeConclusao)
                OpcoesDeConclusao.Add(new OpcaoDeConclusao(propriedade, $"{propriedade.Nome} ({TipoPorExtenso(propriedade.Tipo)})"));
        }

        PropriedadeDataEscolhida = PropriedadesDeData.FirstOrDefault();
        ConclusaoEscolhida = Nenhuma;
    }

    partial void OnConclusaoEscolhidaChanged(OpcaoDeConclusao value)
    {
        OpcoesDeStatus.Clear();
        foreach (var opcao in value.Propriedade?.Opcoes ?? [])
            OpcoesDeStatus.Add(opcao);

        ValorConcluidoEscolhido = value.EhStatus ? OpcoesDeStatus.FirstOrDefault() : null;
    }

    // ---------- comandos ----------

    private bool Livre() => !Ocupado;

    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task CarregarBancoAsync()
    {
        Ocupado = true;
        try
        {
            EstruturaDoBancoNotion? estrutura = null;
            if (!await ExecutarAsync(async () => estrutura = await _notion.InspecionarAsync(Token, LinkDoBanco)) || estrutura is null)
                return;

            _estrutura = estrutura;
            NomeDoBancoCarregado = estrutura.Titulo;

            FontesDeDados.Clear();
            foreach (var fonte in estrutura.FontesDeDados)
                FontesDeDados.Add(fonte);
            OnPropertyChanged(nameof(MostrarEscolhaDeFonte));
            OnPropertyChanged(nameof(MostrarEscolhas));

            FonteEscolhida = null; // força a atualização das listas mesmo se a escolha anterior era a mesma
            FonteEscolhida = FontesDeDados.FirstOrDefault(f => f.PropriedadesDeData.Any()) ?? FontesDeDados.First();

            if (!estrutura.FontesDeDados.Any(f => f.PropriedadesDeData.Any()))
                Dialogos.MostrarAviso(
                    "Esse banco não tem nenhuma propriedade do tipo data. Adicione uma coluna de data no Notion e carregue de novo.");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task ConectarAsync()
    {
        if (_estrutura is null || FonteEscolhida is null)
        {
            Dialogos.MostrarAviso("Cole o token e o link do banco e clique em \"Carregar propriedades\" primeiro.");
            return;
        }

        if (PropriedadeDataEscolhida is null)
        {
            Dialogos.MostrarAviso("Escolha a propriedade de data.");
            return;
        }

        if (!IntervaloValido())
            return;

        Ocupado = true;
        try
        {
            var conclusao = MontarConclusao();
            var conectou = await ExecutarAsync(() => _notion.ConectarAsync(
                Token, _estrutura, FonteEscolhida.Id, PropriedadeDataEscolhida.Nome, conclusao));
            if (!conectou)
                return;

            GravarIntervalo();

            // O token já está no cofre: some da memória e da caixa de senha.
            Token = string.Empty;
            LimparFormulario();
            AtualizarConexao();
            ResultadoDoTeste = string.Empty;
        }
        finally
        {
            Ocupado = false;
        }

        await SincronizarAgoraAsync();
    }

    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task TestarConexaoAsync()
    {
        Ocupado = true;
        try
        {
            ResultadoDoTeste = string.Empty;
            await ExecutarAsync(async () => ResultadoDoTeste = await _notion.TestarConexaoAsync());
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task SincronizarAgoraAsync()
    {
        Ocupado = true;
        try
        {
            var estado = await _sincronizacao.SincronizarAsync(manual: true);

            if (estado.Situacao == SituacaoDaSincronizacao.NaoConfigurado)
                Dialogos.MostrarAviso("Conecte o Notion primeiro.");
            else if (estado.Situacao == SituacaoDaSincronizacao.Sincronizando)
                Dialogos.MostrarAviso("Já há uma sincronização em andamento.");

            AtualizarLinhasDoEstado(estado);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand(CanExecute = nameof(Livre))]
    private async Task DesconectarAsync()
    {
        if (!Dialogos.Confirmar(
                "Desconectar o Notion",
                "Isso apaga o token deste computador e remove do DateCheck os compromissos importados do Notion. "
                + "Seus compromissos do DateCheck e o que está no Notion não são alterados.\n\nDesconectar?"))
            return;

        Ocupado = true;
        try
        {
            var removidos = 0;
            if (!await ExecutarAsync(async () => removidos = await _notion.DesconectarAsync()))
                return;

            LimparFormulario();
            AtualizarConexao();
            ResultadoDoTeste = removidos == 1
                ? "Desconectado. 1 compromisso do Notion foi removido."
                : $"Desconectado. {removidos} compromissos do Notion foram removidos.";
            AtualizarLinhasDoEstado(_sincronizacao.Estado);
        }
        finally
        {
            Ocupado = false;
        }

        await RecarregarTelaAsync();
    }

    [RelayCommand(CanExecute = nameof(Livre))]
    private void SalvarIntervalo()
    {
        if (IntervaloValido())
            GravarIntervalo();
    }

    // ---------- auxiliares ----------

    private ConclusaoNotion? MontarConclusao()
    {
        if (ConclusaoEscolhida.Propriedade is not { } propriedade)
            return null;

        return propriedade.Tipo == TiposDePropriedadeNotion.Checkbox
            ? new ConclusaoNotion(propriedade.Nome, TipoDeConclusaoNotion.Checkbox)
            : new ConclusaoNotion(propriedade.Nome, TipoDeConclusaoNotion.Status, ValorConcluidoEscolhido);
    }

    private bool IntervaloValido()
    {
        if (IntervaloEmMinutos is >= NotionPreferencias.IntervaloMinimoEmMinutos and <= NotionPreferencias.IntervaloMaximoEmMinutos)
            return true;

        Dialogos.MostrarAviso(
            $"O intervalo da sincronização deve ficar entre {NotionPreferencias.IntervaloMinimoEmMinutos} e {NotionPreferencias.IntervaloMaximoEmMinutos} minutos.");
        return false;
    }

    private void GravarIntervalo()
    {
        var atual = _preferencias.Atual;
        if (atual.Notion.IntervaloEmMinutos != IntervaloEmMinutos)
            _preferencias.Salvar(atual with { Notion = atual.Notion with { IntervaloEmMinutos = IntervaloEmMinutos } });
    }

    private void LimparFormulario()
    {
        _estrutura = null;
        Token = string.Empty;
        LinkDoBanco = string.Empty;
        NomeDoBancoCarregado = string.Empty;
        FontesDeDados.Clear();
        FonteEscolhida = null;
        OnPropertyChanged(nameof(MostrarEscolhaDeFonte));
        OnPropertyChanged(nameof(MostrarEscolhas));
    }

    private void AtualizarConexao()
    {
        var configuracao = _notion.ConfiguracaoAtual;
        Conectado = _notion.EstaConectado;
        _temTokenSalvo = _notion.TemTokenSalvo;
        OnPropertyChanged(nameof(UsandoTokenSalvo));
        ResumoDaConexao = Conectado && configuracao is not null
            ? $"Conectado. Propriedade de data: \"{configuracao.PropriedadeData}\"."
              + (configuracao.Conclusao is { } c ? $" Concluído: \"{c.Propriedade}\"." : " Todos entram como pendentes.")
            : "Não conectado.";
    }

    private async Task RecarregarTelaAsync()
    {
        try
        {
            await _navegacao.RecarregarAtualAsync();
        }
        catch (Exception)
        {
            // A tela mantém o que mostrava; a atualização periódica corrige.
        }
    }

    private void AoMudarOEstado(EstadoDaSincronizacao estado)
    {
        // Vem de thread de segundo plano: volta para a da interface.
        if (_contexto is null)
            AtualizarLinhasDoEstado(estado);
        else
            _contexto.Post(_ => AtualizarLinhasDoEstado(estado), null);
    }

    private void AtualizarLinhasDoEstado(EstadoDaSincronizacao estado)
    {
        LinhaDaSincronizacao = FormatarSincronizacao(estado);
        LinhaDeErro = FormatarErro(estado);
        PrecisaReconectar = estado.Situacao == SituacaoDaSincronizacao.ReconecteNecessario;
        AtualizarConexao();
    }

    public static string FormatarSincronizacao(EstadoDaSincronizacao estado)
    {
        var ultima = estado.UltimaSincronizacao is { } quando
            ? $"Última sincronização: {quando.ToString("g", CultureInfo.CurrentCulture)}"
            : "Ainda não sincronizado.";

        return estado.Situacao switch
        {
            SituacaoDaSincronizacao.NaoConfigurado => string.Empty, // o resumo da conexão já diz "Não conectado."
            SituacaoDaSincronizacao.Sincronizando => "Sincronizando…",
            SituacaoDaSincronizacao.Ok when estado.UltimoResultado is { } r =>
                $"{ultima} ({r.Inseridos} novo(s), {r.Atualizados} atualizado(s), {r.Removidos} removido(s))",
            _ => ultima
        };
    }

    public static string FormatarErro(EstadoDaSincronizacao estado) => estado.Situacao switch
    {
        SituacaoDaSincronizacao.ReconecteNecessario => "Reconecte o Notion: o token foi recusado. Cole um token novo abaixo.",
        SituacaoDaSincronizacao.Erro when !string.IsNullOrWhiteSpace(estado.Erro) => $"Erro: {estado.Erro}",
        _ => string.Empty
    };

    private static string TipoPorExtenso(string tipo) => tipo switch
    {
        TiposDePropriedadeNotion.Checkbox => "caixa de seleção",
        TiposDePropriedadeNotion.Status => "status",
        _ => tipo
    };
}
