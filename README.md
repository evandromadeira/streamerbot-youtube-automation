# Streamer.bot YouTube Streaming Engine & Engagement Gateway

> **[PT]** Engine em C# (.NET) para automação de lives no YouTube via Streamer.bot: persistência concorrente (SQLite WAL), integração com APIs externas, gamificação em tempo real e orquestração de eventos.  
> **[EN]** C# (.NET) engine for YouTube live streaming automation via Streamer.bot: concurrent persistence (SQLite WAL), external API integration, real-time gamification, and event orchestration.

---

## Sobre o Projeto / About The Project

### 🇧🇷 Português

Este projeto reúne scripts C# independentes para gerenciamento e engajamento em lives no YouTube via Streamer.bot. Os scripts processam eventos do chat e de doações, gerenciam moedas virtuais e registram dados em SQLite.

**Características da implementação:**

* **Persistência:** SQLite em modo **WAL (Write-Ahead Logging)** com `busy_timeout` de 3 segundos. A consulta `!meta` realiza até três tentativas, com intervalo fixo de 500 ms entre elas.
* **Transações locais:** Operações de saldo, apostas e resgates usam transações SQLite com `BEGIN IMMEDIATE`, `COMMIT` e `ROLLBACK`. Recompensa, registro da doação e Timer são etapas independentes; não formam uma transação única.
* **StreamElements e LivePix:** A importação de pontos consulta e debita saldos pela API REST do StreamElements, com autenticação JWT. As doações do LivePix chegam pela integração configurada entre LivePix e StreamElements, como eventos **Tip** no Streamer.bot. Os scripts não acessam diretamente a API do LivePix.
* **Conversão de valores:** A conversão para BRL usa taxas definidas manualmente no Gerente de Doações, sem consulta de cotações em tempo real. Os pontos de meta são calculados com fatores específicos por tipo de evento.
* **Comunicação e recuperação:** As Actions compartilham argumentos e contextos JSON. Os fluxos de doações e importação distinguem resultados confirmados, falhos e incertos, com logs para conferência e recuperação manual.
* **Moedas Surpresa:** A palavra ativa controla a rodada, e um `lock` serializa a comparação e a atribuição das posições. O prêmio só é anunciado após confirmação do crédito.

---

### 🇺🇸 English

This project contains independent C# scripts for YouTube live stream management and audience engagement through Streamer.bot. The scripts process chat and donation events, manage virtual coins, and store data in SQLite.

**Implementation features:**

* **Persistence:** SQLite uses **WAL (Write-Ahead Logging)** mode and a 3-second `busy_timeout`. The `!meta` query makes up to three attempts with a fixed 500 ms delay between attempts.
* **Local transactions:** Balance operations, bets, and redemptions use SQLite transactions with `BEGIN IMMEDIATE`, `COMMIT`, and `ROLLBACK`. Rewards, donation records, and Timer updates are independent steps, not a single transaction.
* **StreamElements and LivePix:** Point imports query and debit balances through the StreamElements REST API using JWT authentication. LivePix donations arrive through the configured LivePix–StreamElements integration as Streamer.bot **Tip** events. These scripts do not call the LivePix API directly.
* **Value conversion:** BRL conversion uses manually configured rates in the donation manager, without fetching live exchange rates. Goal points use conversion factors specific to each event type.
* **Communication and recovery:** Actions share arguments and JSON contexts. Donation and import flows distinguish confirmed, failed, and uncertain results, with logs for verification and manual recovery.
* **Surprise Coins:** An active word controls the round, and a `lock` serializes word comparison and placement assignment. Prizes are announced only after coin credit is confirmed.

---

## Stack Tecnológica / Technology Stack

* **Linguagem / Language:** C# executado pelo Streamer.bot / C# executed by Streamer.bot
* **Banco de Dados / Database:** SQLite com WAL / SQLite with WAL
* **Integrações / Integrations:** Streamer.bot CPH API, StreamElements REST API
* **Dados / Data:** Argumentos entre Actions e JSON (Newtonsoft.Json) / Action arguments and JSON (Newtonsoft.Json)

---

## Organização dos Módulos / Project Structure

```text
youtube-automation/
├── src/
│   ├── chat/
│   │   └── Youtube Gerente de Chat.cs            # Orquestrador central e roteador de eventos do chat
│   ├── coins/
│   │   └── Youtube Gerente de Moedas.cs          # Saldos, ranking, recompensas e transferências
│   ├── core/
│   │   └── Youtube Gerente de Banco de Dados.cs  # Gerenciador de conexão SQLite, schema e queries
│   ├── donations/
│   │   ├── Youtube Consultar Meta.cs             # Consolidação e cálculo de metas de arrecadação
│   │   ├── Youtube Gerente de Doações.cs         # Orquestra o processamento das doações
│   │   └── Youtube Recompensar Doações.cs        # Recompensas e agradecimentos
│   ├── migration/
│   │   └── Youtube Importar Moedas SE.cs         # Módulo de migração via API do StreamElements
│   ├── minigames/
│   │   ├── Youtube Compara Palavra.cs            # Compara a palavra e confirma o crédito do prêmio
│   │   └── Youtube Moedas Surpresa.cs            # Sorteia a palavra e controla a duração da rodada
│   ├── obs/
│   │   └── Youtube Gerente de OBS.cs             # Alternância de fontes e integração com o OBS
│   ├── platform/
│   │   └── Youtube Gerente de Plataforma.cs     # Catálogo, estoque e resgates de recompensas
│   ├── prediction/
│   │   └── Youtube Gerente de Palpite.cs         # Criação, apostas, resultados e distribuição
│   ├── profile/
│   │   └── Youtube Gerente de Perfil.cs          # Dados de perfil e nível de membro
│   ├── soundboard/
│   │   └── Youtube Gerente de Áudio.cs           # Cadastro, aliases e reprodução de mídias MP3
│   ├── startup/
│   │   └── Youtube Tarefas ao Iniciar a Live.cs  # Bootstrap e rotinas de início da transmissão
│   ├── statistics/
│   │   └── Youtube Gerente de Estatísticas.cs    # Consultas de presença geral e mensal
│   └── timer/
│       ├── Youtube Executa Timer.cs              # Execução e atualização do timer
│       ├── Youtube Gerencia Subathon.cs          # Controle de subathon e metas
│       └── Youtube Gerente de Timer.cs           # Comandos e acréscimos de tempo
├── .gitignore
└── README.md
```

---

## Configuração das Actions / Action Setup

**[PT]** Cada arquivo C# em `src/` corresponde a uma Action independente do Streamer.bot. Para chamadas entre scripts via `CPH.ExecuteMethod`, configure o **Name** da subação **Execute C# Code** com o nome usado pelo chamador.

As sete triggers de doações ficam em `Youtube Gerente de Doações`: Super Chat, Super Sticker, Jewels Gifted, New Sponsor, Member Milestone, Membership Gift e StreamElements Tip. Use a fila **Doações**, com processamento sequencial. `Youtube Recompensar Doações` permanece habilitada, sem triggers, expondo os métodos `RecompensarDoacao` e `EnviarAgradecimento`.

Mantenha `Youtube Compara Palavra` habilitada e sem trigger de mensagem. O Gerente de Chat chama seu método quando há uma rodada ativa, indicada por `moedasSurpresaPalavra`.

Os caminhos de execução são definidos pelas variáveis `caminhoPastaStreamerBot` e `caminhoPastaStreamElements`. Bancos, áudios e configurações de execução ficam fora deste repositório. Confira a compilação, os Names, as triggers e as filas na instalação de destino e valide com eventos de teste antes do uso em live.

**[EN]** Each C# file in `src/` corresponds to an independent Streamer.bot Action. For calls through `CPH.ExecuteMethod`, set the **Name** of the **Execute C# Code** sub-action to the name used by the caller.

The seven donation triggers belong to `Youtube Gerente de Doações`: Super Chat, Super Sticker, Jewels Gifted, New Sponsor, Member Milestone, Membership Gift, and StreamElements Tip. Use the **Doações** queue with sequential processing. Keep `Youtube Recompensar Doações` enabled without triggers; it exposes `RecompensarDoacao` and `EnviarAgradecimento`.

Keep `Youtube Compara Palavra` enabled without a message trigger. The chat manager calls its method when `moedasSurpresaPalavra` indicates an active round.

Runtime paths are configured through `caminhoPastaStreamerBot` and `caminhoPastaStreamElements`. Databases, audio files, and runtime configuration are outside this repository. Check compilation, code Names, triggers, and queues on the target installation, then validate with test events before live use.

---

## Autor / Author

**Evandro Madeira**  
*Desenvolvedor de Software / Software Developer*

* **GitHub:** [@evandromadeira](https://github.com/evandromadeira)