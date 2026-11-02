# DateCheckSistem — guia para o Claude

Agenda pessoal desktop (Windows) em estilo calendário. Abre com o Windows e mostra os compromissos. Também é portfólio no GitHub: histórico de commits limpo.

## Stack
C# / .NET 10, SDK fixado em `global.json` (10.0.403, `rollForward: latestFeature`). WPF + MVVM (CommunityToolkit.Mvvm). SQL Server Express (`.\SQLEXPRESS`) + EF Core Code First com migrations. DI via Microsoft.Extensions.Hosting no `App.xaml.cs`. xUnit. Bandeja: Hardcodet.NotifyIcon.Wpf. Toast: Microsoft.Toolkit.Uwp.Notifications.

## Arquitetura (dependências apontam para dentro)
```
Desktop → Application, Infrastructure
Infrastructure → Application → Domain
```
- `src/DateCheck.Domain`: entidades, enums, regras puras. SEM dependências externas (nem EF, nem SQL Server).
- `src/DateCheck.Application`: serviços, DTOs, casos de uso, interfaces de repositório.
- `src/DateCheck.Infrastructure`: AppDbContext, `IEntityTypeConfiguration<T>`, repositórios, migrations.
- `src/DateCheck.Desktop`: Views XAML, ViewModels, conversores, serviços de UI.
- `tests/DateCheck.Domain.Tests`, `tests/DateCheck.Application.Tests`.

## Regras de domínio
- `Agendamento`: Id, Titulo, Descricao, Inicio, Fim, Status, recorrência. Setters privados; mudanças só por `Concluir()`, `Cancelar()`, `Reagendar(inicio, fim)`, `AlterarRecorrencia(...)`.
- A entidade valida suas invariantes (Fim > Inicio, título obrigatório, não concluir cancelado...) lançando `DomainException`.
- `StatusAgendamento`: Pendente, Concluido, Cancelado. Datas em horário local (`DateTime`).
- Sem Data Annotations nas entidades; mapeamento só por `IEntityTypeConfiguration<T>`.
- Recorrência: grava só a regra (`TipoRecorrencia` Nenhuma/Diaria/Semanal/Mensal, `Intervalo`, `DataFimRecorrencia` opcional). Serviço expande ocorrências num período. Mensal no dia 31 cai no último dia de meses curtos. Status vale para a série inteira.
- Conflito: `inicioA < fimB && inicioB < fimA`. Ignora cancelados e o próprio agendamento na edição. Expande recorrência dos dois lados numa janela de 1 ano. Não bloqueia: retorna a lista e a tela pergunta se salva mesmo assim.

## Banco
- Connection string em `appsettings.json`: `Server=.\SQLEXPRESS;Database=DateCheckDb;Trusted_Connection=True;TrustServerCertificate=True;`
- `AddDbContextFactory<AppDbContext>`; cada operação cria/descarta seu contexto. Nunca manter DbContext vivo.
- `EnableRetryOnFailure` no `UseSqlServer`. Se o banco não responder na inicialização, mostrar mensagem amigável.
- Índice em `Inicio`. Migrations aplicadas automaticamente na inicialização e commitadas.

## Telas e comportamento
- Telas: **Hoje** (inicial; destaque do próximo e dos atrasados; concluir/cancelar/editar/novo) e **Calendário mensal** (navegação, cor por status, clicar no dia). Formulário de criar/editar com recorrência e aviso de conflito. UI em pt-BR.
- Iniciar com o Windows (HKCU\...\Run, liga/desliga no app), ícone de bandeja (fechar esconde), toast X min antes (padrão 10, sem duplicar), instância única via Mutex.

## Git / GitHub
- Remoto: https://github.com/Diogo-Dg7/DateCheckSistem
- **GitHub Flow**: `main` sempre compila. Uma branch por fase a partir da `main` atualizada: `feature/estrutura-inicial`, `feature/dominio`, `feature/aplicacao`, `feature/infraestrutura`, `feature/telas`, `feature/recursos-desktop`, `docs/readme`. Correções: `fix/<descricao-curta>`. Fim da fase → Pull Request.
- Commits pequenos, um por passo lógico que compila, **Conventional Commits em português, imperativo, minúsculo** (`feat`, `fix`, `test`, `refactor`, `docs`, `chore`). Ex.: `feat: adiciona entidade Agendamento com regras de status`.
- **NÃO fazer push, merge nem abrir PR sem confirmação do usuário.** Ao fim de cada fase, mostrar resumo e `git log --oneline` da branch e aguardar aprovação.
- Nunca commitar senhas ou segredos (usar User Secrets).

## Fluxo de trabalho
Trabalhar por fases e parar ao fim de cada uma: 1 estrutura-inicial, 2 dominio (+testes), 3 aplicacao (+testes), 4 infraestrutura (+migration inicial), 5 telas, 6 recursos-desktop, 7 docs/readme (+ tag `v0.1.0`). Se algo for ambíguo ou tecnicamente questionável, perguntar antes de implementar.

## Comandos
```powershell
dotnet build
dotnet test
```
