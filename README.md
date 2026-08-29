# Time Tracking

Aplicativo desktop para Windows de controle de tempo por tarefas — local, offline e sem contas ou login. Os dados ficam inteiramente na máquina do usuário, em um banco SQLite.

Este é um projeto pessoal em desenvolvimento, guiado por uma especificação técnica detalhada (`PROJECT_SPEC.md`) e construído em fases incrementais com o Claude Code.

## Funcionalidades (MVP)

- Criar, editar e excluir tarefas.
- Iniciar, pausar e parar o timer de uma tarefa.
- Uma tarefa por vez com timer em execução (iniciar outra pausa a anterior, mediante confirmação).
- Tarefas com múltiplas sessões de trabalho (`TimeEntry`), com o tempo total calculado a partir dos timestamps.
- Timer persistente: sobrevive ao fechamento e reabertura do aplicativo.
- Edição de dados da tarefa mesmo após o timer ter sido encerrado.
- Tags personalizadas (nome, descrição e cor) para categorizar tarefas.
- Temas claro, escuro e "Sistema" (segue o tema do Windows).
- Limpeza de histórico com confirmação.
- Funciona 100% offline, sem backend, sem sincronização e sem telemetria.

## Stack tecnológica

- **C#** / **.NET 8 (LTS)**
- **WPF** + **XAML**, padrão **MVVM**
- **SQLite** (banco local) + **Entity Framework Core** (migrations)
- **CommunityToolkit.Mvvm**
- **Microsoft.Extensions.DependencyInjection**

## Arquitetura

```text
WPF (Views)
 ↓
ViewModels (MVVM / estado da UI)
 ↓
Services (regras de negócio: TimerService, TaskService, TagService, ThemeService)
 ↓
Repositories / EF Core
 ↓
SQLite (banco local)
```

Views não acessam o banco, repositories ou lógica de negócio diretamente; ViewModels não implementam lógica de persistência. Detalhes completos em `PROJECT_SPEC.md`, Seções 4 e 5.

## Estrutura do projeto

```text
TimeTracking/
├── App.xaml / App.xaml.cs / TimeTracking.csproj
├── Models/            → Task, Tag, TimeEntry
├── Data/               → AppDbContext, Migrations
├── Repositories/       → acesso a dados (interfaces + implementações)
├── Services/           → TimerService, TaskService, TagService, NavigationService, ThemeService
├── ViewModels/         → estado de UI e comandos
├── Views/              → telas e componentes (Sidebar, TaskCard, TaskEditorPanel, TagChip)
├── Resources/          → Themes (Dark/Light), Styles, Icons
└── Helpers/
```

## Como rodar localmente

Pré-requisitos: Windows 10/11 e [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone <url-do-repositorio>
cd TimeTracking
dotnet restore
dotnet ef database update   # aplica as migrations e cria o banco SQLite local
dotnet run --project TimeTracking
```

O banco é criado automaticamente em `%LocalAppData%\TimeTracking\timetracking.db` na primeira execução.

## Status de desenvolvimento

O projeto é construído em fases independentes — uma por vez, cada uma validada antes de avançar para a próxima (ver `PROJECT_SPEC.md`, Seção 48).

- [x] Fase 0 — Análise e planejamento
- [x] Fase 1 — Criação do projeto
- [ ] Fase 2 — Banco de dados _(em andamento)_
- [ ] Fase 3 — Shell e navegação
- [ ] Fase 4 — CRUD de tarefas
- [ ] Fase 5 — Timer
- [ ] Fase 6 — Painel de edição
- [ ] Fase 7 — Tags
- [ ] Fase 8 — Settings e temas
- [ ] Fase 9 — Polish de UI/UX
- [ ] Fase 10 — Testes e estabilização
- [ ] Fase 11 — Build e empacotamento

## Especificação completa

Todo o detalhamento de requisitos, modelo de dados, regras de negócio, design system e plano de fases está em [`PROJECT_SPEC.md`](./PROJECT_SPEC.md) — é a fonte de verdade do projeto.

## Dados e privacidade

Este aplicativo não envia dados para nenhum servidor, não possui telemetria e não requer conexão com a internet. Todo o histórico de tarefas e tempo registrado permanece apenas na máquina onde o aplicativo é executado.
