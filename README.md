# Streamer.bot YouTube Streaming Engine & Engagement Gateway

> **[PT]** Engine em C# (.NET) para automação de lives no YouTube via Streamer.bot: persistência concorrente (SQLite WAL), integração com APIs externas, gamificação em tempo real e orquestração de eventos.  
> **[EN]** C# (.NET) engine for YouTube live streaming automation via Streamer.bot: concurrent persistence (SQLite WAL), external API integration, real-time gamification, and event orchestration.

---

## Sobre o Projeto / About The Project

### 🇧🇷 Português
Esta aplicação foi projetada como uma **solução backend completa para gerenciamento e engajamento em transmissões ao vivo no YouTube**. O sistema orquestra eventos do chat em tempo real, gerencia transações financeiras e pontos de fidelidade, integra-se a serviços terceiros e garante alta disponibilidade de leitura/escrita com concorrência otimizada.

**Destaques de Engenharia de Software:**
* **Concorrência Otimizada & Resiliência:** Uso de SQLite em modo **WAL (Write-Ahead Logging)** com mecanismos de *retry/backoff* para evitar contenção e locks em picos de mensagens no chat.
* **Integridade Transacional (ACID):** Processamento atômico de transações financeiras, doações e atribuição de pontos utilizando blocos explicitados com `BEGIN IMMEDIATE` e `ROLLBACK`.
* **Consumo de APIs REST:** Integração bidirecional com a API do StreamElements (autenticação JWT) e LivePix para consolidação de saldos e conversão dinâmica de moedas.
* **Modelagem e Desacoplamento:** Comunicação entre módulos realizada via DTOs JSON serializados e busca reversa de dados para garantir consistência de ID do YouTube versus Nickname do usuário.
* **Controle Estrito de Threading:** Tratamento de condições de corrida (*race conditions*) em minigames síncronos usando exclusão mútua (`lock`) para premiações em milissegundos.

---

### 🇺🇸 English
This application was engineered as a **comprehensive backend engine for live stream management and real-time audience engagement on YouTube**. The system orchestrates high-throughput chat events, manages loyalty economies and monetization events, integrates with third-party REST services, and guarantees storage availability under concurrent loads.

**Software Engineering Highlights:**
* **Optimized Concurrency & Resilience:** Leverages SQLite with **WAL (Write-Ahead Logging)** mode and automated retry logic to eliminate database locking under high chat volume.
* **ACID Transactional Integrity:** Safe execution of financial events, reward points, and balance updates backed by explicit `BEGIN IMMEDIATE` and `ROLLBACK` blocks.
* **REST API Consumption:** Integrates with StreamElements REST API (JWT bearer authentication) and LivePix for currency conversion and cross-platform point imports.
* **Modular DTO Architecture:** Inter-action communication handled through JSON-serialized DTOs and identity resolution logic mapping user handles to persistent YouTube IDs (`UC...`).
* **Thread Safety & Race Conditions:** Uses thread locking mechanisms (`lock`) to safely execute synchronous real-time chat competitions with accurate priority placement.

---

## Stack Tecnológica

* **Linguagem / Runtime:** C# (.NET / CPHInline Proxy)
* **Banco de Dados:** SQLite (com WAL Journaling & Pragmas Otimizados)
* **APIs & Protocolos:** REST APIs, WebSockets, JSON (Newtonsoft.Json)
* **Padrões de Projeto:** DTO (Data Transfer Object), Repository, Retry Pattern, Mutex/Locking

---

## Organização dos Módulos / Project Structure

```text
streamerbot-youtube-automation/
├── src/
│   ├── chat/
│   │   ├── Youtube Gerente de Chat.cs            # Orquestrador central e roteador de eventos do chat
│   │   └── Youtube Salvar Mensagem.cs            # Logger e persistência assíncrona de mensagens
│   ├── core/
│   │   └── Youtube Gerente de Banco de Dados.cs  # Gerenciador de conexão SQLite, schema e queries genéricas
│   ├── donations/
│   │   ├── Youtube Consultar Meta.cs             # Consolidação e cálculo de metas de arrecadação
│   │   └── Youtube Recompensar Doações.cs        # Processador de Super Chats, Memberships e LivePix
│   ├── migration/
│   │   └── Youtube Importar Pontos SE.cs         # Módulo de migração REST API (StreamElements)
│   ├── minigames/
│   │   ├── Youtube Compara Palavra.cs            # Verificador thread-safe para minigames síncronos
│   │   └── Youtube Pontos Surpresa.cs            # Engine concorrente de eventos randômicos no chat
│   ├── points/
│   │   ├── Youtube Adicionar Pontos.cs           # Operações atômicas de transação de pontos
│   │   └── Youtube Consultar Pontos.cs           # Leitura de saldo, extrato e rankings
│   ├── soundboard/
│   │   └── Youtube Novo Áudio.cs                 # Módulo de cadastro dinâmico de mídias MP3 e gatilhos
│   └── startup/
│       └── Youtube Tarefas ao Iniciar a Live.cs  # Bootstrap e disparo de rotinas de início de transmissão
├── .gitignore
└── README.md
```

---

## Autor / Author

**Evandro Madeira**  
*Desenvolvedor de Software / Software Developer*

* **GitHub:** [@evandromadeira](https://github.com/evandromadeira)
