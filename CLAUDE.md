# DateCheckSistem — guia para o Claude

Agenda pessoal desktop (Windows) em estilo calendário. Abre com o Windows e mostra os compromissos. Também é portfólio no GitHub: histórico de commits limpo.

## Stack
C# / .NET 10, SDK fixado em `global.json` (10.0.403, `rollForward: latestFeature`). WPF + MVVM (CommunityToolkit.Mvvm). SQL Server Express (`.\SQLEXPRESS`) + EF Core Code First com migrations. DI via Microsoft.Extensions.Hosting no `App.xaml.cs`. xUnit. Bandeja: Hardcodet.NotifyIcon.Wpf. Toast: `AppNotificationManager` (Windows App SDK; ver fase 6).

## Arquitetura (dependências apontam para dentro)
```
Desktop → Application, Infrastructure
Infrastructure → Application → Domain
```
- `src/DateCheck.Domain`: entidades, enums, regras puras. SEM dependências externas (nem EF, nem SQL Server).
- `src/DateCheck.Application`: serviços, DTOs, casos de uso, interfaces de repositório.
- `src/DateCheck.Infrastructure`: AppDbContext, `IEntityTypeConfiguration<T>`, repositórios, migrations.
- `src/DateCheck.Desktop`: Views XAML, ViewModels, conversores, serviços de UI.
- `tests/DateCheck.Domain.Tests`, `tests/DateCheck.Application.Tests`, `tests/DateCheck.Infrastructure.Tests`, `tests/DateCheck.Desktop.Tests`.
- ViewModels e Views só conversam com a camada Application (interfaces de serviço). Apenas o `App.xaml.cs`, como composition root, pode referenciar tipos da Infrastructure.
- No Desktop, usar sempre `System.Windows.Application` (nunca `Application` solto): o namespace `DateCheck.Application` o oculta.
- Testes de integração do repositório ficam em `tests/DateCheck.Infrastructure.Tests`, rodam no `.\SQLEXPRESS` real (não InMemory/SQLite, que escondem diferenças de provider), num banco de nome único por execução criado com `Migrate()` e apagado ao fim. Marcados com `[Trait("Categoria", "Integracao")]`; para pular: `dotnet test --filter "Categoria!=Integracao"`. A connection string de teste pode ser sobrescrita pela variável de ambiente `DATECHECK_TEST_CONNECTION`.

## Regras de domínio
- `Agendamento`: Id, Titulo, Descricao, Inicio, Fim, Status, `SerieId` (Guid?, opcional). Setters privados; mudanças só por `Concluir()`, `Cancelar()`, `Reabrir()`, `Reagendar(inicio, fim)`, `AlterarDetalhes(...)`, `AssociarSerie(...)`; `CopiarPara(novoInicio, serieId?)` é o método de fábrica de cópias.
- A entidade valida suas invariantes (Fim > Inicio, título obrigatório, não concluir cancelado...) lançando `DomainException`. Repetir `Concluir`/`Cancelar` é no-op; só agendamento pendente pode ser reagendado/editado; `Reabrir()` devolve concluído/cancelado a Pendente.
- `StatusAgendamento`: Pendente, Concluido, Cancelado. Datas em horário local (`DateTime`).
- Sem Data Annotations nas entidades; mapeamento só por `IEntityTypeConfiguration<T>`.
- **Sem recorrência salva.** Cada agendamento é independente (horário e status próprios). Repetir é só um atalho que cria cópias; `SerieId` apenas agrupa cópias criadas juntas. `CopiarPara` mantém título, descrição e duração, e a cópia é sempre Pendente.
- Conflito: `inicioA < fimB && inicioB < fimA`. Ignora cancelados e o próprio agendamento na edição. Não bloqueia: retorna a lista e a tela pergunta se salva mesmo assim.

## Application (fase 3)
- Serviço **Duplicar**: copia um agendamento para outra data/horário.
- Serviço **Repetir**: gera cópias diárias, semanais ou mensais até uma data de fim OBRIGATÓRIA (máximo 1 ano), todas com o mesmo `SerieId`. Mensal no dia 31 cai no último dia dos meses curtos. Retorna os conflitos encontrados por cópia.
- Usar `TimeProvider` (injetado) em vez de `DateTime.Now`.

## Desktop (fase 5)
- `App.xaml.cs` (composition root): `Host.CreateApplicationBuilder` com ContentRoot = `AppContext.BaseDirectory` (ao iniciar com o Windows o diretório atual não é o do app), cultura pt-BR (inclui `FrameworkElement.LanguageProperty`), migrations antes de abrir a janela (falha -> diálogo "tentar novamente / sair") e tratamento global de exceções da UI. Sem `StartupUri`; a `MainWindow` vem do container.
- Navegação: `NavegacaoService` (`INavegacao`) troca o ViewModel atual; `MainWindow` usa `DataTemplate` por ViewModel (Hoje, Calendário). Telas implementam `ITela` (`AtivarAsync`/`Desativar`).
- Diálogos só via `IDialogService` (erro, aviso, confirmar, formulário, data/hora): permite testar ViewModels sem janelas. Mensagens de `DomainException`/`AplicacaoException` são exibidas por `ViewModelBase.ExecutarAsync`.
- Tela Hoje atualiza a cada 30 s (`TimeProvider.CreateTimer`, só enquanto ativa) para o "próximo" e os "atrasados" acompanharem o relógio. Clicar num dia do calendário abre a tela Hoje naquele dia.
- Testes dos ViewModels: `tests/DateCheck.Desktop.Tests` (repositório em memória, `FakeTimeProvider`, diálogos falsos). Para testar com um banco de capturas/demonstração, sobrescreva a connection string com a variável de ambiente `ConnectionStrings__DateCheckDb`.

## Recursos de desktop (fase 6)
- **Instância única** (`InstanciaUnica`): `Mutex` nomeado (`Local\DateCheckSistem.Instancia`) adquirido no início do `OnStartup`, antes do Host e do banco, e guardado em campo (variável local seria coletada e soltaria a trava). A segunda execução chama `AllowSetForegroundWindow`, avisa a primeira por um `EventWaitHandle` e encerra; a primeira restaura e ativa a janela.
- **Bandeja** (`IconeDaBandeja`, Hardcodet.NotifyIcon.Wpf): criada antes do banco, com balão "Iniciando…". Menu: Abrir, Iniciar com o Windows (marcável), Sair; duplo clique abre. `ShutdownMode = OnExplicitShutdown`: fechar a janela só a esconde (`ControleDaJanela`); só `Sair` encerra. `SessionEnding` libera o fechamento real (senão o app bloquearia o desligamento do Windows).
- **Iniciar com o Windows**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `DateCheckSistem` com o caminho entre aspas. O registro é a fonte da verdade (nada de cópia em arquivo/preferência); o caminho é reescrito a cada abertura se a opção estiver ligada. Acesso ao registro atrás de `IRegistroDeInicializacao`.
- **Toasts**: `AppNotificationManager` do Windows App SDK (`Microsoft.WindowsAppSDK.Foundation`), recomendação atual da Microsoft para WPF não empacotado; `Microsoft.Toolkit.Uwp.Notifications` está parado desde 2022 e o `CommunityToolkit.WinUI.Notifications` está marcado como legado. Exige `RuntimeIdentifier win-x64`, `WindowsPackageType=None` e `WindowsAppSDKSelfContained=true` no Desktop **e no projeto de testes** (herdado). Envio atrás de `INotificador`; clique no toast abre a tela Hoje; ao sair, `RemoveAllAsync` + `Unregister`.
- **Regra de notificação** (`NotificacaoService`, `BackgroundService` com `PeriodicTimer(TimeProvider)` a cada 30 s): só pendentes cujo início está entre agora e agora + X min; nunca os que já começaram (ex.: volta da suspensão); um aviso por par (Id, Início) (reagendar avisa de novo; cancelar/reabrir não repete); falha do toast não marca como avisado.
- **Preferências**: `%LOCALAPPDATA%\DateCheck\preferencias.json` (`minutosAntes`, padrão 10, 0..1440); arquivo ausente/corrompido volta ao padrão; editável em "Configurações".
- **Ícone**: `src/DateCheck.Desktop/Recursos/app.ico`, gerado por `tools/icone/gerar-icone.ps1`.

## Banco
- Connection string em `appsettings.json`: `Server=.\SQLEXPRESS;Database=DateCheckDb;Trusted_Connection=True;TrustServerCertificate=True;`
- `AddDbContextFactory<AppDbContext>`; cada operação cria/descarta seu contexto. Nunca manter DbContext vivo.
- `EnableRetryOnFailure` no `UseSqlServer`. Se o banco não responder na inicialização, mostrar mensagem amigável.
- Índices em `Inicio` e `SerieId`. Migrations aplicadas automaticamente na inicialização e commitadas.

## Telas e comportamento
- Telas: **Hoje** (inicial; destaque do próximo e dos atrasados; concluir/cancelar/editar/novo) e **Calendário mensal** (navegação, cor por status, clicar no dia). Formulário de criar/editar com opção de duplicar/repetir e aviso de conflito. UI em pt-BR.
- Iniciar com o Windows (HKCU\...\Run, liga/desliga no app), ícone de bandeja (fechar esconde), toast X min antes (padrão 10, sem duplicar), instância única via Mutex.

## Git / GitHub
- Remoto: https://github.com/Diogo-Dg7/DateCheckSistem
- **GitHub Flow**: `main` sempre compila. Uma branch por fase a partir da `main` atualizada: `feature/estrutura-inicial`, `feature/dominio`, `feature/aplicacao`, `feature/infraestrutura`, `feature/telas`, `feature/recursos-desktop`, `docs/readme`. Correções: `fix/<descricao-curta>`. Fim da fase → Pull Request.
- Commits pequenos, um por passo lógico que compila, **Conventional Commits em português, imperativo, minúsculo** (`feat`, `fix`, `test`, `refactor`, `docs`, `chore`). Ex.: `feat: adiciona entidade Agendamento com regras de status`.
- **NÃO fazer push, merge nem abrir PR sem confirmação do usuário.** Ao fim de cada fase, mostrar resumo e `git log --oneline` da branch e aguardar aprovação.
- Nunca commitar senhas ou segredos (usar User Secrets).
- Validação commit a commit (`git rebase --exec`) deve rodar num **worktree separado com build limpo** (sem `bin/obj`): `git worktree add ../dc-validacao <commit>`, `dotnet build && dotnet test` e `git worktree remove --force ../dc-validacao`. Numa pasta com `obj` reaproveitado, o WPF gera falsos "FALHOU" (`CS2001`, arquivos `.g.cs` ausentes). Se um commit falhar no `--exec`, reconfirme num worktree limpo antes de concluir que é erro de código.
- Fim de linha: `.gitattributes` com `* text=auto` (sem `core.autocrlf` local).

## Fluxo de trabalho
Trabalhar por fases e parar ao fim de cada uma: 1 estrutura-inicial, 2 dominio (+testes), 3 aplicacao (+testes), 4 infraestrutura (+migration inicial), 5 telas, 6 recursos-desktop, 7 docs/readme (+ tag `v0.1.0`). Se algo for ambíguo ou tecnicamente questionável, perguntar antes de implementar.

Capturas de tela para o README (`docs/imagens`): `tools/capturas/criar-banco-demo.ps1` cria o banco de demonstração e `tools/capturas/capturar.ps1` gera as imagens (instruções no cabeçalho; nunca usam o banco real; só a janela do app é copiada, via PrintWindow).

## Comandos
```powershell
dotnet build
dotnet test
```
