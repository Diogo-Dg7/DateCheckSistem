# DateCheckSistem

Agenda pessoal desktop em estilo calendário, para Windows. Abre junto com o sistema e mostra os compromissos do dia.

> 🚧 Em desenvolvimento. O README completo (prints, instruções de uso) será adicionado na última fase.

## Stack

- C# / .NET 10 (LTS), WPF com MVVM (CommunityToolkit.Mvvm)
- SQL Server Express + Entity Framework Core (Code First, migrations)
- Microsoft.Extensions.Hosting para injeção de dependência
- xUnit para testes

## Estrutura

```
src/
├── DateCheck.Domain          entidades, enums e regras de negócio puras
├── DateCheck.Application     serviços, DTOs e interfaces de repositório
├── DateCheck.Infrastructure  DbContext, configurações EF, repositórios e migrations
└── DateCheck.Desktop         WPF: views, viewmodels e serviços de UI
tests/
├── DateCheck.Domain.Tests
└── DateCheck.Application.Tests
```

## Modelo de agendamentos

Cada agendamento é independente, com horário e status próprios (Pendente, Concluído ou Cancelado), e pode ser reaberto. Não existe recorrência salva: **duplicar** e **repetir** (diário, semanal ou mensal, até uma data de fim de no máximo 1 ano) apenas criam cópias. As cópias criadas juntas compartilham um `SerieId`. Conflitos de horário são avisados, mas nunca bloqueiam.

## Pré-requisitos

- .NET SDK 10.0.403 ou superior da série 10.0.x (veja `global.json`)
- SQL Server Express com a instância local `SQLEXPRESS`

## Compilar

```powershell
dotnet build
dotnet test
```
