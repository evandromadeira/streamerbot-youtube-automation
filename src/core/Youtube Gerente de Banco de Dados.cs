using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Atualização 261003.1020
// Camada de dados da automação da live: centraliza todo o acesso ao SQLite (YoutubeStream.db)
public class CPHInline
{
    // ==================================================================
    // Inicialização do banco - Youtube Tarefas ao Iniciar a Live
    // ==================================================================

    // ------------------------------------------------------------------
    // Cria as tabelas e o catálogo inicial e adiciona as colunas complementares de doações
    // ------------------------------------------------------------------
    public bool GarantirSchema()
    {
        try
        {
            Ambiente ambiente = new Ambiente(CPH);

            if (string.IsNullOrEmpty(ambiente.PastaRaiz))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: Variável 'caminhoPastaStreamerBot' não encontrada!");
                return false;
            }

            if (!Directory.Exists(ambiente.PastaStream))
                Directory.CreateDirectory(ambiente.PastaStream);

            using (var connection = AbrirConexao(ambiente))
            {
                // ------------------------------------------------------------------
                // Criação das tabelas (idempotente — só cria se não existir)
                // ------------------------------------------------------------------
                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubeComandosAudio (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    grupoId INTEGER NOT NULL DEFAULT 0,
                    comando TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    arquivo TEXT NOT NULL,
                    custo INTEGER NOT NULL DEFAULT 0,
                    cooldownSegundos INTEGER NOT NULL DEFAULT 0,
                    ultimoUso TEXT,
                    ativo INTEGER NOT NULL DEFAULT 1);");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubeDoacoes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    userId TEXT NOT NULL,
                    userName TEXT NOT NULL,
                    tipoAcao TEXT NOT NULL,
                    valorOriginal REAL,
                    moedaOrigem TEXT,
                    valorBRL REAL,
                    pontosMeta INTEGER NOT NULL,
                    moedaGanha INTEGER NOT NULL,
                    multiplicador INTEGER NOT NULL,
                    broadcastUserId TEXT NOT NULL,
                    broadcastUserName TEXT NOT NULL,
                    timestamp TEXT NOT NULL);");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubeUsuariosMoeda (
                    userId TEXT PRIMARY KEY NOT NULL,
                    userName TEXT NOT NULL,
                    coinBalance INTEGER NOT NULL DEFAULT 0,
                    lastCoinAt TEXT,
                    broadcastUserId TEXT,
                    broadcastUserName TEXT);");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubeUsuariosPerfil (
                    userId TEXT PRIMARY KEY NOT NULL,
                    userName TEXT NOT NULL,
                    nivelMembro TEXT,
                    origemNivelMembro TEXT,
                    nivelMembroAtualizadoEm TEXT,
                    tempoTotalNivelMeses REAL,
                    tempoTotalMembroMeses REAL,
                    ultimaAlteracaoMembro TEXT,
                    ultimaAlteracaoMembroEm TEXT,
                    sexo TEXT,
                    origemSexo TEXT,
                    sexoAtualizadoEm TEXT);");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubeChatLog (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    userId TEXT,
                    userName TEXT,
                    messageId TEXT,
                    message TEXT,
                    broadcastUserId TEXT,
                    broadcastUserName TEXT,
                    isSubscribed INTEGER,
                    isSponsor INTEGER,
                    isModerator INTEGER,
                    userPreviousActive TEXT,
                    publishedAt TEXT);");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubePalpites (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    description TEXT NOT NULL,
                    options TEXT NOT NULL,
                    durationSeconds INTEGER NOT NULL,
                    createdAt TEXT NOT NULL,
                    endsAt TEXT NOT NULL,
                    createdByUserId TEXT NOT NULL,
                    createdByUserName TEXT NOT NULL,
                    status TEXT NOT NULL,
                    broadcastUserId TEXT,
                    broadcastUserName TEXT);");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubePalpiteRespostas (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    predictionId INTEGER NOT NULL,
                    userId TEXT NOT NULL,
                    userName TEXT NOT NULL,
                    chosenOption TEXT NOT NULL,
                    betAmount INTEGER NOT NULL DEFAULT 0,
                    betAt TEXT NOT NULL,
                    UNIQUE(predictionId, userId));");

                // ------------------------------------------------------------------
                // Sistema da Plataforma
                // ------------------------------------------------------------------
                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubePlataformaItens (
                    item TEXT PRIMARY KEY,
                    nomeExibicao TEXT NOT NULL,
                    descricao TEXT NOT NULL,
                    categoria TEXT NOT NULL,
                    slot TEXT,
                    tier INTEGER NOT NULL DEFAULT 0,
                    itemPrerequisito TEXT,
                    valor INTEGER NOT NULL,
                    limiteMaximo INTEGER NOT NULL,
                    estoqueGlobal INTEGER,
                    ativo INTEGER NOT NULL DEFAULT 1,
                    visivel INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY (itemPrerequisito) REFERENCES YoutubePlataformaItens(item)
                );");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubePlataformaResgates (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    userId TEXT NOT NULL,
                    userName TEXT NOT NULL,
                    item TEXT NOT NULL,
                    valorBase INTEGER NOT NULL,
                    percentualDesconto INTEGER NOT NULL DEFAULT 0,
                    nivelMembro TEXT,
                    valorPago INTEGER NOT NULL,
                    status TEXT NOT NULL DEFAULT 'APROVADO'
                        CHECK (status IN ('APROVADO', 'ESTORNADO')),
                    origem TEXT NOT NULL DEFAULT 'CHAT'
                        CHECK (origem IN ('CHAT', 'OVERLAY', 'ADMIN')),
                    metadata TEXT,
                    versaoRegra INTEGER NOT NULL DEFAULT 1,
                    timestamp TEXT NOT NULL,
                    FOREIGN KEY (item) REFERENCES YoutubePlataformaItens(item)
                );");

                Executar(connection, @"CREATE INDEX IF NOT EXISTS idx_resgates_usuario_item_status
                    ON YoutubePlataformaResgates (userId, item, status);");

                Executar(connection, @"CREATE INDEX IF NOT EXISTS idx_resgates_usuario_data_status
                    ON YoutubePlataformaResgates (userId, timestamp, status);");

                Executar(connection, @"CREATE INDEX IF NOT EXISTS idx_resgates_item_status
                    ON YoutubePlataformaResgates (item, status);");

                // ------------------------------------------------------------------
                // Insere o catálogo inicial da Plataforma
                // ------------------------------------------------------------------
                Executar(connection, @"INSERT OR IGNORE INTO YoutubePlataformaItens
                    (item, nomeExibicao, descricao, categoria, slot, tier, itemPrerequisito, valor, limiteMaximo, estoqueGlobal, ativo, visivel)
                VALUES
                    -- Estruturas Base e Upgrades
                    ('pacote_plataforma', 'Pacote Plataforma', 'Estrutura base obrigatoria contendo 1 Plataforma, 1 Suporte de Armadura, 1 Bau e 6 Molduras Simples.', 'ESTRUTURA', 'plataforma', 0, NULL, 250000, 1, NULL, 1, 1),
                    ('suporte_armadura', 'Suporte de Armadura', 'Suporte avulso adicional para exibicao de conjuntos de vestir.', 'ESTRUTURA', 'suporte_armadura', 0, 'pacote_plataforma', 150000, 4, NULL, 1, 1),
                    ('shulker_colorida', 'Shulker Colorida', 'Upgrade estetico que substitui visual e estruturalmente o Bau Padrao.', 'UPGRADE', 'bau', 0, 'pacote_plataforma', 200000, 1, NULL, 1, 1),
                    ('moldura_brilhante', 'Moldura Brilhante', 'Moldura especial reluzente para destacar armas ou ferramentas na plataforma.', 'UPGRADE', NULL, 0, 'pacote_plataforma', 100000, 6, NULL, 1, 1),
                    ('mob', 'Mob', 'Entidade viva decorativa para habitabilidade da plataforma.', 'MOB', 'mob', 0, 'pacote_plataforma', 500000, 1, NULL, 1, 1),
                    ('molde', 'Molde', 'Item consumivel para customizacao e acabamento visual.', 'CONSUMIVEL', NULL, 0, 'pacote_plataforma', 100000, 20, NULL, 1, 1),
                    ('encantamento', 'Encantamento', 'Aprimoramento magico consumivel para equipamentos.', 'CONSUMIVEL', NULL, 0, 'pacote_plataforma', 5000, 50, NULL, 1, 1),

                    -- Tier 1 - Cobre
                    ('capacete_cobre', 'Capacete de Cobre', 'Protecao de cabeca feita em cobre.', 'ARMADURA', 'capacete', 1, 'pacote_plataforma', 40000, 1, NULL, 1, 1),
                    ('peitoral_cobre', 'Peitoral de Cobre', 'Protecao de peito feita em cobre.', 'ARMADURA', 'peitoral', 1, 'pacote_plataforma', 40000, 1, NULL, 1, 1),
                    ('calca_cobre', 'Calça de Cobre', 'Protecao de pernas feita em cobre.', 'ARMADURA', 'calca', 1, 'pacote_plataforma', 40000, 1, NULL, 1, 1),
                    ('botas_cobre', 'Botas de Cobre', 'Protecao de pes feita em cobre.', 'ARMADURA', 'botas', 1, 'pacote_plataforma', 40000, 1, NULL, 1, 1),
                    ('espada_cobre', 'Espada de Cobre', 'Arma de corte basica feita em cobre.', 'ARMA', 'espada', 1, 'pacote_plataforma', 35000, 1, NULL, 1, 1),
                    ('lanca_cobre', 'Lança de Cobre', 'Arma de alcance basica feita em cobre.', 'ARMA', 'lanca', 1, 'pacote_plataforma', 35000, 1, NULL, 1, 1),
                    ('picareta_cobre', 'Picareta de Cobre', 'Ferramenta de mineracao basica feita em cobre.', 'FERRAMENTA', 'picareta', 1, 'pacote_plataforma', 35000, 1, NULL, 1, 1),
                    ('machado_cobre', 'Machado de Cobre', 'Ferramenta de corte de madeira basica feita em cobre.', 'FERRAMENTA', 'machado', 1, 'pacote_plataforma', 35000, 1, NULL, 1, 1),
                    ('pa_cobre', 'Pá de Cobre', 'Ferramenta de escavacao basica feita em cobre.', 'FERRAMENTA', 'pa', 1, 'pacote_plataforma', 35000, 1, NULL, 1, 1),
                    ('enxada_cobre', 'Enxada de Cobre', 'Ferramenta de cultivo basica feita em cobre.', 'FERRAMENTA', 'enxada', 1, 'pacote_plataforma', 35000, 1, NULL, 1, 1),

                    -- Tier 2 - Ferro
                    ('capacete_ferro', 'Capacete de Ferro', 'Protecao de cabeca feita em ferro resistente.', 'ARMADURA', 'capacete', 2, 'capacete_cobre', 80000, 1, NULL, 1, 1),
                    ('peitoral_ferro', 'Peitoral de Ferro', 'Protecao de peito feita em ferro resistente.', 'ARMADURA', 'peitoral', 2, 'peitoral_cobre', 80000, 1, NULL, 1, 1),
                    ('calca_ferro', 'Calça de Ferro', 'Protecao de pernas feita em ferro resistente.', 'ARMADURA', 'calca', 2, 'calca_cobre', 80000, 1, NULL, 1, 1),
                    ('botas_ferro', 'Botas de Ferro', 'Protecao de pes feita em ferro resistente.', 'ARMADURA', 'botas', 2, 'botas_cobre', 80000, 1, NULL, 1, 1),
                    ('espada_ferro', 'Espada de Ferro', 'Arma de corte intermediaria em ferro.', 'ARMA', 'espada', 2, 'espada_cobre', 70000, 1, NULL, 1, 1),
                    ('lanca_ferro', 'Lança de Ferro', 'Arma de alcance intermediaria em ferro.', 'ARMA', 'lanca', 2, 'lanca_cobre', 70000, 1, NULL, 1, 1),
                    ('picareta_ferro', 'Picareta de Ferro', 'Ferramenta de mineracao em ferro.', 'FERRAMENTA', 'picareta', 2, 'picareta_cobre', 70000, 1, NULL, 1, 1),
                    ('machado_ferro', 'Machado de Ferro', 'Ferramenta de corte de madeira em ferro.', 'FERRAMENTA', 'machado', 2, 'machado_cobre', 70000, 1, NULL, 1, 1),
                    ('pa_ferro', 'Pá de Ferro', 'Ferramenta de escavacao em ferro.', 'FERRAMENTA', 'pa', 2, 'pa_cobre', 70000, 1, NULL, 1, 1),
                    ('enxada_ferro', 'Enxada de Ferro', 'Ferramenta de cultivo em ferro.', 'FERRAMENTA', 'enxada', 2, 'enxada_cobre', 70000, 1, NULL, 1, 1),

                    -- Tier 3 - Ouro
                    ('capacete_ouro', 'Capacete de Ouro', 'Protecao de cabeca ostentacao em ouro.', 'ARMADURA', 'capacete', 3, 'capacete_ferro', 200000, 1, NULL, 1, 1),
                    ('peitoral_ouro', 'Peitoral de Ouro', 'Protecao de peito ostentacao em ouro.', 'ARMADURA', 'peitoral', 3, 'peitoral_ferro', 200000, 1, NULL, 1, 1),
                    ('calca_ouro', 'Calça de Ouro', 'Protecao de pernas ostentacao em ouro.', 'ARMADURA', 'calca', 3, 'calca_ferro', 200000, 1, NULL, 1, 1),
                    ('botas_ouro', 'Botas de Ouro', 'Protecao de pes ostentacao em ouro.', 'ARMADURA', 'botas', 3, 'botas_ferro', 200000, 1, NULL, 1, 1),
                    ('espada_ouro', 'Espada de Ouro', 'Arma de corte reluzente em ouro.', 'ARMA', 'espada', 3, 'espada_ferro', 175000, 1, NULL, 1, 1),
                    ('lanca_ouro', 'Lança de Ouro', 'Arma de alcance reluzente em ouro.', 'ARMA', 'lanca', 3, 'lanca_ferro', 175000, 1, NULL, 1, 1),
                    ('picareta_ouro', 'Picareta de Ouro', 'Ferramenta de mineracao em ouro.', 'FERRAMENTA', 'picareta', 3, 'picareta_ferro', 175000, 1, NULL, 1, 1),
                    ('machado_ouro', 'Machado de Ouro', 'Ferramenta de corte de madeira em ouro.', 'FERRAMENTA', 'machado', 3, 'machado_ferro', 175000, 1, NULL, 1, 1),
                    ('pa_ouro', 'Pá de Ouro', 'Ferramenta de escavacao em ouro.', 'FERRAMENTA', 'pa', 3, 'pa_ferro', 175000, 1, NULL, 1, 1),
                    ('enxada_ouro', 'Enxada de Ouro', 'Ferramenta de cultivo em ouro.', 'FERRAMENTA', 'enxada', 3, 'enxada_ferro', 175000, 1, NULL, 1, 1),

                    -- Tier 4 - Diamante
                    ('capacete_diamante', 'Capacete de Diamante', 'Protecao de cabeca de alta durabilidade em diamante.', 'ARMADURA', 'capacete', 4, 'capacete_ouro', 400000, 1, NULL, 1, 1),
                    ('peitoral_diamante', 'Peitoral de Diamante', 'Protecao de peito de alta durabilidade em diamante.', 'ARMADURA', 'peitoral', 4, 'peitoral_ouro', 400000, 1, NULL, 1, 1),
                    ('calca_diamante', 'Calça de Diamante', 'Protecao de pernas de alta durabilidade em diamante.', 'ARMADURA', 'calca', 4, 'calca_ouro', 400000, 1, NULL, 1, 1),
                    ('botas_diamante', 'Botas de Diamante', 'Protecao de pes de alta durabilidade em diamante.', 'ARMADURA', 'botas', 4, 'botas_ouro', 400000, 1, NULL, 1, 1),
                    ('espada_diamante', 'Espada de Diamante', 'Arma de corte avancada em diamante.', 'ARMA', 'espada', 4, 'espada_ouro', 350000, 1, NULL, 1, 1),
                    ('lanca_diamante', 'Lança de Diamante', 'Arma de alcance avancada em diamante.', 'ARMA', 'lanca', 4, 'lanca_ouro', 350000, 1, NULL, 1, 1),
                    ('picareta_diamante', 'Picareta de Diamante', 'Ferramenta de mineracao avancada em diamante.', 'FERRAMENTA', 'picareta', 4, 'picareta_ouro', 350000, 1, NULL, 1, 1),
                    ('machado_diamante', 'Machado de Diamante', 'Ferramenta de corte de madeira avancada em diamante.', 'FERRAMENTA', 'machado', 4, 'machado_ouro', 350000, 1, NULL, 1, 1),
                    ('pa_diamante', 'Pá de Diamante', 'Ferramenta de escavacao avancada em diamante.', 'FERRAMENTA', 'pa', 4, 'pa_ouro', 350000, 1, NULL, 1, 1),
                    ('enxada_diamante', 'Enxada de Diamante', 'Ferramenta de cultivo avancada em diamante.', 'FERRAMENTA', 'enxada', 4, 'enxada_ouro', 350000, 1, NULL, 1, 1),

                    -- Tier 5 - Netherite
                    ('capacete_netherite', 'Capacete de Netherite', 'Protecao suprema de cabeca em netherite.', 'ARMADURA', 'capacete', 5, 'capacete_diamante', 800000, 1, NULL, 1, 1),
                    ('peitoral_netherite', 'Peitoral de Netherite', 'Protecao suprema de peito em netherite.', 'ARMADURA', 'peitoral', 5, 'peitoral_diamante', 800000, 1, NULL, 1, 1),
                    ('calca_netherite', 'Calça de Netherite', 'Protecao suprema de pernas em netherite.', 'ARMADURA', 'calca', 5, 'calca_diamante', 800000, 1, NULL, 1, 1),
                    ('botas_netherite', 'Botas de Netherite', 'Protecao suprema de pes em netherite.', 'ARMADURA', 'botas', 5, 'botas_diamante', 800000, 1, NULL, 1, 1),
                    ('espada_netherite', 'Espada de Netherite', 'Arma de corte lendaria e suprema em netherite.', 'ARMA', 'espada', 5, 'espada_diamante', 700000, 1, NULL, 1, 1),
                    ('lanca_netherite', 'Lança de Netherite', 'Arma de alcance lendaria e suprema em netherite.', 'ARMA', 'lanca', 5, 'lanca_diamante', 700000, 1, NULL, 1, 1),
                    ('picareta_netherite', 'Picareta de Netherite', 'Ferramenta de mineracao suprema em netherite.', 'FERRAMENTA', 'picareta', 5, 'picareta_diamante', 700000, 1, NULL, 1, 1),
                    ('machado_netherite', 'Machado de Netherite', 'Ferramenta de corte de madeira suprema em netherite.', 'FERRAMENTA', 'machado', 5, 'machado_diamante', 700000, 1, NULL, 1, 1),
                    ('pa_netherite', 'Pá de Netherite', 'Ferramenta de escavacao suprema em netherite.', 'FERRAMENTA', 'pa', 5, 'pa_diamante', 700000, 1, NULL, 1, 1),
                    ('enxada_netherite', 'Enxada de Netherite', 'Ferramenta de cultivo suprema em netherite.', 'FERRAMENTA', 'enxada', 5, 'enxada_diamante', 700000, 1, NULL, 1, 1);");

                // ------------------------------------------------------------------
                // Migrações incrementais da tabela de doações
                // ------------------------------------------------------------------
                AdicionarColunaSeNaoExistir(connection, "YoutubeDoacoes", "tier", "TEXT");
                AdicionarColunaSeNaoExistir(connection, "YoutubeDoacoes", "broadcastId", "TEXT");
                AdicionarColunaSeNaoExistir(connection, "YoutubeDoacoes", "messageId", "TEXT");

                CPH.LogInfo(">>> [GERENTE_DB] Schema verificado/atualizado com sucesso.");
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO CRÍTICO ao garantir schema: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Adiciona uma coluna a uma tabela existente, só se ela ainda não existir
    // ------------------------------------------------------------------
    private void AdicionarColunaSeNaoExistir(SQLiteConnection connection, string tabela, string coluna, string tipoDefinicao)
    {
        bool colunaExiste = false;
        using (var cmd = new SQLiteCommand($"PRAGMA table_info({tabela});", connection))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader["name"].ToString(), coluna, StringComparison.OrdinalIgnoreCase))
                {
                    colunaExiste = true;
                    break;
                }
            }
        }

        if (colunaExiste)
            return;

        using (var cmd = new SQLiteCommand($"ALTER TABLE {tabela} ADD COLUMN {coluna} {tipoDefinicao};", connection))
        {
            cmd.ExecuteNonQuery();
            CPH.LogInfo($">>> [GERENTE_DB] Coluna '{coluna}' adicionada em '{tabela}'.");
        }
    }

    // ==================================================================
    // Youtube Gerente de Chat
    // ==================================================================

    // ------------------------------------------------------------------
    // Salva uma mensagem de chat recebida na tabela de log
    // ------------------------------------------------------------------
    public bool SalvarChatLog()
    {
        try
        {
            CPH.TryGetArg("chatLogUserId", out string userId);
            CPH.TryGetArg("chatLogUserName", out string userName);
            CPH.TryGetArg("chatLogMessageId", out string messageId);
            CPH.TryGetArg("chatLogMessage", out string message);
            CPH.TryGetArg("chatLogBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("chatLogBroadcastUserName", out string broadcastUserName);
            CPH.TryGetArg("chatLogIsSubscribed", out bool isSubscribed);
            CPH.TryGetArg("chatLogIsSponsor", out bool isSponsor);
            CPH.TryGetArg("chatLogIsModerator", out bool isModerator);
            CPH.TryGetArg("chatLogUserPreviousActive", out string userPreviousActive);
            CPH.TryGetArg("chatLogPublishedAt", out string publishedAt);

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                string insertSql = @"INSERT INTO YoutubeChatLog
                                    (userId, userName, messageId, message, broadcastUserId, broadcastUserName, isSubscribed, isSponsor, isModerator, userPreviousActive, publishedAt)
                                    VALUES
                                    (@userId, @userName, @messageId, @message, @broadcastUserId, @broadcastUserName, @isSubscribed, @isSponsor, @isModerator, @userPreviousActive, @publishedAt);";
                using (var cmd = new SQLiteCommand(insertSql, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@userName", userName);
                    cmd.Parameters.AddWithValue("@messageId", messageId);
                    cmd.Parameters.AddWithValue("@message", message);
                    cmd.Parameters.AddWithValue("@broadcastUserId", broadcastUserId);
                    cmd.Parameters.AddWithValue("@broadcastUserName", broadcastUserName);
                    cmd.Parameters.AddWithValue("@isSubscribed", isSubscribed ? 1 : 0);
                    cmd.Parameters.AddWithValue("@isSponsor", isSponsor ? 1 : 0);
                    cmd.Parameters.AddWithValue("@isModerator", isModerator ? 1 : 0);
                    cmd.Parameters.AddWithValue("@userPreviousActive", userPreviousActive);
                    cmd.Parameters.AddWithValue("@publishedAt", publishedAt);

                    // Retry simples em caso de "database is locked" mesmo com WAL (picos de concorrência)
                    int tentativas = 0;
                    const int maxTentativas = 3;
                    while (true)
                    {
                        try
                        {
                            cmd.ExecuteNonQuery();
                            break;
                        }
                        catch (SQLiteException sqlEx) when (sqlEx.ResultCode == SQLiteErrorCode.Busy || sqlEx.ResultCode == SQLiteErrorCode.Locked)
                        {
                            tentativas++;
                            if (tentativas >= maxTentativas)
                                throw;
                            CPH.LogWarn($">>> [GERENTE_DB] Banco ocupado ao salvar ChatLog, tentativa {tentativas}/{maxTentativas}...");
                            System.Threading.Thread.Sleep(150 * tentativas);
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao salvar ChatLog: " + ex.Message);
            return false;
        }
    }

    // ==================================================================
    // Youtube Gerente de Áudio
    // ==================================================================

    // Tabelas aceitas pelo método SalvarRegistro.
    private static readonly HashSet<string> TabelasPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "YoutubeComandosAudio",
        "YoutubeUsuariosMoeda",
        "YoutubeChatLog"
    };

    // ------------------------------------------------------------------
    // Consulta o grupo de um apelido existente ou calcula o próximo grupoId para cadastro
    // ------------------------------------------------------------------
    public bool ObterOuCriarGrupoIdAudio()
    {
        try
        {
            CPH.TryGetArg("grupoAudioAliasesJson", out string aliasesJson);
            var aliases = JsonConvert.DeserializeObject<List<string>>(aliasesJson ?? "[]");
            if (aliases == null || aliases.Count == 0)
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: nenhum apelido informado para resolver grupoId.");
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                int grupoIdExistente = 0;
                string placeholders = string.Join(",", aliases.Select((_, i) => $"@a{i}"));
                using (var cmd = new SQLiteCommand($"SELECT grupoId FROM YoutubeComandosAudio WHERE comando IN ({placeholders}) COLLATE NOCASE LIMIT 1", connection))
                {
                    for (int i = 0; i < aliases.Count; i++)
                        cmd.Parameters.AddWithValue($"@a{i}", aliases[i]);
                    var resultado = cmd.ExecuteScalar();
                    if (resultado != null && resultado != DBNull.Value)
                        grupoIdExistente = Convert.ToInt32(resultado);
                }

                if (grupoIdExistente > 0)
                {
                    CPH.SetArgument("grupoIdResultado", grupoIdExistente);
                    return true;
                }

                int novoGrupoId;
                using (var cmd = new SQLiteCommand("SELECT COALESCE(MAX(grupoId), 0) + 1 FROM YoutubeComandosAudio", connection))
                {
                    novoGrupoId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                CPH.SetArgument("grupoIdResultado", novoGrupoId);
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao obter grupoId: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Insere ou atualiza um registro por JSON; usado no cadastro dos apelidos de áudio
    // ------------------------------------------------------------------
    public bool SalvarRegistro()
    {
        try
        {
            CPH.TryGetArg("salvarTabela", out string tabela);
            CPH.TryGetArg("salvarColunasJson", out string colunasJson);
            CPH.TryGetArg("salvarChaveConflito", out string chaveConflito);
            CPH.TryGetArg("salvarColunasSomenteInsercaoJson", out string somenteInsercaoJson);

            if (string.IsNullOrEmpty(tabela) || string.IsNullOrEmpty(colunasJson) || string.IsNullOrEmpty(chaveConflito))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: parâmetros insuficientes para SalvarRegistro.");
                return false;
            }

            if (!TabelasPermitidas.Contains(tabela))
            {
                CPH.LogError($">>> [GERENTE_DB] ERRO: tabela '{tabela}' não está na lista de tabelas permitidas para SalvarRegistro.");
                return false;
            }

            var colunas = JsonConvert.DeserializeObject<Dictionary<string, object>>(colunasJson);
            if (colunas == null || colunas.Count == 0 || !colunas.ContainsKey(chaveConflito))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: colunas inválidas ou chave de conflito ausente entre as colunas.");
                return false;
            }

            var somenteInsercao = string.IsNullOrEmpty(somenteInsercaoJson) ? new List<string>() : JsonConvert.DeserializeObject<List<string>>(somenteInsercaoJson);

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                var nomesColunas = colunas.Keys.ToList();
                string listaColunas = string.Join(", ", nomesColunas);
                string listaValores = string.Join(", ", nomesColunas.Select(c => "@" + c));
                string listaUpdate = string.Join(", ", nomesColunas.Where(c => c != chaveConflito && !somenteInsercao.Contains(c, StringComparer.OrdinalIgnoreCase)).Select(c => $"{c} = @{c}"));
                if (string.IsNullOrEmpty(listaUpdate))
                    listaUpdate = $"{chaveConflito} = {chaveConflito}";

                string sql = $@"INSERT INTO {tabela} ({listaColunas})
                                VALUES ({listaValores})
                                ON CONFLICT({chaveConflito}) DO UPDATE SET {listaUpdate};";
                using (var cmd = new SQLiteCommand(sql, connection))
                {
                    foreach (var kvp in colunas)
                        cmd.Parameters.AddWithValue("@" + kvp.Key, kvp.Value ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao executar SalvarRegistro: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Busca um comando de áudio ativo pelo nome digitado no chat
    // ------------------------------------------------------------------
    public bool BuscarAudioPorComando()
    {
        try
        {
            CPH.TryGetArg("buscarAudioComando", out string comando);
            if (string.IsNullOrEmpty(comando))
            {
                CPH.SetArgument("audioEncontrado", false);
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente))
            using (var cmd = new SQLiteCommand("SELECT arquivo, custo, grupoId, cooldownSegundos, ultimoUso FROM YoutubeComandosAudio WHERE comando = @comando COLLATE NOCASE AND ativo = 1 LIMIT 1", connection))
            {
                cmd.Parameters.AddWithValue("@comando", comando);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        CPH.SetArgument("audioEncontrado", true);
                        CPH.SetArgument("audioArquivo", reader["arquivo"].ToString());
                        CPH.SetArgument("audioCusto", Convert.ToInt32(reader["custo"]));
                        CPH.SetArgument("audioGrupoId", Convert.ToInt32(reader["grupoId"]));
                        CPH.SetArgument("audioCooldownSegundos", Convert.ToInt32(reader["cooldownSegundos"]));
                        CPH.SetArgument("audioUltimoUso", reader["ultimoUso"] == DBNull.Value ? "" : reader["ultimoUso"].ToString());
                    }
                    else
                    {
                        CPH.SetArgument("audioEncontrado", false);
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao buscar áudio por comando: " + ex.Message);
            CPH.SetArgument("audioEncontrado", false);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Lista um áudio representante de cada grupo ativo, pro comando !audios
    // ------------------------------------------------------------------
    public bool ListarAudiosPorGrupo()
    {
        try
        {
            Ambiente ambiente = new Ambiente(CPH);

            var lista = new List<AudioResumo>();
            using (var connection = AbrirConexao(ambiente))
            {
                string sql = @"SELECT grupoId, comando, custo
                                 FROM YoutubeComandosAudio
                                WHERE ativo = 1
                                ORDER BY grupoId, LENGTH(comando) ASC, comando ASC;";
                using (var cmd = new SQLiteCommand(sql, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    int grupoAnterior = -1;
                    while (reader.Read())
                    {
                        int grupoId = Convert.ToInt32(reader["grupoId"]);
                        if (grupoId == grupoAnterior)
                            continue;
                        grupoAnterior = grupoId;
                        lista.Add(new AudioResumo { GrupoId = grupoId, Comando = reader["comando"].ToString(), Custo = Convert.ToInt32(reader["custo"]) });
                    }
                }
            }

            lista = lista.OrderBy(a => a.Comando, StringComparer.OrdinalIgnoreCase).ToList();
            CPH.SetArgument("audiosListaJson", JsonConvert.SerializeObject(lista));
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao listar áudios: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Debita moedas de um usuário para reproduzir um áudio pago
    // ------------------------------------------------------------------
    public bool DebitarMoedasUsuario()
    {
        try
        {
            CPH.TryGetArg("debitarUserId", out string userId);
            CPH.TryGetArg("debitarBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("debitarCusto", out int custo);

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(broadcastUserId))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: userId/broadcastUserId ausente para DebitarMoedasUsuario.");
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
                return DebitarMoedas(connection, userId, custo, broadcastUserId);
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao debitar moedas: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Atualiza o timestamp de último uso de um grupo de áudio (controle de cooldown)
    // ------------------------------------------------------------------
    public bool AtualizarUltimoUsoAudio()
    {
        try
        {
            CPH.TryGetArg("atualizarUltimoUsoGrupoId", out int grupoId);
            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente))
            using (var cmd = new SQLiteCommand("UPDATE YoutubeComandosAudio SET ultimoUso = @agora WHERE grupoId = @grupoId;", connection))
            {
                cmd.Parameters.AddWithValue("@agora", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@grupoId", grupoId);
                cmd.ExecuteNonQuery();
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao atualizar último uso do áudio: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Representa um grupo de áudio resumido pra listagem do comando !audios
    // ------------------------------------------------------------------
    public class AudioResumo
    {
        public int GrupoId { get; set; }
        public string Comando { get; set; }
        public int Custo { get; set; }
    }

    // ==================================================================
    // Youtube Gerente de Moedas - consulta de saldo também usada pela importação
    // ==================================================================

    // ------------------------------------------------------------------
    // Consulta saldo e ranking de um usuário, por id ou por nome
    // ------------------------------------------------------------------
    public bool SaldoMoedasUsuario()
    {
        try
        {
            CPH.TryGetArg("consultarChave", out string chave);
            CPH.TryGetArg("consultarPorId", out bool consultarPorId);

            if (string.IsNullOrEmpty(chave))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: chave ausente para SaldoMoedasUsuario.");
                CPH.SetArgument("consultarEncontrado", false);
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);

            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: banco de dados não encontrado para SaldoMoedasUsuario.");
                CPH.SetArgument("consultarEncontrado", false);
                return false;
            }

            using (var connection = AbrirConexao(ambiente))
            {
                string selectSql = consultarPorId
                    ? "SELECT userName, coinBalance, lastCoinAt FROM YoutubeUsuariosMoeda WHERE userId = @chave"
                    : "SELECT userName, coinBalance, lastCoinAt FROM YoutubeUsuariosMoeda WHERE userName = @chave COLLATE NOCASE";

                string nomeExibido = null;
                int? moedasUsuario = null;
                string ultimoCredito = null;

                using (var cmd = new SQLiteCommand(selectSql, connection))
                {
                    cmd.Parameters.AddWithValue("@chave", chave);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            nomeExibido = reader["userName"].ToString();
                            moedasUsuario = Convert.ToInt32(reader["coinBalance"]);
                            ultimoCredito = reader["lastCoinAt"] == DBNull.Value ? null : reader["lastCoinAt"].ToString();
                        }
                    }
                }

                if (moedasUsuario == null)
                {
                    CPH.SetArgument("consultarEncontrado", false);
                    return true;
                }

                int rankUsuario;
                using (var rankCmd = new SQLiteCommand("SELECT COUNT(*) FROM YoutubeUsuariosMoeda WHERE coinBalance > @moedas", connection))
                {
                    rankCmd.Parameters.AddWithValue("@moedas", moedasUsuario.Value);
                    rankUsuario = Convert.ToInt32(rankCmd.ExecuteScalar()) + 1;
                }

                CPH.SetArgument("consultarEncontrado", true);
                CPH.SetArgument("consultarNomeExibido", nomeExibido);
                CPH.SetArgument("consultarMoedas", moedasUsuario.Value);
                CPH.SetArgument("consultarRank", rankUsuario);
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao consultar moedas: " + ex.Message);
            CPH.SetArgument("consultarEncontrado", false);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Monta o ranking dos N maiores saldos de moeda, pro comando !topmoedas
    // ------------------------------------------------------------------
    public bool ConsultarTopMoedas()
    {
        try
        {
            CPH.TryGetArg("topMoedasQuantidade", out int quantidade);
            if (quantidade <= 0)
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: quantidade inválida para ConsultarTopMoedas.");
                CPH.SetArgument("topMoedasResultadoJson", "[]");
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);

            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: banco de dados não encontrado para ConsultarTopMoedas.");
                CPH.SetArgument("topMoedasResultadoJson", "[]");
                return false;
            }

            var itens = new List<TopMoedaItem>();
            using (var connection = AbrirConexao(ambiente))
            {
                string sql = @"SELECT userName, coinBalance
                                 FROM YoutubeUsuariosMoeda
                                ORDER BY coinBalance DESC, userName COLLATE NOCASE ASC
                                LIMIT @quantidade;";
                using (var cmd = new SQLiteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@quantidade", quantidade);
                    using (var reader = cmd.ExecuteReader())
                    {
                        int? saldoAnterior = null;
                        int rankAnterior = 0;
                        int posicao = 0;
                        while (reader.Read())
                        {
                            posicao++;
                            int saldo = Convert.ToInt32(reader["coinBalance"]);
                            int rank = (saldoAnterior.HasValue && saldo == saldoAnterior.Value) ? rankAnterior : posicao;

                            itens.Add(new TopMoedaItem
                            {
                                Rank = rank,
                                NomeExibido = reader["userName"].ToString(),
                                Moedas = saldo
                            });

                            saldoAnterior = saldo;
                            rankAnterior = rank;
                        }
                    }
                }
            }

            CPH.SetArgument("topMoedasResultadoJson", JsonConvert.SerializeObject(itens));
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao consultar top de moedas: " + ex.Message);
            CPH.SetArgument("topMoedasResultadoJson", "[]");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Credita moedas a um usuário, com regra de cooldown para atividade de chat
    // ------------------------------------------------------------------
    public bool AdicionarMoedasUsuario()
    {
        string creditoStatus = "falhou";
        bool commitIniciado = false;
        bool commitConfirmado = false;

        try
        {
            CPH.SetArgument("adicionarCreditoStatus", creditoStatus);
            CPH.SetArgument("adicionarErro", "");
            CPH.SetArgument("adicionarDestinatarioId", "");
            CPH.SetArgument("adicionarDestinatarioNomeExibido", "");
            CPH.SetArgument("adicionarResultado", "");

            CPH.TryGetArg("adicionarOrigem", out string origem);
            CPH.TryGetArg("adicionarUserId", out string userId);
            CPH.TryGetArg("adicionarUserName", out string userName);
            CPH.TryGetArg("adicionarQuantidade", out int quantidadeMoedas);
            CPH.TryGetArg("adicionarCooldownMinutos", out int cooldownMinutos);
            CPH.TryGetArg("adicionarBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("adicionarBroadcastUserName", out string broadcastUserName);

            bool ehAtividadeChat = origem == "chat_atividade";

            if ((string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(userName)) || quantidadeMoedas <= 0)
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: parâmetros inválidos para AdicionarMoedasUsuario.");
                CPH.SetArgument("adicionarErro", "Parâmetros inválidos para adicionar moedas.");
                CPH.SetArgument("adicionarResultado", "ParametrosInvalidos");
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);

            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.SetArgument("adicionarErro", "Banco de dados não encontrado.");
                CPH.SetArgument("adicionarResultado", "BancoNaoEncontrado");
                return true;
            }

            using (var connection = AbrirConexao(ambiente))
            {
                IniciarTransacao(connection);

                try
                {
                    // Resolve o destinatário pelo nome sempre que o userId informado não bater com
                    // nenhum registro existente — cobre casos em que o YouTube manda um userId
                    // diferente do que já está salvo (não só quando o userId vem vazio).
                    string destinatarioId = userId;

                    if (!string.IsNullOrEmpty(destinatarioId) && !UserIdExiste(connection, destinatarioId) && !string.IsNullOrEmpty(userName))
                    {
                        string idPeloNome = BuscarUserIdPorNome(connection, userName);
                        if (!string.IsNullOrEmpty(idPeloNome))
                        {
                            CPH.LogInfo($">>> [GERENTE_DB] userId '{destinatarioId}' não encontrado, resolvido via nome '{userName}' -> '{idPeloNome}'.");
                            destinatarioId = idPeloNome;
                        }
                    }
                    else if (string.IsNullOrEmpty(destinatarioId) && !string.IsNullOrEmpty(userName))
                    {
                        destinatarioId = BuscarUserIdPorNome(connection, userName);
                        if (string.IsNullOrEmpty(destinatarioId))
                        {
                            destinatarioId = userName;
                            CPH.LogWarn($">>> [GERENTE_DB] Usuário '{userName}' não encontrado por userId — criando/creditando conta identificada pelo nome.");
                        }
                    }

                    CPH.SetArgument("adicionarDestinatarioId", destinatarioId);

                    // Cooldown de atividade de chat: só se aplica à origem "chat_atividade" e quando
                    // o chamador informa um valor > 0, checado na mesma transação do upsert.
                    if (ehAtividadeChat)
                    {
                        DateTime? ultimoCredito = BuscarUltimoCreditoAtividade(connection, destinatarioId);
                        if (cooldownMinutos > 0 && ultimoCredito.HasValue && (DateTime.Now - ultimoCredito.Value).TotalMinutes < cooldownMinutos)
                        {
                            RollbackTransacao(connection);
                            CPH.SetArgument("adicionarResultado", "EmCooldown");
                            return true;
                        }
                    }

                    // lastCoinAt só é tocado quando a origem é atividade de chat — doações, moedas
                    // surpresa, importação e !adicionar não resetam o cooldown de atividade.
                    string sql = ehAtividadeChat
                                ? @"INSERT INTO YoutubeUsuariosMoeda (userId, userName, coinBalance, lastCoinAt, broadcastUserId, broadcastUserName)
                                    VALUES (@userId, @userName, @quantidadeMoedas, @agora, @broadcastUserId, @broadcastUserName)
                                    ON CONFLICT(userId) DO UPDATE SET
                                    coinBalance = coinBalance + excluded.coinBalance,
                                    lastCoinAt = excluded.lastCoinAt,
                                    userName = excluded.userName,
                                    broadcastUserId = COALESCE(excluded.broadcastUserId, broadcastUserId),
                                    broadcastUserName = COALESCE(excluded.broadcastUserName, broadcastUserName);"
                                : @"INSERT INTO YoutubeUsuariosMoeda (userId, userName, coinBalance, lastCoinAt, broadcastUserId, broadcastUserName)
                                    VALUES (@userId, @userName, @quantidadeMoedas, NULL, @broadcastUserId, @broadcastUserName)
                                    ON CONFLICT(userId) DO UPDATE SET
                                    coinBalance = coinBalance + excluded.coinBalance,
                                    userName = excluded.userName,
                                    broadcastUserId = COALESCE(excluded.broadcastUserId, broadcastUserId),
                                    broadcastUserName = COALESCE(excluded.broadcastUserName, broadcastUserName);";

                    using (var cmd = new SQLiteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@userId", destinatarioId);
                        cmd.Parameters.AddWithValue("@userName", userName);
                        cmd.Parameters.AddWithValue("@quantidadeMoedas", quantidadeMoedas);

                        if (ehAtividadeChat)
                            cmd.Parameters.AddWithValue("@agora", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                        cmd.Parameters.AddWithValue("@broadcastUserId", string.IsNullOrEmpty(broadcastUserId) ? (object)DBNull.Value : broadcastUserId);
                        cmd.Parameters.AddWithValue("@broadcastUserName", (object)broadcastUserName ?? DBNull.Value);
                        creditoStatus = "incerto";
                        cmd.ExecuteNonQuery();
                    }

                    string nomeDestinatario = BuscarUserNamePorId(connection, destinatarioId) ?? userName;
                    commitIniciado = true;
                    ConfirmarTransacao(connection);
                    commitConfirmado = true;
                    creditoStatus = "creditado";

                    CPH.SetArgument("adicionarCreditoStatus", creditoStatus);
                    CPH.SetArgument("adicionarResultado", "Sucesso");
                    CPH.SetArgument("adicionarDestinatarioNomeExibido", nomeDestinatario);
                }
                catch
                {
                    if (!commitConfirmado)
                    {
                        try
                        {
                            RollbackTransacao(connection);
                            if (!commitIniciado) creditoStatus = "falhou";
                        }
                        catch (Exception rollbackEx)
                        {
                            creditoStatus = "incerto";
                            CPH.LogError(">>> [GERENTE_DB] ERRO ao desfazer crédito de moedas: " + rollbackEx.Message);
                        }
                    }
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao adicionar moedas: " + ex.Message);
            CPH.SetArgument("adicionarCreditoStatus", creditoStatus);
            CPH.SetArgument("adicionarErro", ex.Message);
            CPH.SetArgument("adicionarResultado", "Erro");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Transfere moedas de um usuário para outro, validando saldo do remetente
    // ------------------------------------------------------------------
    public bool TransferirMoedasUsuario()
    {
        try
        {
            CPH.TryGetArg("transferirRemetenteUserId", out string remetenteUserId);
            CPH.TryGetArg("transferirDestinatarioNome", out string destinatarioNome);
            CPH.TryGetArg("transferirQuantidade", out int quantidade);

            if (string.IsNullOrEmpty(remetenteUserId) || string.IsNullOrEmpty(destinatarioNome) || quantidade <= 0)
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: parâmetros inválidos para TransferirMoedasUsuario.");
                CPH.SetArgument("transferirResultado", "Erro");
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                IniciarTransacao(connection);

                try
                {
                    string destinatarioId = BuscarUserIdPorNome(connection, destinatarioNome);
                    if (string.IsNullOrEmpty(destinatarioId))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("transferirResultado", "DestinatarioNaoEncontrado");
                        return true;
                    }

                    if (destinatarioId == remetenteUserId)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("transferirResultado", "AutoTransferencia");
                        return true;
                    }

                    int saldoRemetente = ObterSaldoUsuario(connection, remetenteUserId);
                    if (saldoRemetente < quantidade)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("transferirResultado", "SaldoInsuficiente");
                        CPH.SetArgument("transferirSaldoRemetente", saldoRemetente);
                        return true;
                    }

                    if (!DebitarMoedas(connection, remetenteUserId, quantidade))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("transferirResultado", "SaldoInsuficiente");
                        CPH.SetArgument("transferirSaldoRemetente", ObterSaldoUsuario(connection, remetenteUserId));
                        return true;
                    }

                    if (!CreditarMoedas(connection, destinatarioId, quantidade))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("transferirResultado", "DestinatarioNaoEncontrado");
                        return true;
                    }

                    string nomeDestinatario = BuscarUserNamePorId(connection, destinatarioId) ?? destinatarioNome;
                    ConfirmarTransacao(connection);

                    CPH.SetArgument("transferirResultado", "Sucesso");
                    CPH.SetArgument("transferirDestinatarioNomeExibido", nomeDestinatario);
                }
                catch
                {
                    RollbackTransacao(connection);
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao transferir moedas: " + ex.Message);
            CPH.SetArgument("transferirResultado", "Erro");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Verifica se um userId já existe na tabela de moedas
    // ------------------------------------------------------------------
    private bool UserIdExiste(SQLiteConnection connection, string userId)
    {
        using (var cmd = new SQLiteCommand("SELECT 1 FROM YoutubeUsuariosMoeda WHERE userId = @userId LIMIT 1", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            return cmd.ExecuteScalar() != null;
        }
    }

    // ------------------------------------------------------------------
    // Busca a data do último crédito por atividade de chat de um usuário
    // ------------------------------------------------------------------
    private DateTime? BuscarUltimoCreditoAtividade(SQLiteConnection connection, string userId)
    {
        using (var cmd = new SQLiteCommand("SELECT lastCoinAt FROM YoutubeUsuariosMoeda WHERE userId = @userId", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            if (resultado == null || resultado == DBNull.Value) return null;
            return DateTime.Parse(resultado.ToString());
        }
    }

    // ------------------------------------------------------------------
    // Representa uma posição no ranking do comando !topmoedas
    // ------------------------------------------------------------------
    public class TopMoedaItem
    {
        public int Rank { get; set; }
        public string NomeExibido { get; set; }
        public int Moedas { get; set; }
    }

    // ==================================================================
    // Youtube Gerente de Palpite
    // ==================================================================

    // ------------------------------------------------------------------
    // Abre uma nova rodada de palpite, bloqueando se já existir uma em aberto
    // ------------------------------------------------------------------
    public bool CriarPalpite()
    {
        try
        {
            CPH.TryGetArg("novoPalpiteDescription", out string description);
            CPH.TryGetArg("novoPalpiteOptions", out string options);
            CPH.TryGetArg("novoPalpiteDurationSeconds", out int durationSeconds);
            CPH.TryGetArg("novoPalpiteCreatedAt", out string createdAt);
            CPH.TryGetArg("novoPalpiteEndsAt", out string endsAt);
            CPH.TryGetArg("novoPalpiteCreatedByUserId", out string createdByUserId);
            CPH.TryGetArg("novoPalpiteCreatedByUserName", out string createdByUserName);
            CPH.TryGetArg("novoPalpiteBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("novoPalpiteBroadcastUserName", out string broadcastUserName);

            if (string.IsNullOrEmpty(description) || string.IsNullOrEmpty(options) || durationSeconds <= 0)
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: parâmetros inválidos para CriarPalpite.");
                CPH.SetArgument("criarPalpiteResultado", "Erro");
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                IniciarTransacao(connection);

                try
                {
                    using (var checkCmd = new SQLiteCommand("SELECT 1 FROM YoutubePalpites WHERE status = 'open' LIMIT 1", connection))
                    {
                        var existente = checkCmd.ExecuteScalar();
                        if (existente != null)
                        {
                            RollbackTransacao(connection);
                            CPH.SetArgument("criarPalpiteResultado", "RodadaJaAberta");
                            return true;
                        }
                    }

                    using (var insertCmd = new SQLiteCommand(@"INSERT INTO YoutubePalpites
                                        (description, options, durationSeconds, createdAt, endsAt, createdByUserId, createdByUserName, status, broadcastUserId, broadcastUserName)
                                        VALUES (@description, @options, @durationSeconds, @createdAt, @endsAt, @createdByUserId, @createdByUserName, 'open', @broadcastUserId, @broadcastUserName);", connection))
                    {
                        insertCmd.Parameters.AddWithValue("@description", description);
                        insertCmd.Parameters.AddWithValue("@options", options);
                        insertCmd.Parameters.AddWithValue("@durationSeconds", durationSeconds);
                        insertCmd.Parameters.AddWithValue("@createdAt", createdAt);
                        insertCmd.Parameters.AddWithValue("@endsAt", endsAt);
                        insertCmd.Parameters.AddWithValue("@createdByUserId", createdByUserId);
                        insertCmd.Parameters.AddWithValue("@createdByUserName", createdByUserName);
                        insertCmd.Parameters.AddWithValue("@broadcastUserId", (object)broadcastUserId ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@broadcastUserName", (object)broadcastUserName ?? DBNull.Value);
                        insertCmd.ExecuteNonQuery();
                    }

                    ConfirmarTransacao(connection);

                    CPH.SetArgument("criarPalpiteResultado", "Sucesso");
                }
                catch
                {
                    RollbackTransacao(connection);
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao criar palpite: " + ex.Message);
            CPH.SetArgument("criarPalpiteResultado", "Erro");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Registra (ou soma) a aposta de um usuário na rodada de palpite aberta
    // ------------------------------------------------------------------
    public bool ApostarPalpite()
    {
        try
        {
            CPH.TryGetArg("apostarPalpiteUserId", out string userId);
            CPH.TryGetArg("apostarPalpiteUserName", out string userName);
            CPH.TryGetArg("apostarPalpiteOption", out string option);
            CPH.TryGetArg("apostarPalpiteValor", out int valor);
            CPH.TryGetArg("apostarPalpiteBroadcastUserId", out string broadcastUserId);

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(option) || valor <= 0)
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: parâmetros inválidos para ApostarPalpite.");
                CPH.SetArgument("apostarPalpiteResultado", "Erro");
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                IniciarTransacao(connection);

                try
                {
                    int predictionId = 0;
                    string optionsRaw = null;
                    string endsAtRaw = null;
                    string agora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    using (var cmd = new SQLiteCommand("SELECT id, options, endsAt FROM YoutubePalpites WHERE status = 'open' ORDER BY id DESC LIMIT 1", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            predictionId = Convert.ToInt32(reader["id"]);
                            optionsRaw = reader["options"].ToString();
                            endsAtRaw = reader["endsAt"].ToString();
                        }
                    }

                    if (predictionId == 0)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("apostarPalpiteResultado", "SemRodadaAberta");
                        return true;
                    }

                    if (DateTime.Parse(endsAtRaw) <= DateTime.Parse(agora))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("apostarPalpiteResultado", "RodadaEncerrada");
                        return true;
                    }

                    var options = optionsRaw.Split(';');
                    int indiceOpcao = option[0] - 'a';
                    if (indiceOpcao < 0 || indiceOpcao >= options.Length)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("apostarPalpiteResultado", "OpcaoInvalida");
                        return true;
                    }

                    int saldoAtual = ObterSaldoUsuario(connection, userId);
                    if (saldoAtual < valor)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("apostarPalpiteResultado", "SaldoInsuficiente");
                        CPH.SetArgument("apostarPalpiteSaldoAtual", saldoAtual);
                        return true;
                    }

                    int totalUsuario = valor;

                    string chosenOptionExistente = null;
                    using (var cmd = new SQLiteCommand("SELECT chosenOption FROM YoutubePalpiteRespostas WHERE predictionId = @predictionId AND userId = @userId", connection))
                    {
                        cmd.Parameters.AddWithValue("@predictionId", predictionId);
                        cmd.Parameters.AddWithValue("@userId", userId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                                chosenOptionExistente = reader["chosenOption"].ToString();
                        }
                    }

                    if (chosenOptionExistente != null && !string.Equals(chosenOptionExistente, option, StringComparison.OrdinalIgnoreCase))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("apostarPalpiteResultado", "OpcaoDiferente");
                        CPH.SetArgument("apostarPalpiteOpcaoAtual", chosenOptionExistente);
                        return true;
                    }

                    if (chosenOptionExistente != null)
                    {
                        using (var updateCmd = new SQLiteCommand(@"UPDATE YoutubePalpiteRespostas
                                                                    SET betAmount = betAmount + @valor, betAt = @agora
                                                                  WHERE predictionId = @predictionId AND userId = @userId;", connection))
                        {
                            updateCmd.Parameters.AddWithValue("@valor", valor);
                            updateCmd.Parameters.AddWithValue("@agora", agora);
                            updateCmd.Parameters.AddWithValue("@predictionId", predictionId);
                            updateCmd.Parameters.AddWithValue("@userId", userId);
                            updateCmd.ExecuteNonQuery();
                        }

                        using (var totalCmd = new SQLiteCommand("SELECT betAmount FROM YoutubePalpiteRespostas WHERE predictionId = @predictionId AND userId = @userId", connection))
                        {
                            totalCmd.Parameters.AddWithValue("@predictionId", predictionId);
                            totalCmd.Parameters.AddWithValue("@userId", userId);
                            totalUsuario = Convert.ToInt32(totalCmd.ExecuteScalar());
                        }
                    }
                    else
                    {
                        using (var insertCmd = new SQLiteCommand(@"INSERT INTO YoutubePalpiteRespostas
                                            (predictionId, userId, userName, chosenOption, betAmount, betAt)
                                            VALUES (@predictionId, @userId, @userName, @chosenOption, @valor, @agora);", connection))
                        {
                            insertCmd.Parameters.AddWithValue("@predictionId", predictionId);
                            insertCmd.Parameters.AddWithValue("@userId", userId);
                            insertCmd.Parameters.AddWithValue("@userName", userName);
                            insertCmd.Parameters.AddWithValue("@chosenOption", option);
                            insertCmd.Parameters.AddWithValue("@valor", valor);
                            insertCmd.Parameters.AddWithValue("@agora", agora);
                            insertCmd.ExecuteNonQuery();
                        }
                    }

                    if (!DebitarMoedas(connection, userId, valor))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("apostarPalpiteResultado", "SaldoInsuficiente");
                        CPH.SetArgument("apostarPalpiteSaldoAtual", ObterSaldoUsuario(connection, userId));
                        return true;
                    }

                    ConfirmarTransacao(connection);

                    CPH.SetArgument("apostarPalpiteResultado", "Sucesso");
                    CPH.SetArgument("apostarPalpiteTotalUsuario", totalUsuario);
                }
                catch
                {
                    RollbackTransacao(connection);
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao registrar aposta de palpite: " + ex.Message);
            CPH.SetArgument("apostarPalpiteResultado", "Erro");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Verifica se algum palpite aberto acabou de passar do prazo, pra avisar no chat
    // ------------------------------------------------------------------
    public bool VerificarEncerramentoPalpite()
    {
        try
        {
            CPH.TryGetArg("verificarEncerramentoIntervaloSegundos", out int intervaloSegundos);
            if (intervaloSegundos <= 0) intervaloSegundos = 3;

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                string agora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string janelaInicio = DateTime.Now.AddSeconds(-intervaloSegundos).ToString("yyyy-MM-dd HH:mm:ss");

                int predictionId = 0;
                string description = null;
                string optionsRaw = null;

                using (var cmd = new SQLiteCommand(@"SELECT id, description, options FROM YoutubePalpites
                                                      WHERE status = 'open' AND endsAt <= @agora AND endsAt > @janelaInicio
                                                      LIMIT 1", connection))
                {
                    cmd.Parameters.AddWithValue("@agora", agora);
                    cmd.Parameters.AddWithValue("@janelaInicio", janelaInicio);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            predictionId = Convert.ToInt32(reader["id"]);
                            description = reader["description"].ToString();
                            optionsRaw = reader["options"].ToString();
                        }
                    }
                }

                if (predictionId == 0)
                {
                    CPH.SetArgument("palpiteEncerradoEncontrado", false);
                    return true;
                }

                var totaisPorOpcao = new Dictionary<string, int>();
                using (var cmd = new SQLiteCommand("SELECT chosenOption, SUM(betAmount) as total FROM YoutubePalpiteRespostas WHERE predictionId = @predictionId GROUP BY chosenOption", connection))
                {
                    cmd.Parameters.AddWithValue("@predictionId", predictionId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            totaisPorOpcao[reader["chosenOption"].ToString()] = Convert.ToInt32(reader["total"]);
                    }
                }

                CPH.SetArgument("palpiteEncerradoEncontrado", true);
                CPH.SetArgument("palpiteEncerradoDescription", description);
                CPH.SetArgument("palpiteEncerradoOptions", optionsRaw);
                CPH.SetArgument("palpiteEncerradoTotaisJson", JsonConvert.SerializeObject(totaisPorOpcao));
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao verificar encerramento de palpite: " + ex.Message);
            CPH.SetArgument("palpiteEncerradoEncontrado", false);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Declara a opção vencedora do palpite aberto e paga os vencedores pelo pool proporcional
    // ------------------------------------------------------------------
    public bool ResolverPalpite()
    {
        try
        {
            CPH.TryGetArg("resolverPalpiteOpcaoVencedora", out string opcaoVencedora);

            if (string.IsNullOrEmpty(opcaoVencedora))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: parâmetros inválidos para ResolverPalpite.");
                CPH.SetArgument("resolverPalpiteResultado", "Erro");
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                IniciarTransacao(connection);

                try
                {
                    int predictionId = 0;
                    string description = null;
                    string optionsRaw = null;
                    using (var cmd = new SQLiteCommand("SELECT id, description, options FROM YoutubePalpites WHERE status = 'open' ORDER BY id DESC LIMIT 1", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            predictionId = Convert.ToInt32(reader["id"]);
                            description = reader["description"].ToString();
                            optionsRaw = reader["options"].ToString();
                        }
                    }

                    if (predictionId == 0)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("resolverPalpiteResultado", "SemRodadaAberta");
                        return true;
                    }

                    var options = optionsRaw.Split(';');
                    int indiceOpcao = opcaoVencedora[0] - 'a';
                    if (indiceOpcao < 0 || indiceOpcao >= options.Length)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("resolverPalpiteResultado", "OpcaoInvalida");
                        return true;
                    }

                    var vencedores = new List<ApostaVencedora>();
                    int poteTotal = 0;
                    int poteVencedores = 0;

                    using (var cmd = new SQLiteCommand("SELECT userId, userName, chosenOption, betAmount FROM YoutubePalpiteRespostas WHERE predictionId = @predictionId ORDER BY betAt ASC", connection))
                    {
                        cmd.Parameters.AddWithValue("@predictionId", predictionId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string userId = reader["userId"].ToString();
                                string userName = reader["userName"].ToString();
                                string chosenOption = reader["chosenOption"].ToString();
                                int betAmount = Convert.ToInt32(reader["betAmount"]);

                                poteTotal += betAmount;
                                if (string.Equals(chosenOption, opcaoVencedora, StringComparison.OrdinalIgnoreCase))
                                {
                                    vencedores.Add(new ApostaVencedora { UserId = userId, UserName = userName, BetAmount = betAmount });
                                    poteVencedores += betAmount;
                                }
                            }
                        }
                    }

                    if (vencedores.Count == 0)
                    {
                        DevolverApostas(connection, predictionId);

                        using (var statusCmd = new SQLiteCommand("UPDATE YoutubePalpites SET status = 'cancelled' WHERE id = @id;", connection))
                        {
                            statusCmd.Parameters.AddWithValue("@id", predictionId);
                            statusCmd.ExecuteNonQuery();
                        }

                        ConfirmarTransacao(connection);

                        CPH.SetArgument("resolverPalpiteResultado", "SemGanhadores");
                        return true;
                    }

                    int potePerdedores = poteTotal - poteVencedores;

                    // Ordena pelo valor apostado; em empate, preserva a ordem crescente de betAt.
                    // betAt é atualizado a cada aposta adicional; o primeiro recebe a sobra do arredondamento.
                    var vencedoresOrdenados = vencedores.OrderByDescending(v => v.BetAmount).ToList();

                    var pagamentos = new Dictionary<string, int>();
                    int totalDistribuido = 0;
                    foreach (var vencedor in vencedoresOrdenados)
                    {
                        int parteProporcional = poteVencedores > 0 ? (int)((long)vencedor.BetAmount * potePerdedores / poteVencedores) : 0;
                        int pago = vencedor.BetAmount + parteProporcional;
                        pagamentos[vencedor.UserId] = pago;
                        totalDistribuido += parteProporcional;
                    }

                    int sobra = potePerdedores - totalDistribuido;
                    if (sobra > 0)
                    {
                        string idMaiorApostador = vencedoresOrdenados.First().UserId;
                        pagamentos[idMaiorApostador] += sobra;
                    }

                    int totalPago = 0;
                    foreach (var vencedor in vencedores)
                    {
                        int valorPago = pagamentos[vencedor.UserId];
                        totalPago += valorPago;

                        if (!CreditarMoedas(connection, vencedor.UserId, valorPago))
                        {
                            throw new InvalidOperationException("Usuário vencedor não encontrado para crédito de moedas.");
                        }
                    }

                    using (var statusCmd = new SQLiteCommand("UPDATE YoutubePalpites SET status = 'resolved' WHERE id = @id;", connection))
                    {
                        statusCmd.Parameters.AddWithValue("@id", predictionId);
                        statusCmd.ExecuteNonQuery();
                    }

                    ConfirmarTransacao(connection);

                    CPH.SetArgument("resolverPalpiteResultado", "Sucesso");
                    CPH.SetArgument("resolverPalpiteDescription", description);
                    CPH.SetArgument("resolverPalpiteTotalPago", totalPago);
                    CPH.SetArgument("resolverPalpiteQtdVencedores", vencedores.Count);
                }
                catch
                {
                    RollbackTransacao(connection);
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao resolver palpite: " + ex.Message);
            CPH.SetArgument("resolverPalpiteResultado", "Erro");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Cancela o palpite aberto e devolve o valor apostado a todos os participantes
    // ------------------------------------------------------------------
    public bool CancelarPalpite()
    {
        try
        {
            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                IniciarTransacao(connection);

                try
                {
                    int predictionId = 0;
                    using (var cmd = new SQLiteCommand("SELECT id FROM YoutubePalpites WHERE status = 'open' ORDER BY id DESC LIMIT 1", connection))
                    {
                        var resultado = cmd.ExecuteScalar();
                        if (resultado != null && resultado != DBNull.Value)
                            predictionId = Convert.ToInt32(resultado);
                    }

                    if (predictionId == 0)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("cancelarPalpiteResultado", "SemRodadaAberta");
                        return true;
                    }

                    DevolverApostas(connection, predictionId);

                    using (var statusCmd = new SQLiteCommand("UPDATE YoutubePalpites SET status = 'cancelled' WHERE id = @id;", connection))
                    {
                        statusCmd.Parameters.AddWithValue("@id", predictionId);
                        statusCmd.ExecuteNonQuery();
                    }

                    ConfirmarTransacao(connection);

                    CPH.SetArgument("cancelarPalpiteResultado", "Sucesso");
                }
                catch
                {
                    RollbackTransacao(connection);
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao cancelar palpite: " + ex.Message);
            CPH.SetArgument("cancelarPalpiteResultado", "Erro");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Devolve o valor apostado a todos os participantes de uma rodada de palpite
    // ------------------------------------------------------------------
    private void DevolverApostas(SQLiteConnection connection, int predictionId)
    {
        var reembolsos = new Dictionary<string, int>();
        using (var cmd = new SQLiteCommand("SELECT userId, betAmount FROM YoutubePalpiteRespostas WHERE predictionId = @predictionId", connection))
        {
            cmd.Parameters.AddWithValue("@predictionId", predictionId);
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    reembolsos[reader["userId"].ToString()] = Convert.ToInt32(reader["betAmount"]);
            }
        }

        foreach (var reembolso in reembolsos)
        {
            if (!CreditarMoedas(connection, reembolso.Key, reembolso.Value))
                throw new InvalidOperationException("Usuário não encontrado para devolução de moedas.");
        }
    }

    // ------------------------------------------------------------------
    // Guarda um apostador vencedor enquanto o pagamento proporcional é calculado
    // ------------------------------------------------------------------
    private class ApostaVencedora
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public int BetAmount { get; set; }
    }

    // ==================================================================
    // Youtube Gerente de Doações e Youtube Consultar Meta
    // ==================================================================

    // ------------------------------------------------------------------
    // Procura doações com o mesmo usuário, canal, tipo e tier nos últimos 15 segundos
    // ------------------------------------------------------------------
    public bool VerificarDoacaoDuplicada()
    {
        try
        {
            CPH.SetArgument("doacaoDuplicada", false);
            CPH.SetArgument("doacaoDuplicidadeErro", "");

            CPH.TryGetArg("doacaoDupUserId", out string userId);
            CPH.TryGetArg("doacaoDupBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("doacaoDupTipoAcao", out string tipoAcao);
            CPH.TryGetArg("doacaoDupTier", out string tier);

            Ambiente ambiente = new Ambiente(CPH);

            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.SetArgument("doacaoDuplicidadeErro", "Banco de dados não encontrado.");
                return false;
            }

            using (var connection = AbrirConexao(ambiente))
            {
                TimeSpan janelaDedup = TimeSpan.FromSeconds(15); // Janela usada na consulta de duplicidade
                string limiteTimestamp = DateTime.Now.Subtract(janelaDedup).ToString("yyyy-MM-dd HH:mm:ss");

                string sql = @"SELECT COUNT(*) FROM YoutubeDoacoes
                                WHERE userId = @userId
                                  AND broadcastUserId = @broadcastUserId
                                  AND tipoAcao = @tipoAcao
                                  AND tier = @tier
                                  AND timestamp >= @limiteTimestamp;";
                using (var cmd = new SQLiteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@broadcastUserId", broadcastUserId);
                    cmd.Parameters.AddWithValue("@tipoAcao", tipoAcao);
                    cmd.Parameters.AddWithValue("@tier", tier ?? "");
                    cmd.Parameters.AddWithValue("@limiteTimestamp", limiteTimestamp);

                    bool duplicado = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                    CPH.SetArgument("doacaoDuplicada", duplicado);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao checar doação duplicada: " + ex.Message);
            CPH.SetArgument("doacaoDuplicidadeErro", ex.Message);
            CPH.SetArgument("doacaoDuplicada", false); // O retorno false sinaliza ao chamador que a consulta falhou
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Registra uma doação (Super Chat, Membership, Tip, etc.) na tabela de doações
    // ------------------------------------------------------------------
    public bool SalvarDoacao()
    {
        string registroStatus = "falhou";

        try
        {
            CPH.SetArgument("doacaoRegistroStatus", registroStatus);
            CPH.SetArgument("doacaoRegistroErro", "");
            CPH.SetArgument("doacaoRegistroId", 0L);

            CPH.TryGetArg("doacaoUserId", out string userId);
            CPH.TryGetArg("doacaoUserName", out string userName);
            CPH.TryGetArg("doacaoTipoAcao", out string tipoAcao);
            CPH.TryGetArg("doacaoValorOriginal", out double valorOriginal);
            CPH.TryGetArg("doacaoMoedaOrigem", out string moedaOrigem);
            CPH.TryGetArg("doacaoValorBRL", out double valorBRL);
            CPH.TryGetArg("doacaoPontosMeta", out int pontosMeta);
            CPH.TryGetArg("doacaoMoedaGanha", out int moedaGanha);
            CPH.TryGetArg("doacaoMultiplicador", out int multiplicador);
            CPH.TryGetArg("doacaoBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("doacaoBroadcastUserName", out string broadcastUserName);
            CPH.TryGetArg("doacaoTier", out string tier);
            CPH.TryGetArg("doacaoBroadcastId", out string broadcastId);
            CPH.TryGetArg("doacaoMessageId", out string messageId);
            CPH.TryGetArg("doacaoTimestamp", out string timestamp);

            if (string.IsNullOrEmpty(timestamp))
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else if (!DateTime.TryParseExact(timestamp, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dataDoacao))
            {
                throw new ArgumentException("doacaoTimestamp deve usar o formato yyyy-MM-dd HH:mm:ss.");
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                string insertSql = @"INSERT INTO YoutubeDoacoes
                                    (userId, userName, tipoAcao, valorOriginal, moedaOrigem, valorBRL, pontosMeta, moedaGanha, multiplicador, broadcastUserId, broadcastUserName, timestamp, tier, broadcastId, messageId)
                                    VALUES (@userId, @userName, @tipoAcao, @valorOriginal, @moedaOrigem, @valorBRL, @pontosMeta, @moedaGanha, @multiplicador, @broadcastUserId, @broadcastUserName, @timestamp, @tier, @broadcastId, @messageId);";
                using (var cmd = new SQLiteCommand(insertSql, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@userName", userName);
                    cmd.Parameters.AddWithValue("@tipoAcao", tipoAcao);
                    cmd.Parameters.AddWithValue("@valorOriginal", valorOriginal);
                    cmd.Parameters.AddWithValue("@moedaOrigem", moedaOrigem ?? "BRL");
                    cmd.Parameters.AddWithValue("@valorBRL", valorBRL);
                    cmd.Parameters.AddWithValue("@pontosMeta", pontosMeta);
                    cmd.Parameters.AddWithValue("@moedaGanha", moedaGanha);
                    cmd.Parameters.AddWithValue("@multiplicador", multiplicador);
                    cmd.Parameters.AddWithValue("@broadcastUserId", broadcastUserId);
                    cmd.Parameters.AddWithValue("@broadcastUserName", broadcastUserName);
                    cmd.Parameters.AddWithValue("@timestamp", timestamp);
                    cmd.Parameters.AddWithValue("@tier", (object)tier ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@broadcastId", (object)broadcastId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@messageId", (object)messageId ?? DBNull.Value);
                    registroStatus = "incerto";
                    cmd.ExecuteNonQuery();
                    registroStatus = "salvo";
                    CPH.SetArgument("doacaoRegistroStatus", registroStatus);
                }

                using (var cmdId = new SQLiteCommand("SELECT last_insert_rowid();", connection))
                {
                    CPH.SetArgument("doacaoRegistroId", Convert.ToInt64(cmdId.ExecuteScalar()));
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao salvar doação: " + ex.Message);
            CPH.SetArgument("doacaoRegistroStatus", registroStatus);
            CPH.SetArgument("doacaoRegistroErro", ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Consulta o progresso atual da meta de doações/pontos
    // ------------------------------------------------------------------
    public bool ObterProgressoMeta()
    {
        try
        {
            CPH.SetArgument("metaConsultaSucesso", false);
            CPH.TryGetArg("metaBroadcastUserName", out string broadcastUserName);
            if (string.IsNullOrEmpty(broadcastUserName))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: broadcastUserName ausente para ObterProgressoMeta.");
                CPH.SetArgument("metaProgressoMensal", 0);
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);

            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.SetArgument("metaProgressoMensal", 0);
                CPH.LogError(">>> [GERENTE_DB] ERRO ao consultar meta: banco de dados não encontrado em " + ambiente.CaminhoBanco);
                return false;
            }

            using (var connection = AbrirConexao(ambiente))
            {
                string selectSql = @"SELECT COALESCE(SUM(pontosMeta), 0) FROM YoutubeDoacoes
                                      WHERE broadcastUserName = @broadcastUserName
                                    COLLATE NOCASE
                                        AND timestamp >= date('now', 'localtime', 'start of month');";
                using (var cmd = new SQLiteCommand(selectSql, connection))
                {
                    cmd.Parameters.AddWithValue("@broadcastUserName", broadcastUserName);
                    var resultado = cmd.ExecuteScalar();
                    int progresso = resultado != null ? Convert.ToInt32(resultado) : 0;
                    CPH.SetArgument("metaProgressoMensal", progresso);
                }
            }

            CPH.SetArgument("metaConsultaSucesso", true);
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao obter progresso da meta: " + ex.Message);
            CPH.SetArgument("metaProgressoMensal", 0);
            return false;
        }
    }

    // ==================================================================
    // Youtube Gerente de Estatísticas
    // ==================================================================

    // ------------------------------------------------------------------
    // Conta em quantos dias diferentes um usuário mandou mensagem no chat
    // ------------------------------------------------------------------
    public bool ObterDiasDePresenca()
    {
        return ObterDiasDePresencaComFiltro(null);
    }

    // ------------------------------------------------------------------
    // Conta em quantos dias diferentes um usuário mandou mensagem no chat, só no mês atual (hora local)
    // ------------------------------------------------------------------
    public bool ObterDiasDePresencaNoMes()
    {
        var inicioMes = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        return ObterDiasDePresencaComFiltro(inicioMes);
    }

    // ------------------------------------------------------------------
    // Implementação compartilhada da consulta de dias de presença, com filtro de data opcional
    // ------------------------------------------------------------------
    private bool ObterDiasDePresencaComFiltro(DateTime? desde)
    {
        try
        {
            CPH.TryGetArg("presencaUserId", out string userId);
            CPH.TryGetArg("presencaUserName", out string userName);

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                if (string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(userName))
                {
                    userId = BuscarUserIdPorNome(connection, userName);
                }

                if (string.IsNullOrEmpty(userId))
                {
                    CPH.SetArgument("presencaEncontrado", false);
                    return true;
                }

                int dias = ContarDiasPresenca(connection, userId, desde);
                string nomeResolvido = BuscarUserNamePorId(connection, userId);

                CPH.SetArgument("presencaEncontrado", dias > 0);
                CPH.SetArgument("presencaDias", dias);
                CPH.SetArgument("presencaUserNameResolvido", nomeResolvido ?? userName ?? "");
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao obter dias de presença: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Conta dias distintos com mensagem no YoutubeChatLog, opcionalmente a partir de uma data
    // ------------------------------------------------------------------
    private int ContarDiasPresenca(SQLiteConnection connection, string userId, DateTime? desde)
    {
        string sql = "SELECT COUNT(DISTINCT DATE(publishedAt)) FROM YoutubeChatLog WHERE userId = @userId";
        if (desde.HasValue)
            sql += " AND publishedAt >= @desde";

        using (var cmd = new SQLiteCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            if (desde.HasValue)
                cmd.Parameters.AddWithValue("@desde", desde.Value.ToString("yyyy-MM-dd HH:mm:ss"));

            var resultado = cmd.ExecuteScalar();
            return resultado != null && resultado != DBNull.Value ? Convert.ToInt32(resultado) : 0;
        }
    }

    // ==================================================================
    // Youtube Gerente de Perfil
    // ==================================================================

    // ------------------------------------------------------------------
    // Atualiza um campo permitido do perfil de um usuário
    // ------------------------------------------------------------------
    public bool AtualizarPerfilUsuario()
    {
        try
        {
            CPH.TryGetArg("perfilUserName", out string userName);
            CPH.TryGetArg("perfilCampo", out string campo);
            CPH.TryGetArg("perfilValor", out string valor);
            CPH.TryGetArg("perfilOrigem", out string origem);

            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(campo) || string.IsNullOrEmpty(valor))
            {
                CPH.SetArgument("perfilResultado", "ParametrosInvalidos");
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                string userId = BuscarUserIdPorNome(connection, userName);

                if (string.IsNullOrEmpty(userId))
                {
                    CPH.SetArgument("perfilResultado", "UsuarioNaoEncontrado");
                    return true;
                }

                string userNameResolvido = BuscarUserNamePorId(connection, userId) ?? userName;
                string atualizadoEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string sql;

                switch (campo)
                {
                    case "nivelMembro":
                        sql = @"INSERT INTO YoutubeUsuariosPerfil
                                    (userId, userName, nivelMembro, origemNivelMembro, nivelMembroAtualizadoEm)
                                VALUES
                                    (@userId, @userName, @valor, @origem, @atualizadoEm)
                                ON CONFLICT(userId) DO UPDATE SET
                                    userName = excluded.userName,
                                    nivelMembro = excluded.nivelMembro,
                                    origemNivelMembro = excluded.origemNivelMembro,
                                    nivelMembroAtualizadoEm = excluded.nivelMembroAtualizadoEm;";
                        break;
                    case "sexo":
                        sql = @"INSERT INTO YoutubeUsuariosPerfil
                                    (userId, userName, sexo, origemSexo, sexoAtualizadoEm)
                                VALUES
                                    (@userId, @userName, @valor, @origem, @atualizadoEm)
                                ON CONFLICT(userId) DO UPDATE SET
                                    userName = excluded.userName,
                                    sexo = excluded.sexo,
                                    origemSexo = excluded.origemSexo,
                                    sexoAtualizadoEm = excluded.sexoAtualizadoEm;";
                        break;
                    default:
                        CPH.SetArgument("perfilResultado", "CampoInvalido");
                        return true;
                }

                using (var cmd = new SQLiteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@userName", userNameResolvido);
                    cmd.Parameters.AddWithValue("@valor", valor);
                    cmd.Parameters.AddWithValue("@origem", string.IsNullOrEmpty(origem) ? "CHAT" : origem);
                    cmd.Parameters.AddWithValue("@atualizadoEm", atualizadoEm);
                    cmd.ExecuteNonQuery();
                }
            }

            CPH.SetArgument("perfilResultado", "Sucesso");
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao atualizar perfil do usuário: " + ex.Message);
            return false;
        }
    }

    // ==================================================================
    // Youtube Gerente de Plataforma
    // ==================================================================

    private const int CotaDiariaResgatesPlataforma = 3;

    // ------------------------------------------------------------------
    // Remove o nível cadastrado quando o evento de resgate informa que o usuário não é membro
    // ------------------------------------------------------------------
    public bool RemoverNivelMembroUsuario()
    {
        try
        {
            CPH.TryGetArg("plataformaRemoverNivelUserId", out string userId);
            if (string.IsNullOrEmpty(userId))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: userId ausente para RemoverNivelMembroUsuario.");
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente))
            using (var cmd = new SQLiteCommand(@"UPDATE YoutubeUsuariosPerfil
                                                   SET nivelMembro = NULL,
                                                       origemNivelMembro = 'YOUTUBE',
                                                       nivelMembroAtualizadoEm = @atualizadoEm
                                                 WHERE userId = @userId
                                                   AND nivelMembro IS NOT NULL;", connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@atualizadoEm", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao remover nível de membro: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Consulta o nível de membro de um usuário para as regras da Plataforma
    // ------------------------------------------------------------------
    public bool ConsultarNivelMembroUsuario()
    {
        try
        {
            CPH.TryGetArg("plataformaConsultarNivelUserId", out string userId);
            CPH.SetArgument("plataformaNivelMembro", "");

            if (string.IsNullOrEmpty(userId))
                return true;

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente))
            using (var cmd = new SQLiteCommand("SELECT nivelMembro FROM YoutubeUsuariosPerfil WHERE userId = @userId LIMIT 1", connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId);
                var resultado = cmd.ExecuteScalar();
                if (resultado != null && resultado != DBNull.Value)
                    CPH.SetArgument("plataformaNivelMembro", resultado.ToString());
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao consultar nível de membro: " + ex.Message);
            CPH.SetArgument("plataformaNivelMembro", "");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Consulta um item visível do catálogo da Plataforma
    // ------------------------------------------------------------------
    public bool ConsultarItemPlataforma()
    {
        try
        {
            CPH.TryGetArg("plataformaConsultarItem", out string item);

            if (string.IsNullOrEmpty(item))
            {
                CPH.SetArgument("plataformaItemEncontrado", false);
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                PlataformaItem itemCatalogo = BuscarItemPlataforma(connection, item);

                if (itemCatalogo == null || !itemCatalogo.Visivel)
                {
                    CPH.SetArgument("plataformaItemEncontrado", false);
                    return true;
                }

                CPH.SetArgument("plataformaItemEncontrado", true);
                CPH.SetArgument("plataformaItemJson", JsonConvert.SerializeObject(itemCatalogo));
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao consultar item da Plataforma: " + ex.Message);
            CPH.SetArgument("plataformaItemEncontrado", false);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Valida saldo, cotas, pré-requisito e estoque e registra o resgate na mesma transação
    // ------------------------------------------------------------------
    public bool ResgatarItemPlataforma()
    {
        try
        {
            CPH.TryGetArg("plataformaUserId", out string userId);
            CPH.TryGetArg("plataformaUserName", out string userName);
            CPH.TryGetArg("plataformaItem", out string item);
            CPH.TryGetArg("plataformaOrigem", out string origem);
            CPH.TryGetArg("plataformaMetadata", out string metadata);
            CPH.TryGetArg("plataformaVersaoRegra", out int versaoRegra);
            CPH.TryGetArg("plataformaPercentualDesconto", out int percentualDesconto);
            CPH.TryGetArg("plataformaNivelMembro", out string nivelMembro);

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(item))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: parâmetros inválidos para ResgatarItemPlataforma.");
                CPH.SetArgument("plataformaResultado", "ParametrosInvalidos");
                return true;
            }

            if (string.IsNullOrEmpty(origem))
                origem = "CHAT";

            origem = origem.ToUpperInvariant();

            if (origem != "CHAT" && origem != "OVERLAY" && origem != "ADMIN")
            {
                CPH.LogError($">>> [GERENTE_DB] ERRO: origem inválida para resgate da Plataforma: '{origem}'.");
                CPH.SetArgument("plataformaResultado", "OrigemInvalida");
                return true;
            }

            if (versaoRegra <= 0)
                versaoRegra = 1;

            // Usa o mesmo percentual validado no cálculo e no registro do resgate.
            if (percentualDesconto < 0 || percentualDesconto > 100)
            {
                CPH.LogWarn($">>> [GERENTE_DB] Percentual de desconto inválido: {percentualDesconto}. Usando 0%.");
                percentualDesconto = 0;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                IniciarTransacao(connection);

                try
                {
                    PlataformaItem itemCatalogo = BuscarItemPlataforma(connection, item);

                    if (itemCatalogo == null)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaResultado", "ItemNaoEncontrado");
                        return true;
                    }

                    if (!itemCatalogo.Ativo)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaResultado", "ItemInativo");
                        CPH.SetArgument("plataformaNomeItem", itemCatalogo.NomeExibicao);
                        return true;
                    }

                    // Valida o saldo do usuário
                    int saldoAtual = ObterSaldoUsuario(connection, userId);
                    int valorItem = CalcularValorItemComDesconto(itemCatalogo.Valor, percentualDesconto);

                    CPH.SetArgument("plataformaSaldoAtual", saldoAtual);
                    CPH.SetArgument("plataformaValorItem", valorItem);
                    CPH.SetArgument("plataformaNomeItem", itemCatalogo.NomeExibicao);

                    if (saldoAtual < valorItem)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaResultado", "SaldoInsuficiente");
                        return true;
                    }

                    // Valida a cota diária de resgates
                    DateTime inicioHoje = DateTime.Today;
                    DateTime inicioAmanha = inicioHoje.AddDays(1);

                    int resgatesHoje;
                    using (var cmd = new SQLiteCommand(@"SELECT COUNT(*)
                            FROM YoutubePlataformaResgates
                        WHERE userId = @userId
                            AND status = 'APROVADO'
                            AND timestamp >= @inicioHoje
                            AND timestamp < @inicioAmanha;", connection))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@inicioHoje", inicioHoje.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@inicioAmanha", inicioAmanha.ToString("yyyy-MM-dd HH:mm:ss"));

                        resgatesHoje = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    CPH.SetArgument("plataformaResgatesHoje", resgatesHoje);

                    if (resgatesHoje >= CotaDiariaResgatesPlataforma)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaResultado", "LimiteDiarioAtingido");
                        return true;
                    }

                    // Valida o limite máximo do item por espectador
                    int quantidadeJaResgatada;
                    using (var cmd = new SQLiteCommand(@"SELECT COUNT(*)
                            FROM YoutubePlataformaResgates
                        WHERE userId = @userId
                            AND item = @item
                            AND status = 'APROVADO';", connection))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@item", itemCatalogo.Item);

                        quantidadeJaResgatada = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    CPH.SetArgument("plataformaQuantidadeAtual", quantidadeJaResgatada);
                    CPH.SetArgument("plataformaLimiteItem", itemCatalogo.LimiteMaximo);

                    if (quantidadeJaResgatada >= itemCatalogo.LimiteMaximo)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaResultado", "LimiteItemAtingido");
                        return true;
                    }

                    // Valida o pré-requisito do item
                    if (!string.IsNullOrEmpty(itemCatalogo.ItemPrerequisito))
                    {
                        bool possuiPrerequisito;

                        using (var cmd = new SQLiteCommand(@"SELECT 1
                                FROM YoutubePlataformaResgates
                            WHERE userId = @userId
                                AND item = @itemPrerequisito
                                AND status = 'APROVADO'
                            LIMIT 1;", connection))
                        {
                            cmd.Parameters.AddWithValue("@userId", userId);
                            cmd.Parameters.AddWithValue("@itemPrerequisito", itemCatalogo.ItemPrerequisito);

                            possuiPrerequisito = cmd.ExecuteScalar() != null;
                        }

                        if (!possuiPrerequisito)
                        {
                            RollbackTransacao(connection);
                            CPH.SetArgument("plataformaResultado", "PrerequisitoNaoCumprido");
                            CPH.SetArgument("plataformaItemPrerequisito", itemCatalogo.ItemPrerequisito);
                            return true;
                        }
                    }

                    // Valida o estoque global
                    if (itemCatalogo.EstoqueGlobal.HasValue && itemCatalogo.EstoqueGlobal.Value <= 0)
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaResultado", "EstoqueEsgotado");
                        return true;
                    }

                    // Resgates gratuitos não precisam movimentar moedas.
                    if (valorItem != 0 && !DebitarMoedas(connection, userId, valorItem))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaResultado", "SaldoInsuficiente");
                        return true;
                    }

                    // Reduz o estoque global quando limitado
                    if (itemCatalogo.EstoqueGlobal.HasValue)
                    {
                        int linhasEstoque;

                        using (var cmd = new SQLiteCommand(@"UPDATE YoutubePlataformaItens
                                SET estoqueGlobal = estoqueGlobal - 1
                            WHERE item = @item
                                AND estoqueGlobal IS NOT NULL
                                AND estoqueGlobal > 0;", connection))
                        {
                            cmd.Parameters.AddWithValue("@item", itemCatalogo.Item);
                            linhasEstoque = cmd.ExecuteNonQuery();
                        }

                        if (linhasEstoque != 1)
                        {
                            RollbackTransacao(connection);
                            CPH.SetArgument("plataformaResultado", "EstoqueEsgotado");
                            return true;
                        }
                    }

                    // Registra o resgate aprovado
                    long resgateId;

                    using (var cmd = new SQLiteCommand(@"INSERT INTO YoutubePlataformaResgates
                            (userId, userName, item, valorBase, percentualDesconto, nivelMembro, valorPago, status, origem, metadata, versaoRegra, timestamp)
                        VALUES
                            (@userId, @userName, @item, @valorBase, @percentualDesconto, @nivelMembro, @valorPago, 'APROVADO', @origem, @metadata, @versaoRegra, @timestamp);

                        SELECT last_insert_rowid();", connection))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@userName", userName);
                        cmd.Parameters.AddWithValue("@item", itemCatalogo.Item);
                        cmd.Parameters.AddWithValue("@valorBase", itemCatalogo.Valor);
                        cmd.Parameters.AddWithValue("@percentualDesconto", percentualDesconto);
                        cmd.Parameters.AddWithValue("@nivelMembro", (object)nivelMembro ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@valorPago", valorItem);
                        cmd.Parameters.AddWithValue("@origem", origem);
                        cmd.Parameters.AddWithValue("@metadata", string.IsNullOrEmpty(metadata) ? (object)DBNull.Value : metadata);
                        cmd.Parameters.AddWithValue("@versaoRegra", versaoRegra);
                        cmd.Parameters.AddWithValue("@timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                        resgateId = Convert.ToInt64(cmd.ExecuteScalar());
                    }

                    ConfirmarTransacao(connection);

                    int saldoRestante = saldoAtual - valorItem;

                    CPH.SetArgument("plataformaResultado", "Sucesso");
                    CPH.SetArgument("plataformaResgateId", resgateId);
                    CPH.SetArgument("plataformaSaldoRestante", saldoRestante);

                    CPH.LogInfo($">>> [GERENTE_DB] Resgate da Plataforma aprovado: " + $"Usuário '{userName}' ({userId}) | " + $"Item '{itemCatalogo.Item}' | " + $"Valor {valorItem:N0} | " + $"Resgate #{resgateId}.");
                }
                catch
                {
                    RollbackTransacao(connection);
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao processar resgate da Plataforma: " + ex.Message);
            CPH.SetArgument("plataformaResultado", "Erro");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Estorna um resgate aprovado, devolvendo o valorPago histórico e o estoque limitado
    // ------------------------------------------------------------------
    public bool EstornarResgatePlataforma()
    {
        try
        {
            CPH.TryGetArg("plataformaEstornoResgateId", out long resgateId);

            if (resgateId <= 0)
            {
                CPH.SetArgument("plataformaEstornoResultado", "ParametrosInvalidos");
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);

            using (var connection = AbrirConexao(ambiente))
            {
                IniciarTransacao(connection);

                try
                {
                    string userId = null;
                    string item = null;
                    int valorPago = 0;

                    using (var cmd = new SQLiteCommand(@"SELECT userId, item, valorPago
                            FROM YoutubePlataformaResgates
                        WHERE id = @id
                            AND status = 'APROVADO'
                        LIMIT 1;", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", resgateId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                userId = reader["userId"].ToString();
                                item = reader["item"].ToString();
                                valorPago = Convert.ToInt32(reader["valorPago"]);
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(userId))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaEstornoResultado", "ResgateNaoEncontradoOuJaEstornado");
                        return true;
                    }

                    // O estorno gratuito altera o resgate e o estoque, sem crédito de moedas.
                    if (valorPago != 0 && !CreditarMoedas(connection, userId, valorPago))
                    {
                        RollbackTransacao(connection);
                        CPH.SetArgument("plataformaEstornoResultado", "UsuarioNaoEncontrado");
                        return true;
                    }

                    using (var cmd = new SQLiteCommand(@"UPDATE YoutubePlataformaResgates
                            SET status = 'ESTORNADO'
                        WHERE id = @id
                            AND status = 'APROVADO';", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", resgateId);

                        if (cmd.ExecuteNonQuery() != 1)
                        {
                            RollbackTransacao(connection);
                            CPH.SetArgument("plataformaEstornoResultado", "FalhaAoEstornar");
                            return true;
                        }
                    }

                    // Devolve uma unidade ao estoque global, quando limitado
                    using (var cmd = new SQLiteCommand(@"UPDATE YoutubePlataformaItens
                            SET estoqueGlobal = estoqueGlobal + 1
                        WHERE item = @item
                            AND estoqueGlobal IS NOT NULL;", connection))
                    {
                        cmd.Parameters.AddWithValue("@item", item);
                        cmd.ExecuteNonQuery();
                    }

                    ConfirmarTransacao(connection);

                    CPH.SetArgument("plataformaEstornoResultado", "Sucesso");
                    CPH.SetArgument("plataformaEstornoValor", valorPago);

                    CPH.LogInfo($">>> [GERENTE_DB] Resgate da Plataforma #{resgateId} estornado. " + $"Valor devolvido: {valorPago:N0} moedas.");
                }
                catch
                {
                    RollbackTransacao(connection);
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao estornar resgate da Plataforma: " + ex.Message);
            CPH.SetArgument("plataformaEstornoResultado", "Erro");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Calcula o valor final de um item conforme o percentual já validado da regra da Plataforma
    // ------------------------------------------------------------------
    private int CalcularValorItemComDesconto(int valorBase, int percentualDesconto)
    {
        return (int)Math.Round(valorBase * (100m - percentualDesconto) / 100m, MidpointRounding.AwayFromZero);
    }

    // ------------------------------------------------------------------
    // Busca um item da Plataforma pelo identificador técnico
    // ------------------------------------------------------------------
    private PlataformaItem BuscarItemPlataforma(SQLiteConnection connection, string item)
    {
        using (var cmd = new SQLiteCommand(@"SELECT item,
                    nomeExibicao,
                    descricao,
                    categoria,
                    slot,
                    tier,
                    itemPrerequisito,
                    valor,
                    limiteMaximo,
                    estoqueGlobal,
                    ativo,
                    visivel
                FROM YoutubePlataformaItens
            WHERE item = @item
            LIMIT 1;", connection))
        {
            cmd.Parameters.AddWithValue("@item", item);

            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                    return null;

                return new PlataformaItem
                {
                    Item = reader["item"].ToString(),
                    NomeExibicao = reader["nomeExibicao"].ToString(),
                    Descricao = reader["descricao"].ToString(),
                    Categoria = reader["categoria"].ToString(),
                    Slot = reader["slot"] == DBNull.Value ? null : reader["slot"].ToString(),
                    Tier = Convert.ToInt32(reader["tier"]),
                    ItemPrerequisito = reader["itemPrerequisito"] == DBNull.Value ? null : reader["itemPrerequisito"].ToString(),
                    Valor = Convert.ToInt32(reader["valor"]),
                    LimiteMaximo = Convert.ToInt32(reader["limiteMaximo"]),
                    EstoqueGlobal = reader["estoqueGlobal"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["estoqueGlobal"]),
                    Ativo = Convert.ToInt32(reader["ativo"]) == 1,
                    Visivel = Convert.ToInt32(reader["visivel"]) == 1
                };
            }
        }
    }

    // ------------------------------------------------------------------
    // Representa um item cadastrado no catálogo da Plataforma
    // ------------------------------------------------------------------
    public class PlataformaItem
    {
        public string Item { get; set; }
        public string NomeExibicao { get; set; }
        public string Descricao { get; set; }
        public string Categoria { get; set; }
        public string Slot { get; set; }
        public int Tier { get; set; }
        public string ItemPrerequisito { get; set; }
        public int Valor { get; set; }
        public int LimiteMaximo { get; set; }
        public int? EstoqueGlobal { get; set; }
        public bool Ativo { get; set; }
        public bool Visivel { get; set; }
    }

    // ==================================================================
    // Usuários e moedas - auxiliares compartilhados entre os gerentes
    // ==================================================================

    // ------------------------------------------------------------------
    // Busca o userId de um usuário a partir do nome exibido
    // ------------------------------------------------------------------
    private string BuscarUserIdPorNome(SQLiteConnection connection, string userName)
    {
        using (var cmd = new SQLiteCommand("SELECT userId FROM YoutubeUsuariosMoeda WHERE userName = @userName COLLATE NOCASE ORDER BY CASE WHEN userId LIKE 'UC%' THEN 0 ELSE 1 END LIMIT 1", connection))
        {
            cmd.Parameters.AddWithValue("@userName", userName);
            var resultado = cmd.ExecuteScalar();
            return resultado?.ToString();
        }
    }

    // ------------------------------------------------------------------
    // Busca o nome exibido de um usuário a partir do userId
    // ------------------------------------------------------------------
    private string BuscarUserNamePorId(SQLiteConnection connection, string userId)
    {
        using (var cmd = new SQLiteCommand("SELECT userName FROM YoutubeUsuariosMoeda WHERE userId = @userId", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            return resultado?.ToString();
        }
    }

    // ------------------------------------------------------------------
    // Retorna o saldo atual de moedas de um usuário
    // ------------------------------------------------------------------
    private int ObterSaldoUsuario(SQLiteConnection connection, string userId)
    {
        using (var cmd = new SQLiteCommand("SELECT coinBalance FROM YoutubeUsuariosMoeda WHERE userId = @userId", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            return resultado != null && resultado != DBNull.Value ? Convert.ToInt32(resultado) : 0;
        }
    }

    // ------------------------------------------------------------------
    // Credita uma quantidade positiva de moedas a um usuário existente
    // ------------------------------------------------------------------
    private bool CreditarMoedas(SQLiteConnection connection, string userId, int quantidade)
    {
        if (string.IsNullOrEmpty(userId) || quantidade <= 0)
            return false;

        using (var cmd = new SQLiteCommand(@"UPDATE YoutubeUsuariosMoeda
                                                SET coinBalance = coinBalance + @quantidade
                                              WHERE userId = @userId;", connection))
        {
            cmd.Parameters.AddWithValue("@quantidade", quantidade);
            cmd.Parameters.AddWithValue("@userId", userId);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    // ------------------------------------------------------------------
    // Debita uma quantidade positiva com saldo suficiente e filtro opcional por canal
    // ------------------------------------------------------------------
    private bool DebitarMoedas(SQLiteConnection connection, string userId, int quantidade, string broadcastUserId = null)
    {
        if (string.IsNullOrEmpty(userId) || quantidade <= 0)
            return false;

        string sql = @"UPDATE YoutubeUsuariosMoeda
                          SET coinBalance = coinBalance - @quantidade
                        WHERE userId = @userId
                          AND coinBalance >= @quantidade";

        if (!string.IsNullOrEmpty(broadcastUserId))
            sql += " AND broadcastUserId = @broadcastUserId;";

        using (var cmd = new SQLiteCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@quantidade", quantidade);
            cmd.Parameters.AddWithValue("@userId", userId);
            if (!string.IsNullOrEmpty(broadcastUserId))
                cmd.Parameters.AddWithValue("@broadcastUserId", broadcastUserId);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    // ==================================================================
    // Conexão e transações - infraestrutura compartilhada
    // ==================================================================

    // ------------------------------------------------------------------
    // Abre a conexão SQLite e configura WAL, sincronização, timeout e chaves estrangeiras
    // ------------------------------------------------------------------
    private SQLiteConnection AbrirConexao(Ambiente ambiente)
    {
        var connection = new SQLiteConnection($"Data Source={ambiente.CaminhoBanco};Version=3;");
        connection.Open();
        using (var pragmaCmd = new SQLiteCommand(@"PRAGMA journal_mode=WAL;
              PRAGMA synchronous=NORMAL;
              PRAGMA busy_timeout=3000;
              PRAGMA foreign_keys=ON;", connection))
        {
            pragmaCmd.ExecuteNonQuery();
        }

        return connection;
    }

    // ------------------------------------------------------------------
    // Executa um comando SQL simples sem retorno, dentro da conexão informada
    // ------------------------------------------------------------------
    private void Executar(SQLiteConnection connection, string sql)
    {
        using (var cmd = new SQLiteCommand(sql, connection))
        {
            cmd.ExecuteNonQuery();
        }
    }

    // ------------------------------------------------------------------
    // Inicia uma transação SQLite com bloqueio imediato para operações de escrita
    // ------------------------------------------------------------------
    private void IniciarTransacao(SQLiteConnection connection)
    {
        Executar(connection, "BEGIN IMMEDIATE;");
    }

    // ------------------------------------------------------------------
    // Confirma a transação SQLite em andamento
    // ------------------------------------------------------------------
    private void ConfirmarTransacao(SQLiteConnection connection)
    {
        Executar(connection, "COMMIT;");
    }

    // ------------------------------------------------------------------
    // Desfaz a transação SQL em andamento
    // ------------------------------------------------------------------
    private void RollbackTransacao(SQLiteConnection connection)
    {
        using (var cmd = new SQLiteCommand("ROLLBACK;", connection))
        {
            cmd.ExecuteNonQuery();
        }
    }

    // ==================================================================
    // Ambiente
    // ==================================================================

    // ------------------------------------------------------------------
    // Resolve os caminhos de arquivo usados pelo script a partir da pasta raiz do Streamer.bot
    // ------------------------------------------------------------------
    public class Ambiente
    {
        public string PastaRaiz { get; set; } = "";

        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");

        // Construtor vazio necessário para desserialização do contexto.
        public Ambiente()
        {
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
        }
    }
}