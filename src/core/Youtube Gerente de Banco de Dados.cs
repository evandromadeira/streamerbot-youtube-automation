using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Atualização 261024.1245
// Padrões aplicados: 5
// Revisão concluída: 1
public class CPHInline
{
    // Camada de dados da automação da live: centraliza todo o acesso ao SQLite (YoutubeStream.db)
    private const int CotaDiariaResgatesPlataforma = 3;
    private const int VersaoInicialRegraPlataforma = 1;
    private const int MaximoTentativasSalvarChatLog = 3;
    private const int JanelaDuplicidadeDoacaoSegundos = 15;
    private const int IntervaloTentativasSalvarChatLogMs = 150;
    private const int IntervaloPadraoVerificacaoPalpiteSegundos = 3;
    private const int LimiteConsultaResgatesMinecraft = 20;

    private const decimal PercentualIntegral = 100m;

    private const string Prefixo = "[GERENTE_BD] ";

    // Tabelas aceitas pelo cadastro de registros do Gerente de Áudio.
    private static readonly HashSet<string> TabelasPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "YoutubeComandosAudio",
        "YoutubeUsuariosMoeda",
        "YoutubeChatLog"
    };

    public bool GarantirSchema()
    {
        // Youtube Tarefas ao Iniciar a Live: cria as tabelas completas e os índices do banco.
        try
        {
            Ambiente ambiente = new Ambiente(CPH);
            if (string.IsNullOrEmpty(ambiente.PastaRaiz))
            {
                LogError("ERRO: Variável 'caminhoPastaStreamerBot' não encontrada!");
                return false;
            }

            if (!Directory.Exists(ambiente.PastaStream))
            {
                Directory.CreateDirectory(ambiente.PastaStream);
            }

            using (var connection = AbrirConexao(ambiente))
            {
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
                    timestamp TEXT NOT NULL,
                    tier TEXT,
                    broadcastId TEXT,
                    messageId TEXT,
                    processamentoId TEXT UNIQUE);");

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
                    publishedAt TEXT,
                    broadcastId TEXT);");

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

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubePlataformaItens (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    item TEXT NOT NULL UNIQUE,
                    nomeExibicao TEXT NOT NULL,
                    descricao TEXT NOT NULL,
                    categoria TEXT NOT NULL,
                    slot TEXT,
                    tier INTEGER NOT NULL DEFAULT 0,
                    itemPrerequisitoId INTEGER,
                    valor INTEGER NOT NULL,
                    limiteMaximo INTEGER NOT NULL,
                    estoqueGlobal INTEGER,
                    ativo INTEGER NOT NULL DEFAULT 1,
                    visivel INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY (itemPrerequisitoId) REFERENCES YoutubePlataformaItens(id));");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubePlataformaResgates (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    userId TEXT NOT NULL,
                    userName TEXT NOT NULL,
                    itemId INTEGER NOT NULL,
                    valorBase INTEGER NOT NULL,
                    percentualDesconto INTEGER NOT NULL DEFAULT 0,
                    nivelMembro TEXT,
                    valorPago INTEGER NOT NULL,
                    status TEXT NOT NULL DEFAULT 'APROVADO' CHECK (status IN ('APROVADO', 'ESTORNADO')),
                    origem TEXT NOT NULL DEFAULT 'CHAT' CHECK (origem IN ('CHAT', 'OVERLAY', 'ADMIN')),
                    metadata TEXT,
                    versaoRegra INTEGER NOT NULL DEFAULT 1,
                    timestamp TEXT NOT NULL,
                    FOREIGN KEY (itemId) REFERENCES YoutubePlataformaItens(id));");

                Executar(connection, @"CREATE INDEX IF NOT EXISTS idx_resgates_usuario_item_status ON YoutubePlataformaResgates (userId, itemId, status);");
                Executar(connection, @"CREATE INDEX IF NOT EXISTS idx_resgates_usuario_data_status ON YoutubePlataformaResgates (userId, timestamp, status);");
                Executar(connection, @"CREATE INDEX IF NOT EXISTS idx_resgates_item_status ON YoutubePlataformaResgates (itemId, status);");

                Executar(connection, @"CREATE TABLE IF NOT EXISTS YoutubeMinecraftResgates (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    operacaoId TEXT NOT NULL UNIQUE,
                    userId TEXT NOT NULL,
                    userName TEXT NOT NULL,
                    regraId TEXT NOT NULL,
                    mundo TEXT NOT NULL,
                    origem TEXT NOT NULL CHECK(origem IN ('moedas', 'livePixPayPal', 'superChatSticker')),
                    valorPago INTEGER,
                    valorBRL REAL,
                    doacaoId INTEGER,
                    status TEXT NOT NULL DEFAULT 'DEBITADO' CHECK (status IN ('DEBITADO', 'PENDENTE', 'EXECUTADO', 'PARCIAL', 'FALHOU', 'INCERTO')),
                    mensagemResultado TEXT NOT NULL,
                    estornado INTEGER NOT NULL DEFAULT 0 CHECK(estornado IN (0,1)),
                    timestamp TEXT NOT NULL,
                    atualizadoEm TEXT NOT NULL,
                    broadcastUserId TEXT NOT NULL,
                    broadcastUserName TEXT NOT NULL, CHECK ((origem = 'moedas' AND valorPago IS NOT NULL AND valorPago > 0 AND valorBRL IS NULL)
                                                        OR (origem IN ('livePixPayPal', 'superChatSticker') AND valorPago IS NULL AND valorBRL IS NOT NULL AND valorBRL > 0)),
                    CHECK (origem = 'moedas' OR estornado = 0));");

                Executar(connection, @"CREATE INDEX IF NOT EXISTS idx_minecraft_resgates_usuario_data ON YoutubeMinecraftResgates (userId, broadcastUserId, timestamp);");
                Executar(connection, @"CREATE INDEX IF NOT EXISTS idx_minecraft_resgates_status ON YoutubeMinecraftResgates (status, estornado);");

                LogInfo("Schema verificado/atualizado com sucesso.");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao garantir schema: " + ex.Message);
            return false;
        }
    }

    public bool SalvarChatLog()
    {
        // Youtube Gerente de Chat: salva uma mensagem recebida na tabela de log.
        try
        {
            CPH.TryGetArg("chatLogUserId", out string userId);
            CPH.TryGetArg("chatLogUserName", out string userName);
            CPH.TryGetArg("chatLogMessageId", out string messageId);
            CPH.TryGetArg("chatLogMessage", out string message);
            CPH.TryGetArg("chatLogBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("chatLogBroadcastUserName", out string broadcastUserName);
            CPH.TryGetArg("chatLogBroadcastId", out string broadcastId);
            CPH.TryGetArg("chatLogIsSubscribed", out bool isSubscribed);
            CPH.TryGetArg("chatLogIsSponsor", out bool isSponsor);
            CPH.TryGetArg("chatLogIsModerator", out bool isModerator);
            CPH.TryGetArg("chatLogUserPreviousActive", out string userPreviousActive);
            CPH.TryGetArg("chatLogPublishedAt", out string publishedAt);
            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente))
            {
                string insertSql = @"INSERT INTO YoutubeChatLog
                                    (userId, userName, messageId, message, broadcastUserId, broadcastUserName, isSubscribed, isSponsor, isModerator, userPreviousActive, publishedAt, broadcastId)
                                    VALUES
                                    (@userId, @userName, @messageId, @message, @broadcastUserId, @broadcastUserName, @isSubscribed, @isSponsor, @isModerator, @userPreviousActive, @publishedAt, @broadcastId);";
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
                    cmd.Parameters.AddWithValue("@broadcastId", string.IsNullOrWhiteSpace(broadcastId) ? (object)DBNull.Value : broadcastId);
                    // Retry simples em caso de "database is locked" mesmo com WAL (picos de concorrência)
                    int tentativas = 0;
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
                            if (tentativas >= MaximoTentativasSalvarChatLog)
                            {
                                throw;
                            }

                            LogWarn($"Banco ocupado ao salvar ChatLog, tentativa {tentativas}/{MaximoTentativasSalvarChatLog}...");
                            System.Threading.Thread.Sleep(IntervaloTentativasSalvarChatLogMs * tentativas);
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO ao salvar ChatLog: " + ex.Message);
            return false;
        }
    }

    public bool ObterOuCriarGrupoIdAudio()
    {
        // Consulta o grupo de um apelido existente ou calcula o próximo grupoId para cadastro
        try
        {
            CPH.TryGetArg("grupoAudioAliasesJson", out string aliasesJson);
            var aliases = JsonConvert.DeserializeObject<List<string>>(aliasesJson ?? "[]");
            if (aliases == null || aliases.Count == 0)
            {
                LogError("ERRO: nenhum apelido informado para resolver grupoId.");
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
                    {
                        grupoIdExistente = Convert.ToInt32(resultado);
                    }
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
            LogError("ERRO ao obter grupoId: " + ex.Message);
            return false;
        }
    }

    public bool SalvarRegistro()
    {
        // Insere ou atualiza um registro por JSON; usado no cadastro dos apelidos de áudio
        try
        {
            CPH.TryGetArg("salvarTabela", out string tabela);
            CPH.TryGetArg("salvarColunasJson", out string colunasJson);
            CPH.TryGetArg("salvarChaveConflito", out string chaveConflito);
            CPH.TryGetArg("salvarColunasSomenteInsercaoJson", out string somenteInsercaoJson);
            if (string.IsNullOrEmpty(tabela) || string.IsNullOrEmpty(colunasJson) || string.IsNullOrEmpty(chaveConflito))
            {
                LogError("ERRO: parâmetros insuficientes para SalvarRegistro.");
                return false;
            }

            if (!TabelasPermitidas.Contains(tabela))
            {
                LogError($"ERRO: tabela '{tabela}' não está na lista de tabelas permitidas para SalvarRegistro.");
                return false;
            }

            var colunas = JsonConvert.DeserializeObject<Dictionary<string, object>>(colunasJson);
            if (colunas == null || colunas.Count == 0 || !colunas.ContainsKey(chaveConflito))
            {
                LogError("ERRO: colunas inválidas ou chave de conflito ausente entre as colunas.");
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
                {
                    listaUpdate = $"{chaveConflito} = {chaveConflito}";
                }

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
            LogError("ERRO ao executar SalvarRegistro: " + ex.Message);
            return false;
        }
    }

    public bool BuscarAudioPorComando()
    {
        // Busca um comando de áudio ativo pelo nome digitado no chat
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
            LogError("ERRO ao buscar áudio por comando: " + ex.Message);
            CPH.SetArgument("audioEncontrado", false);
            return false;
        }
    }

    public bool ListarAudiosPorGrupo()
    {
        // Lista um áudio representante de cada grupo ativo, pro comando !audios
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
                        {
                            continue;
                        }

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
            LogError("ERRO ao listar áudios: " + ex.Message);
            return false;
        }
    }

    public bool AtualizarUltimoUsoAudio()
    {
        // Atualiza o timestamp de último uso de um grupo de áudio (controle de cooldown)
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
            LogError("ERRO ao atualizar último uso do áudio: " + ex.Message);
            return false;
        }
    }

    public bool DebitarMoedasUsuario()
    {
        try
        {
            CPH.TryGetArg("debitarUserId", out string userId);
            CPH.TryGetArg("debitarBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("debitarCusto", out int custo);
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(broadcastUserId))
            {
                LogError("ERRO: userId/broadcastUserId ausente para DebitarMoedasUsuario.");
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente))
                return DebitarMoedas(connection, userId, custo, broadcastUserId);
        }
        catch (Exception ex)
        {
            LogError("ERRO ao debitar moedas: " + ex.Message);
            return false;
        }
    }

    public bool VerificarBancoMinecraft()
    {
        // Registro e cobrança dos resgates Minecraft
        CPH.SetArgument("minecraftDbResultado", "INDISPONIVEL");
        try
        {
            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente, true))
            {
                using (var cmd = new SQLiteCommand("SELECT id,operacaoId,userId,userName,broadcastUserId,broadcastUserName,regraId,mundo,origem,valorPago,valorBRL,doacaoId,status,mensagemResultado,estornado,timestamp,atualizadoEm FROM YoutubeMinecraftResgates LIMIT 0;", connection))
                    cmd.ExecuteNonQuery();
                CPH.TryGetArg("origem", out string origem);
                if (origem == "moedas")
                {
                    using (var cmd = new SQLiteCommand("SELECT userId,userName,broadcastUserId,coinBalance FROM YoutubeUsuariosMoeda LIMIT 0;", connection))
                        cmd.ExecuteNonQuery();
                }
            }

            CPH.SetArgument("minecraftDbResultado", "DISPONIVEL");
            return true;
        }
        catch (Exception ex)
        {
            LogError("Banco Minecraft indisponivel; confira GarantirSchema: " + ex.Message);
            return false;
        }
    }

    public bool RegistrarContribuicaoMinecraft()
    {
        // Registra contribuições sem debitar moedas e correlaciona a confirmação de cada tentativa.
        bool confirmouTransacao = false;

        string id = "";
        string chamadaId = "";
        string resultado = "INCERTO";
        string estadoRetorno = "";
        string regraRetorno = "";

        try
        {
            CPH.SetArgument("minecraftDbResultado", "INCERTO");
            CPH.SetArgument("minecraftRegistroRetorno", "");
            CPH.TryGetArg("doacaoValorBRL", out double valor);
            CPH.TryGetArg("minecraftDoacaoId", out long doacaoId);
            CPH.TryGetArg("minecraftOperacaoId", out id);
            CPH.TryGetArg("origem", out string origem);
            CPH.TryGetArg("doacaoUserId", out string usuario);
            CPH.TryGetArg("minecraftUserName", out string nomeUsuario);
            CPH.TryGetArg("doacaoBroadcastUserId", out string canal);
            CPH.TryGetArg("minecraftBroadcastUserName", out string nomeCanal);
            CPH.TryGetArg("minecraftRegraId", out string regra);
            CPH.TryGetArg("mundoEsperado", out string mundo);
            CPH.TryGetArg("minecraftDbOperacao", out string operacao);
            CPH.TryGetArg("minecraftStatusRegistro", out string status);
            CPH.TryGetArg("minecraftMensagemResultado", out string mensagemResultado);
            CPH.TryGetArg("minecraftEnvioId", out string envioId);
            CPH.TryGetArg("minecraftRegistroChamadaId", out chamadaId);
            if (!Guid.TryParseExact(id, "D", out Guid uuid) || (origem != "livePixPayPal" && origem != "superChatSticker") || string.IsNullOrWhiteSpace(mundo) || valor <= 0 || double.IsNaN(valor) || double.IsInfinity(valor))
            {
                return false;
            }

            bool consultar = operacao == "CONSULTAR";
            bool prepararEnvio = operacao == "PREPARAR_ENVIO";
            bool recuperarResultado = operacao == "RECUPERAR_RESULTADO";
            bool contratoCorrelacionado = consultar || prepararEnvio || recuperarResultado;
            if (operacao != "INICIAR" && operacao != "ATUALIZAR" && !contratoCorrelacionado)
            {
                return false;
            }

            if (contratoCorrelacionado && (id != uuid.ToString("D") || !Guid.TryParseExact(chamadaId, "D", out Guid chamadaUuid) || chamadaId != chamadaUuid.ToString("D") || !Guid.TryParseExact(envioId, "D", out Guid envioUuid) || envioId != envioUuid.ToString("D") || doacaoId < 0))
            {
                return false;
            }

            if ((operacao == "ATUALIZAR" || recuperarResultado) && status != "PENDENTE" && status != "EXECUTADO" && status != "PARCIAL" && status != "FALHOU" && status != "INCERTO")
            {
                return false;
            }

            string marcadorEnvio = prepararEnvio ? JsonConvert.SerializeObject(new { envioId = envioId }) : "";

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente, true))
            {
                IniciarTransacao(connection);
                try
                {
                    bool conflitante = false;
                    bool gravar = true;

                    string anterior = null;
                    string regraAnterior = "";
                    string mensagemAnterior = "";
                    using (var consulta = new SQLiteCommand("SELECT * FROM YoutubeMinecraftResgates WHERE operacaoId=@id;", connection))
                    {
                        consulta.Parameters.AddWithValue("@id", id);
                        using (var reader = consulta.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                anterior = Convert.ToString(reader["status"]);
                                regraAnterior = Convert.ToString(reader["regraId"]);
                                mensagemAnterior = Convert.ToString(reader["mensagemResultado"]);
                                conflitante = Convert.ToString(reader["origem"]) != origem || Convert.ToString(reader["mundo"]) != mundo || Convert.ToString(reader["userId"]) != (usuario ?? "") || Convert.ToString(reader["broadcastUserId"]) != (canal ?? "") || reader["valorBRL"] == DBNull.Value || Convert.ToDouble(reader["valorBRL"]) != valor;
                                if (contratoCorrelacionado)
                                {
                                    long doacaoAnterior = reader["doacaoId"] == DBNull.Value ? 0 : Convert.ToInt64(reader["doacaoId"]);
                                    conflitante = conflitante || Convert.ToString(reader["userName"]) != (nomeUsuario ?? "") || Convert.ToString(reader["broadcastUserName"]) != (nomeCanal ?? "") || doacaoAnterior != doacaoId;
                                }
                            }
                        }
                    }

                    resultado = "SALVO";
                    estadoRetorno = anterior ?? "INCERTO";
                    regraRetorno = regraAnterior;
                    if (conflitante)
                    {
                        resultado = "CONFLITO";
                        gravar = false;
                    }
                    else if (consultar)
                    {
                        // A consulta não cria nem atualiza registros e nunca autoriza operações existentes.
                        resultado = anterior == null ? "AUSENTE" : "EXISTENTE";
                        gravar = false;
                    }
                    else if ((operacao == "INICIAR" || prepararEnvio) && anterior != null)
                    {
                        // Só a marca da mesma tentativa de envio identifica uma confirmação inicial perdida.
                        resultado = prepararEnvio && anterior == "INCERTO" && regraAnterior.Length == 0 && mensagemAnterior == marcadorEnvio ? "SALVO" : "EXISTENTE";
                        gravar = false;
                    }
                    else if (operacao == "ATUALIZAR" || recuperarResultado)
                    {
                        if (anterior == null && !recuperarResultado)
                        {
                            throw new InvalidOperationException("Registro ausente; a atualização exige uma contribuição já registrada.");
                        }

                        bool marcaDeOutroEnvio = recuperarResultado && anterior == "INCERTO" && regraAnterior.Length == 0 && mensagemAnterior.StartsWith("{\"envioId\":", StringComparison.Ordinal) && mensagemAnterior != JsonConvert.SerializeObject(new { envioId = envioId });
                        bool terminalAnterior = anterior == "EXECUTADO" || anterior == "PARCIAL" || anterior == "FALHOU";
                        bool terminalSolicitado = status == "EXECUTADO" || status == "PARCIAL" || status == "FALHOU";
                        if (marcaDeOutroEnvio || (regraAnterior.Length > 0 && !string.IsNullOrEmpty(regra) && regraAnterior != regra) || (terminalAnterior && terminalSolicitado && anterior != status))
                        {
                            resultado = "CONFLITO";
                            gravar = false;
                        }
                        else if (terminalAnterior || (anterior == "PENDENTE" && status == "INCERTO"))
                        {
                            // Uma repetição não altera nenhum campo de um resultado mais conclusivo.
                            resultado = recuperarResultado ? "PRESERVADO" : "SALVO";
                            gravar = false;
                        }
                        else if (string.IsNullOrEmpty(regra))
                        {
                            regra = regraAnterior;
                        }
                    }

                    if (gravar)
                    {
                        bool inserir = anterior == null;

                        string estado = operacao == "INICIAR" || prepararEnvio ? "INCERTO" : status;
                        string mensagem = prepararEnvio ? marcadorEnvio : mensagemResultado ?? "";
                        if (prepararEnvio)
                        {
                            regra = "";
                        }

                        string sql = inserir ? @"INSERT INTO YoutubeMinecraftResgates
                            (operacaoId,userId,userName,broadcastUserId,broadcastUserName,regraId,mundo,origem,valorPago,valorBRL,doacaoId,status,mensagemResultado,timestamp,atualizadoEm)
                            VALUES (@id,@usuario,@nomeUsuario,@canal,@nomeCanal,@regra,@mundo,@origem,NULL,@valor,@doacao,@status,@mensagem,@agora,@agora);" : "UPDATE YoutubeMinecraftResgates SET status=@status,regraId=@regra,mensagemResultado=@mensagem,atualizadoEm=@agora WHERE operacaoId=@id;";
                        using (var cmd = new SQLiteCommand(sql, connection))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.Parameters.AddWithValue("@usuario", usuario ?? "");
                            cmd.Parameters.AddWithValue("@nomeUsuario", nomeUsuario ?? "");
                            cmd.Parameters.AddWithValue("@canal", canal ?? "");
                            cmd.Parameters.AddWithValue("@nomeCanal", nomeCanal ?? "");
                            cmd.Parameters.AddWithValue("@regra", regra ?? "");
                            cmd.Parameters.AddWithValue("@mundo", mundo);
                            cmd.Parameters.AddWithValue("@origem", origem);
                            cmd.Parameters.AddWithValue("@valor", valor);
                            cmd.Parameters.AddWithValue("@doacao", doacaoId > 0 ? (object)doacaoId : DBNull.Value);
                            cmd.Parameters.AddWithValue("@status", estado);
                            cmd.Parameters.AddWithValue("@mensagem", mensagem);
                            cmd.Parameters.AddWithValue("@agora", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            if (cmd.ExecuteNonQuery() != 1)
                            {
                                throw new InvalidOperationException("Registro de contribuição não confirmado.");
                            }
                        }

                        estadoRetorno = estado;
                        regraRetorno = regra ?? "";
                    }

                    ConfirmarTransacao(connection);
                    confirmouTransacao = true;
                }
                catch
                {
                    try
                    {
                        RollbackTransacao(connection);
                    }
                    catch (Exception rollbackEx)
                    {
                        LogError("Falha ao encerrar a transação da contribuição Minecraft " + id + ": " + rollbackEx.Message);
                    }
                    throw;
                }
            }

            // O retorno fica fora da transação: perder a confirmação não desfaz uma gravação concluída.
            CPH.SetArgument("minecraftDbResultado", resultado);
            CPH.SetArgument("minecraftRegistroRetorno", JsonConvert.SerializeObject(new { OperacaoId = id, ChamadaId = chamadaId, Resultado = resultado, Status = estadoRetorno, RegraId = regraRetorno }));
            return resultado != "CONFLITO";
        }
        catch (Exception ex)
        {
            LogError("Falha ao registrar contribuição Minecraft: " + JsonConvert.SerializeObject(new { OperacaoId = id, ChamadaId = chamadaId, TransacaoConfirmada = confirmouTransacao, Resultado = resultado, Status = estadoRetorno, RegraId = regraRetorno, Erro = ex.ToString() }));
            return false;
        }
    }

    public bool ListarResgatesMinecraftPendentes()
    {
        // Consulta recibos pendentes em lotes; nao confirma reservas nem repete efeitos ou cobrancas.
        CPH.SetArgument("minecraftDbResultado", "INCERTO");
        CPH.SetArgument("minecraftPendentesJson", "");
        try
        {
            CPH.TryGetArg("minecraftConsultaMundo", out string mundo);
            CPH.TryGetArg("minecraftConsultaAposId", out long aposId);
            if (string.IsNullOrWhiteSpace(mundo) || aposId < 0)
            {
                return false;
            }

            var pendentes = new List<object>();

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente, true))
            using (var cmd = new SQLiteCommand(@"SELECT id,operacaoId FROM YoutubeMinecraftResgates
                                                WHERE mundo=@mundo AND status IN ('PENDENTE','DEBITADO','INCERTO')
                                                  AND estornado=0 AND id>@aposId
                                                ORDER BY id LIMIT @limite;", connection))
            {
                cmd.Parameters.AddWithValue("@mundo", mundo);
                cmd.Parameters.AddWithValue("@aposId", aposId);
                cmd.Parameters.AddWithValue("@limite", LimiteConsultaResgatesMinecraft);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        pendentes.Add(new
                        {
                            id = Convert.ToInt64(reader["id"]),
                            operacaoId = Convert.ToString(reader["operacaoId"])
                        });
                    }
                }
            }

            CPH.SetArgument("minecraftPendentesJson", JsonConvert.SerializeObject(pendentes));
            CPH.SetArgument("minecraftDbResultado", "CONSULTADO");
            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO ao listar resgates Minecraft pendentes: " + ex.ToString());
            return false;
        }
    }

    public bool ConsultarResgateMinecraft()
    {
        CPH.SetArgument("minecraftDbResultado", "INCERTO");
        try
        {
            CPH.TryGetArg("minecraftOperacaoId", out string id);
            Guid uuid;
            if (!Guid.TryParseExact(id, "D", out uuid))
            {
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente, true))
            using (var cmd = new SQLiteCommand("SELECT * FROM YoutubeMinecraftResgates WHERE operacaoId=@id;", connection))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return false;
                    }

                    CPH.SetArgument("origem", Convert.ToString(reader["origem"]));
                    CPH.SetArgument("doacaoUserId", Convert.ToString(reader["userId"]));
                    CPH.SetArgument("doacaoBroadcastUserId", Convert.ToString(reader["broadcastUserId"]));
                    CPH.SetArgument("minecraftUserId", Convert.ToString(reader["userId"]));
                    CPH.SetArgument("minecraftUserName", Convert.ToString(reader["userName"]));
                    CPH.SetArgument("minecraftCanalId", Convert.ToString(reader["broadcastUserId"]));
                    CPH.SetArgument("minecraftBroadcastUserName", Convert.ToString(reader["broadcastUserName"]));
                    CPH.SetArgument("minecraftMensagemResultado", Convert.ToString(reader["mensagemResultado"]));
                    CPH.SetArgument("minecraftDoacaoId", reader["doacaoId"] == DBNull.Value ? 0L : Convert.ToInt64(reader["doacaoId"]));
                    CPH.SetArgument("minecraftRegraId", Convert.ToString(reader["regraId"]));
                    CPH.SetArgument("mundoEsperado", Convert.ToString(reader["mundo"]));
                    CPH.SetArgument("minecraftCusto", reader["valorPago"] == DBNull.Value ? 0 : Convert.ToInt32(reader["valorPago"]));
                    CPH.SetArgument("doacaoValorBRL", reader["valorBRL"] == DBNull.Value ? 0d : Convert.ToDouble(reader["valorBRL"]));
                    CPH.SetArgument("minecraftDbResultado", Convert.ToInt32(reader["estornado"]) == 1 ? "ESTORNADO" : Convert.ToString(reader["status"]));
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO ao consultar resgate Minecraft: " + ex.ToString());
            return false;
        }
    }

    public bool ProcessarResgateMinecraft()
    {
        CPH.SetArgument("minecraftDbResultado", "INCERTO");
        CPH.SetArgument("minecraftLogUsuarioEncontrado", false);
        CPH.SetArgument("minecraftLogUserId", "");
        CPH.SetArgument("minecraftLogUserName", "");
        CPH.SetArgument("minecraftLogCoinBalance", 0L);
        CPH.SetArgument("minecraftLogResgateId", 0L);
        bool debitoPossivel = true;
        bool registroConsultado = false;
        bool registroExistia = true;
        try
        {
            CPH.TryGetArg("minecraftOperacaoId", out string id);
            CPH.TryGetArg("minecraftUserId", out string userId);
            CPH.TryGetArg("minecraftUserName", out string nomeUsuario);
            CPH.TryGetArg("minecraftCanalId", out string canalId);
            CPH.TryGetArg("minecraftBroadcastUserName", out string nomeCanal);
            CPH.TryGetArg("minecraftRegraId", out string regraId);
            CPH.TryGetArg("mundoEsperado", out string mundo);
            CPH.TryGetArg("minecraftCusto", out int custo);
            CPH.TryGetArg("minecraftDbOperacao", out string operacao);
            CPH.TryGetArg("minecraftIncluirDadosLog", out bool incluirDadosLog);
            CPH.TryGetArg("minecraftMensagemResultado", out string mensagemResultado);
            Guid uuid;
            if (!Guid.TryParseExact(id, "D", out uuid) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(canalId) || string.IsNullOrWhiteSpace(regraId) || string.IsNullOrWhiteSpace(mundo) || custo <= 0)
            {
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente, true))
            {
                IniciarTransacao(connection);
                try
                {
                    int estornado = 0;

                    long resgateId = 0;

                    string estado = null;
                    using (var cmd = new SQLiteCommand("SELECT * FROM YoutubeMinecraftResgates WHERE operacaoId=@id;", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                debitoPossivel = true;
                                if (Convert.ToString(reader["origem"]) != "moedas" || Convert.ToString(reader["userId"]) != userId || Convert.ToString(reader["broadcastUserId"]) != canalId || Convert.ToString(reader["regraId"]) != regraId || Convert.ToString(reader["mundo"]) != mundo || Convert.ToInt32(reader["valorPago"]) != custo)
                                {
                                    throw new InvalidOperationException("Operacao Minecraft com parametros conflitantes.");
                                }

                                estado = Convert.ToString(reader["status"]);
                                estornado = Convert.ToInt32(reader["estornado"]);
                                resgateId = Convert.ToInt64(reader["id"]);
                            }
                        }
                    }

                    registroConsultado = true;
                    registroExistia = estado != null;
                    debitoPossivel = registroExistia;
                    string agora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    if (operacao == "DEBITAR" && estado == null)
                    {
                        debitoPossivel = true;
                        if (!DebitarMoedas(connection, userId, custo, canalId))
                        {
                            if (incluirDadosLog)
                            {
                                PrepararLogResgateMinecraft(connection, userId, canalId, resgateId);
                            }

                            RollbackTransacao(connection);
                            CPH.SetArgument("minecraftDbResultado", "SALDO_INSUFICIENTE");
                            return true;
                        }

                        using (var cmd = new SQLiteCommand(@"INSERT INTO YoutubeMinecraftResgates
                            (operacaoId,userId,userName,broadcastUserId,broadcastUserName,regraId,mundo,origem,valorPago,valorBRL,doacaoId,status,mensagemResultado,timestamp,atualizadoEm)
                            VALUES (@id,@usuario,@nomeUsuario,@canal,@nomeCanal,@regra,@mundo,'moedas',@custo,NULL,NULL,'DEBITADO',@mensagem,@agora,@agora);", connection))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.Parameters.AddWithValue("@usuario", userId);
                            cmd.Parameters.AddWithValue("@nomeUsuario", nomeUsuario ?? "");
                            cmd.Parameters.AddWithValue("@canal", canalId);
                            cmd.Parameters.AddWithValue("@nomeCanal", nomeCanal ?? "");
                            cmd.Parameters.AddWithValue("@regra", regraId);
                            cmd.Parameters.AddWithValue("@mundo", mundo);
                            cmd.Parameters.AddWithValue("@custo", custo);
                            cmd.Parameters.AddWithValue("@mensagem", mensagemResultado ?? "");
                            cmd.Parameters.AddWithValue("@agora", agora);
                            cmd.ExecuteNonQuery();
                        }

                        estado = "DEBITADO";
                    }
                    else if (operacao != "DEBITAR")
                    {
                        if (estado == null || (operacao != "EXECUTADO" && operacao != "PENDENTE" && operacao != "INCERTO" && operacao != "PARCIAL" && operacao != "FALHOU"))
                        {
                            throw new InvalidOperationException("Resultado de resgate invalido.");
                        }

                        // Preservar resultados terminais; FALHOU exige recibo confirmado pelo consumidor.
                        if (estornado == 0 && estado != "EXECUTADO" && estado != "PARCIAL")
                        {
                            if (operacao == "FALHOU")
                            {
                                if (!CreditarMoedas(connection, userId, custo))
                                {
                                    throw new InvalidOperationException("Estorno nao confirmado.");
                                }

                                estornado = 1;
                            }

                            estado = operacao;
                            using (var cmd = new SQLiteCommand("UPDATE YoutubeMinecraftResgates SET status=@estado,estornado=@estornado,mensagemResultado=@mensagem,atualizadoEm=@agora WHERE operacaoId=@id;", connection))
                            {
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.Parameters.AddWithValue("@estado", estado);
                                cmd.Parameters.AddWithValue("@estornado", estornado);
                                cmd.Parameters.AddWithValue("@mensagem", mensagemResultado ?? "");
                                cmd.Parameters.AddWithValue("@agora", agora);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    if (incluirDadosLog && operacao == "EXECUTADO" && estado == "EXECUTADO" && estornado == 0)
                    {
                        PrepararLogResgateMinecraft(connection, userId, canalId, resgateId);
                    }

                    ConfirmarTransacao(connection);
                    CPH.SetArgument("minecraftDbResultado", estornado == 1 ? "ESTORNADO" : estado);
                    return true;
                }
                catch
                {
                    RollbackTransacao(connection);
                    if (registroConsultado && !registroExistia)
                    {
                        debitoPossivel = false;
                    }

                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            if (!debitoPossivel)
            {
                CPH.SetArgument("minecraftDbResultado", "SEM_DEBITO");
            }

            LogError("ERRO no resgate Minecraft: " + ex.ToString());
            return false;
        }
    }

    public bool SaldoMoedasUsuario()
    {
        // Youtube Gerente de Moedas e importação: consulta saldo e ranking de um usuário por ID ou nome.
        try
        {
            CPH.TryGetArg("consultarChave", out string chave);
            CPH.TryGetArg("consultarPorId", out bool consultarPorId);
            if (string.IsNullOrEmpty(chave))
            {
                LogError("ERRO: chave ausente para SaldoMoedasUsuario.");
                CPH.SetArgument("consultarEncontrado", false);
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);
            if (!File.Exists(ambiente.CaminhoBanco))
            {
                LogError("ERRO: banco de dados não encontrado para SaldoMoedasUsuario.");
                CPH.SetArgument("consultarEncontrado", false);
                return false;
            }

            using (var connection = AbrirConexao(ambiente))
            {
                int? moedasUsuario = null;

                string selectSql = consultarPorId ? "SELECT userName, coinBalance, lastCoinAt FROM YoutubeUsuariosMoeda WHERE userId = @chave" : "SELECT userName, coinBalance, lastCoinAt FROM YoutubeUsuariosMoeda WHERE userName = @chave COLLATE NOCASE";
                string nomeExibido = null;
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
            LogError("ERRO ao consultar moedas: " + ex.Message);
            CPH.SetArgument("consultarEncontrado", false);
            return false;
        }
    }

    public bool ConsultarTopMoedas()
    {
        // Monta o ranking dos N maiores saldos de moeda, pro comando !topmoedas
        try
        {
            CPH.TryGetArg("topMoedasQuantidade", out int quantidade);
            if (quantidade <= 0)
            {
                LogError("ERRO: quantidade inválida para ConsultarTopMoedas.");
                CPH.SetArgument("topMoedasResultadoJson", "[]");
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);
            if (!File.Exists(ambiente.CaminhoBanco))
            {
                LogError("ERRO: banco de dados não encontrado para ConsultarTopMoedas.");
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
                            itens.Add(new TopMoedaItem { Rank = rank, NomeExibido = reader["userName"].ToString(), Moedas = saldo });
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
            LogError("ERRO ao consultar top de moedas: " + ex.Message);
            CPH.SetArgument("topMoedasResultadoJson", "[]");
            return false;
        }
    }

    public bool AdicionarMoedasUsuario()
    {
        // Credita moedas a um usuário, com regra de cooldown para atividade de chat
        CPH.TryGetArg("adicionarOperacaoId", out string operacaoId);
        CPH.SetArgument("adicionarBancoInicioId", operacaoId);
        CPH.SetArgument("adicionarBancoRespostaId", "");
        CPH.SetArgument("adicionarBancoConcluido", false);
        bool commitIniciado = false;
        bool commitConfirmado = false;

        string creditoStatus = "falhou";
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
                LogError("ERRO: parâmetros inválidos para AdicionarMoedasUsuario.");
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
                    // Resolve pelo nome quando o userId está vazio ou não corresponde a um registro existente.
                    string destinatarioId = userId;
                    if (!string.IsNullOrEmpty(destinatarioId) && !UserIdExiste(connection, destinatarioId) && !string.IsNullOrEmpty(userName))
                    {
                        string idPeloNome = BuscarUserIdPorNome(connection, userName);
                        if (!string.IsNullOrEmpty(idPeloNome))
                        {
                            LogInfo($"userId '{destinatarioId}' não encontrado, resolvido via nome '{userName}' -> '{idPeloNome}'.");
                            destinatarioId = idPeloNome;
                        }
                    }
                    else if (string.IsNullOrEmpty(destinatarioId) && !string.IsNullOrEmpty(userName))
                    {
                        destinatarioId = BuscarUserIdPorNome(connection, userName);
                        if (string.IsNullOrEmpty(destinatarioId))
                        {
                            destinatarioId = userName;
                            LogWarn($"Usuário '{userName}' não encontrado por userId — criando/creditando conta identificada pelo nome.");
                        }
                    }

                    CPH.SetArgument("adicionarDestinatarioId", destinatarioId);
                    // Confere na mesma transação o cooldown positivo informado para a origem "chat_atividade".
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

                    // Só a atividade de chat atualiza lastCoinAt; os demais créditos preservam esse cooldown.
                    string sql = ehAtividadeChat ? @"INSERT INTO YoutubeUsuariosMoeda (userId, userName, coinBalance, lastCoinAt, broadcastUserId, broadcastUserName)
                                    VALUES (@userId, @userName, @quantidadeMoedas, @agora, @broadcastUserId, @broadcastUserName)
                                    ON CONFLICT(userId) DO UPDATE SET
                                    coinBalance = coinBalance + excluded.coinBalance,
                                    lastCoinAt = excluded.lastCoinAt,
                                    userName = excluded.userName,
                                    broadcastUserId = COALESCE(excluded.broadcastUserId, broadcastUserId),
                                    broadcastUserName = COALESCE(excluded.broadcastUserName, broadcastUserName);" : @"INSERT INTO YoutubeUsuariosMoeda (userId, userName, coinBalance, lastCoinAt, broadcastUserId, broadcastUserName)
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
                        {
                            cmd.Parameters.AddWithValue("@agora", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        }

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
                            if (!commitIniciado)
                            {
                                creditoStatus = "falhou";
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            creditoStatus = "incerto";
                            LogError("ERRO ao desfazer crédito de moedas: " + rollbackEx.ToString());
                        }
                    }

                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO ao adicionar moedas: " + ex.ToString());
            CPH.SetArgument("adicionarCreditoStatus", creditoStatus);
            CPH.SetArgument("adicionarErro", ex.ToString());
            CPH.SetArgument("adicionarResultado", "Erro");
            return false;
        }
        finally
        {
            CPH.SetArgument("adicionarBancoRespostaId", operacaoId);
            CPH.SetArgument("adicionarBancoConcluido", true);
        }
    }

    public bool TransferirMoedasUsuario()
    {
        // Transfere moedas de um usuário para outro, validando saldo do remetente
        try
        {
            CPH.TryGetArg("transferirRemetenteUserId", out string remetenteUserId);
            CPH.TryGetArg("transferirDestinatarioNome", out string destinatarioNome);
            CPH.TryGetArg("transferirQuantidade", out int quantidade);
            if (string.IsNullOrEmpty(remetenteUserId) || string.IsNullOrEmpty(destinatarioNome) || quantidade <= 0)
            {
                LogError("ERRO: parâmetros inválidos para TransferirMoedasUsuario.");
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
            LogError("ERRO ao transferir moedas: " + ex.Message);
            CPH.SetArgument("transferirResultado", "Erro");
            return false;
        }
    }

    public bool CriarPalpite()
    {
        // Youtube Gerente de Palpite: abre uma rodada, bloqueando se já existir uma em aberto.
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
                LogError("ERRO: parâmetros inválidos para CriarPalpite.");
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
            LogError("ERRO ao criar palpite: " + ex.Message);
            CPH.SetArgument("criarPalpiteResultado", "Erro");
            return false;
        }
    }

    public bool ApostarPalpite()
    {
        // Registra (ou soma) a aposta de um usuário na rodada de palpite aberta
        try
        {
            CPH.TryGetArg("apostarPalpiteUserId", out string userId);
            CPH.TryGetArg("apostarPalpiteUserName", out string userName);
            CPH.TryGetArg("apostarPalpiteOption", out string option);
            CPH.TryGetArg("apostarPalpiteValor", out int valor);
            CPH.TryGetArg("apostarPalpiteBroadcastUserId", out string broadcastUserId);
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(option) || valor <= 0)
            {
                LogError("ERRO: parâmetros inválidos para ApostarPalpite.");
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
                            {
                                chosenOptionExistente = reader["chosenOption"].ToString();
                            }
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
            LogError("ERRO ao registrar aposta de palpite: " + ex.Message);
            CPH.SetArgument("apostarPalpiteResultado", "Erro");
            return false;
        }
    }

    public bool VerificarEncerramentoPalpite()
    {
        // Verifica se algum palpite aberto acabou de passar do prazo, pra avisar no chat
        try
        {
            CPH.TryGetArg("verificarEncerramentoIntervaloSegundos", out int intervaloSegundos);
            if (intervaloSegundos <= 0)
            {
                intervaloSegundos = IntervaloPadraoVerificacaoPalpiteSegundos;
            }

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente))
            {
                int predictionId = 0;

                string agora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string janelaInicio = DateTime.Now.AddSeconds(-intervaloSegundos).ToString("yyyy-MM-dd HH:mm:ss");
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
            LogError("ERRO ao verificar encerramento de palpite: " + ex.Message);
            CPH.SetArgument("palpiteEncerradoEncontrado", false);
            return false;
        }
    }

    public bool ResolverPalpite()
    {
        // Declara a opção vencedora do palpite aberto e paga os vencedores pelo pool proporcional
        try
        {
            CPH.TryGetArg("resolverPalpiteOpcaoVencedora", out string opcaoVencedora);
            if (string.IsNullOrEmpty(opcaoVencedora))
            {
                LogError("ERRO: parâmetros inválidos para ResolverPalpite.");
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
                                int betAmount = Convert.ToInt32(reader["betAmount"]);

                                string userId = reader["userId"].ToString();
                                string userName = reader["userName"].ToString();
                                string chosenOption = reader["chosenOption"].ToString();
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
                    // Ordena pelo valor apostado e, no empate, pelo betAt atualizado na última aposta; o primeiro recebe a sobra.
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
            LogError("ERRO ao resolver palpite: " + ex.Message);
            CPH.SetArgument("resolverPalpiteResultado", "Erro");
            return false;
        }
    }

    public bool CancelarPalpite()
    {
        // Cancela o palpite aberto e devolve o valor apostado a todos os participantes
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
                        {
                            predictionId = Convert.ToInt32(resultado);
                        }
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
            LogError("ERRO ao cancelar palpite: " + ex.Message);
            CPH.SetArgument("cancelarPalpiteResultado", "Erro");
            return false;
        }
    }

    public bool VerificarDoacaoDuplicada()
    {
        // Youtube Gerente de Doações e Youtube Consultar Meta: procura duplicidade por usuário, canal, tipo e tier na janela configurada.
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
                TimeSpan janelaDedup = TimeSpan.FromSeconds(JanelaDuplicidadeDoacaoSegundos); // Janela usada na consulta de duplicidade
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
            LogError("ERRO ao checar doação duplicada: " + ex.Message);
            CPH.SetArgument("doacaoDuplicidadeErro", ex.Message);
            CPH.SetArgument("doacaoDuplicada", false); // O retorno false sinaliza ao chamador que a consulta falhou
            return false;
        }
    }

    public bool SalvarDoacao()
    {
        // Registra a doação ou retorna o mesmo processamento já salvo, sem repetir pontos nem alterar valores.
        bool commitIniciado = false;
        bool commitConfirmado = false;

        long registroId = 0;

        string registroStatus = "falhou";
        try
        {
            CPH.SetArgument("doacaoRegistroStatus", registroStatus);
            CPH.SetArgument("doacaoRegistroErro", "");
            CPH.SetArgument("doacaoRegistroId", registroId);
            CPH.TryGetArg("doacaoProcessamentoId", out string processamentoId);
            if (!Guid.TryParse(processamentoId, out Guid identificador))
            {
                throw new ArgumentException("doacaoProcessamentoId deve conter o GUID original da operação.");
            }

            processamentoId = identificador.ToString("D");
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
            using (var connection = AbrirConexao(ambiente, exigirExistente: true))
            {
                IniciarTransacao(connection);
                try
                {
                    string insertSql = @"INSERT INTO YoutubeDoacoes
                        (processamentoId, userId, userName, tipoAcao, valorOriginal, moedaOrigem, valorBRL, pontosMeta, moedaGanha, multiplicador, broadcastUserId, broadcastUserName, timestamp, tier, broadcastId, messageId)
                        VALUES (@processamentoId, @userId, @userName, @tipoAcao, @valorOriginal, @moedaOrigem, @valorBRL, @pontosMeta, @moedaGanha, @multiplicador, @broadcastUserId, @broadcastUserName, @timestamp, @tier, @broadcastId, @messageId)
                        ON CONFLICT(processamentoId) DO NOTHING;";
                    using (var cmd = new SQLiteCommand(insertSql, connection))
                    {
                        cmd.Parameters.AddWithValue("@processamentoId", processamentoId);
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
                        cmd.Parameters.AddWithValue("@messageId", string.IsNullOrWhiteSpace(messageId) ? DBNull.Value : (object)messageId);
                        registroStatus = "incerto";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = @"SELECT id FROM YoutubeDoacoes
                            WHERE processamentoId = @processamentoId
                            AND userId IS @userId
                            AND userName IS @userName
                            AND tipoAcao IS @tipoAcao
                            AND valorOriginal IS @valorOriginal
                            AND moedaOrigem IS @moedaOrigem
                            AND valorBRL IS @valorBRL
                            AND pontosMeta IS @pontosMeta
                            AND moedaGanha IS @moedaGanha
                            AND multiplicador IS @multiplicador
                            AND broadcastUserId IS @broadcastUserId
                            AND broadcastUserName IS @broadcastUserName
                            AND timestamp IS @timestamp
                            AND tier IS @tier
                            AND broadcastId IS @broadcastId
                            AND messageId IS @messageId;";
                        object id = cmd.ExecuteScalar();
                        if (id == null || id == DBNull.Value)
                        {
                            throw new InvalidOperationException("ProcessamentoId já utilizado com dados diferentes. O registro existente não foi alterado.");
                        }

                        registroId = Convert.ToInt64(id);
                    }

                    commitIniciado = true;
                    ConfirmarTransacao(connection);
                    commitConfirmado = true;
                    registroStatus = "salvo";
                }
                catch
                {
                    if (!commitConfirmado)
                    {
                        try
                        {
                            RollbackTransacao(connection);
                            if (!commitIniciado)
                            {
                                registroStatus = "falhou";
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            registroStatus = "incerto";
                            LogError("ERRO ao desfazer registro da doação: " + rollbackEx.Message);
                        }
                    }

                    throw;
                }
            }

            CPH.SetArgument("doacaoRegistroId", registroId);
            CPH.SetArgument("doacaoRegistroStatus", registroStatus);
            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO ao salvar doação: " + ex.Message);
            CPH.SetArgument("doacaoRegistroStatus", registroStatus);
            CPH.SetArgument("doacaoRegistroErro", ex.Message);
            return false;
        }
    }

    public bool ObterProgressoMeta()
    {
        // Consulta o progresso atual da meta de doações/pontos
        try
        {
            CPH.SetArgument("metaConsultaSucesso", false);
            CPH.TryGetArg("metaBroadcastUserName", out string broadcastUserName);
            if (string.IsNullOrEmpty(broadcastUserName))
            {
                LogError("ERRO: broadcastUserName ausente para ObterProgressoMeta.");
                CPH.SetArgument("metaProgressoMensal", 0);
                return false;
            }

            Ambiente ambiente = new Ambiente(CPH);
            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.SetArgument("metaProgressoMensal", 0);
                LogError("ERRO ao consultar meta: banco de dados não encontrado em " + ambiente.CaminhoBanco);
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
            LogError("ERRO ao obter progresso da meta: " + ex.Message);
            CPH.SetArgument("metaProgressoMensal", 0);
            return false;
        }
    }

    public bool AtualizarPerfilUsuario()
    {
        // Youtube Gerente de Perfil: atualiza um campo permitido do perfil de um usuário.
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
            LogError("ERRO ao atualizar perfil do usuário: " + ex.Message);
            return false;
        }
    }

    public bool RemoverNivelMembroUsuario()
    {
        // Remove o nível cadastrado quando o evento de resgate informa que o usuário não é membro
        try
        {
            CPH.TryGetArg("plataformaRemoverNivelUserId", out string userId);
            if (string.IsNullOrEmpty(userId))
            {
                LogError("ERRO: userId ausente para RemoverNivelMembroUsuario.");
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
            LogError("ERRO ao remover nível de membro: " + ex.Message);
            return false;
        }
    }

    public bool ConsultarNivelMembroUsuario()
    {
        // Consulta o nível de membro de um usuário para as regras da Plataforma
        try
        {
            CPH.TryGetArg("plataformaConsultarNivelUserId", out string userId);
            CPH.SetArgument("plataformaNivelMembro", "");
            if (string.IsNullOrEmpty(userId))
            {
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);
            using (var connection = AbrirConexao(ambiente))
            using (var cmd = new SQLiteCommand("SELECT nivelMembro FROM YoutubeUsuariosPerfil WHERE userId = @userId LIMIT 1", connection))
            {
                cmd.Parameters.AddWithValue("@userId", userId);
                var resultado = cmd.ExecuteScalar();
                if (resultado != null && resultado != DBNull.Value)
                {
                    CPH.SetArgument("plataformaNivelMembro", resultado.ToString());
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO ao consultar nível de membro: " + ex.Message);
            CPH.SetArgument("plataformaNivelMembro", "");
            return false;
        }
    }

    public bool ConsultarItemPlataforma()
    {
        // Consulta um item visível do catálogo da Plataforma
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
            LogError("ERRO ao consultar item da Plataforma: " + ex.Message);
            CPH.SetArgument("plataformaItemEncontrado", false);
            return false;
        }
    }

    public bool ResgatarItemPlataforma()
    {
        // Valida saldo, cotas, pré-requisito e estoque e registra o resgate na mesma transação
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
                LogError("ERRO: parâmetros inválidos para ResgatarItemPlataforma.");
                CPH.SetArgument("plataformaResultado", "ParametrosInvalidos");
                return true;
            }

            if (string.IsNullOrEmpty(origem))
            {
                origem = "CHAT";
            }

            origem = origem.ToUpperInvariant();
            if (origem != "CHAT" && origem != "OVERLAY" && origem != "ADMIN")
            {
                LogError($"ERRO: origem inválida para resgate da Plataforma: '{origem}'.");
                CPH.SetArgument("plataformaResultado", "OrigemInvalida");
                return true;
            }

            if (versaoRegra <= 0)
            {
                versaoRegra = VersaoInicialRegraPlataforma;
            }

            // Usa o mesmo percentual validado no cálculo e no registro do resgate.
            if (percentualDesconto < 0 || percentualDesconto > PercentualIntegral)
            {
                LogWarn($"Percentual de desconto inválido: {percentualDesconto}. Usando 0%.");
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
                            AND itemId = @itemId
                            AND status = 'APROVADO';", connection))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@itemId", itemCatalogo.Id);
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
                    if (itemCatalogo.ItemPrerequisitoId.HasValue)
                    {
                        bool possuiPrerequisito;
                        using (var cmd = new SQLiteCommand(@"SELECT 1
                                FROM YoutubePlataformaResgates
                            WHERE userId = @userId
                                AND itemId = @itemPrerequisitoId
                                AND status = 'APROVADO'
                            LIMIT 1;", connection))
                        {
                            cmd.Parameters.AddWithValue("@userId", userId);
                            cmd.Parameters.AddWithValue("@itemPrerequisitoId", itemCatalogo.ItemPrerequisitoId.Value);
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
                            WHERE id = @itemId
                                AND estoqueGlobal IS NOT NULL
                                AND estoqueGlobal > 0;", connection))
                        {
                            cmd.Parameters.AddWithValue("@itemId", itemCatalogo.Id);
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
                            (userId, userName, itemId, valorBase, percentualDesconto, nivelMembro, valorPago, status, origem, metadata, versaoRegra, timestamp)
                        VALUES
                            (@userId, @userName, @itemId, @valorBase, @percentualDesconto, @nivelMembro, @valorPago, 'APROVADO', @origem, @metadata, @versaoRegra, @timestamp);

                        SELECT last_insert_rowid();", connection))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@userName", userName);
                        cmd.Parameters.AddWithValue("@itemId", itemCatalogo.Id);
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
                    LogInfo($"Resgate da Plataforma aprovado: " + $"Usuário '{userName}' ({userId}) | " + $"Item '{itemCatalogo.Item}' | " + $"Valor {valorItem:N0} | " + $"Resgate #{resgateId}.");
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
            LogError("ERRO ao processar resgate da Plataforma: " + ex.Message);
            CPH.SetArgument("plataformaResultado", "Erro");
            return false;
        }
    }

    public bool EstornarResgatePlataforma()
    {
        // Estorna um resgate aprovado, devolvendo o valorPago histórico e o estoque limitado
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
                    int valorPago = 0;

                    long itemId = 0;

                    string userId = null;
                    using (var cmd = new SQLiteCommand(@"SELECT userId, itemId, valorPago
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
                                itemId = Convert.ToInt64(reader["itemId"]);
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
                        WHERE id = @itemId
                            AND estoqueGlobal IS NOT NULL;", connection))
                    {
                        cmd.Parameters.AddWithValue("@itemId", itemId);
                        cmd.ExecuteNonQuery();
                    }

                    ConfirmarTransacao(connection);
                    CPH.SetArgument("plataformaEstornoResultado", "Sucesso");
                    CPH.SetArgument("plataformaEstornoValor", valorPago);
                    LogInfo($"Resgate da Plataforma #{resgateId} estornado. " + $"Valor devolvido: {valorPago:N0} moedas.");
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
            LogError("ERRO ao estornar resgate da Plataforma: " + ex.Message);
            CPH.SetArgument("plataformaEstornoResultado", "Erro");
            return false;
        }
    }

    public bool ObterDiasDePresenca()
    {
        // Youtube Gerente de Estatísticas: conta os dias distintos em que um usuário enviou mensagens no chat.
        return ObterDiasDePresencaComFiltro(null);
    }

    public bool ObterDiasDePresencaNoMes()
    {
        // Conta em quantos dias diferentes um usuário mandou mensagem no chat, só no mês atual (hora local)
        var inicioMes = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        return ObterDiasDePresencaComFiltro(inicioMes);
    }

    private bool ObterDiasDePresencaComFiltro(DateTime? desde)
    {
        // Implementação compartilhada da consulta de dias de presença, com filtro de data opcional
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
            LogError("ERRO ao obter dias de presença: " + ex.Message);
            return false;
        }
    }

    private void DevolverApostas(SQLiteConnection connection, int predictionId)
    {
        // Devolve o valor apostado a todos os participantes de uma rodada de palpite
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
            {
                throw new InvalidOperationException("Usuário não encontrado para devolução de moedas.");
            }
        }
    }

    private int CalcularValorItemComDesconto(int valorBase, int percentualDesconto)
    {
        // Calcula o valor final de um item conforme o percentual já validado da regra da Plataforma
        return (int)Math.Round(valorBase * (PercentualIntegral - percentualDesconto) / PercentualIntegral, MidpointRounding.AwayFromZero);
    }

    private bool CreditarMoedas(SQLiteConnection connection, string userId, int quantidade)
    {
        // Credita uma quantidade positiva de moedas a um usuário existente
        if (string.IsNullOrEmpty(userId) || quantidade <= 0)
        {
            return false;
        }

        using (var cmd = new SQLiteCommand(@"UPDATE YoutubeUsuariosMoeda
                                                SET coinBalance = coinBalance + @quantidade
                                              WHERE userId = @userId;", connection))
        {
            cmd.Parameters.AddWithValue("@quantidade", quantidade);
            cmd.Parameters.AddWithValue("@userId", userId);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    private bool DebitarMoedas(SQLiteConnection connection, string userId, int quantidade, string broadcastUserId = null)
    {
        // Debita uma quantidade positiva com saldo suficiente e filtro opcional por canal
        if (string.IsNullOrEmpty(userId) || quantidade <= 0)
        {
            return false;
        }

        string sql = @"UPDATE YoutubeUsuariosMoeda
                          SET coinBalance = coinBalance - @quantidade
                        WHERE userId = @userId
                          AND coinBalance >= @quantidade";
        if (!string.IsNullOrEmpty(broadcastUserId))
        {
            sql += " AND broadcastUserId = @broadcastUserId;";
        }

        using (var cmd = new SQLiteCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@quantidade", quantidade);
            cmd.Parameters.AddWithValue("@userId", userId);
            if (!string.IsNullOrEmpty(broadcastUserId))
            {
                cmd.Parameters.AddWithValue("@broadcastUserId", broadcastUserId);
            }

            return cmd.ExecuteNonQuery() == 1;
        }
    }

    private SQLiteConnection AbrirConexao(Ambiente ambiente, bool exigirExistente = false)
    {
        // Infraestrutura compartilhada: abre a conexão SQLite e configura WAL, sincronização, timeout e chaves estrangeiras.
        if (exigirExistente && (string.IsNullOrWhiteSpace(ambiente.PastaRaiz) || !File.Exists(ambiente.CaminhoBanco)))
        {
            throw new InvalidOperationException("Banco configurado ausente; execute a configuracao antes de resgatar.");
        }

        var connection = new SQLiteConnection($"Data Source={ambiente.CaminhoBanco};Version=3;FailIfMissing={exigirExistente};");
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

    private void Executar(SQLiteConnection connection, string sql)
    {
        // Executa um comando SQL simples sem retorno, dentro da conexão informada
        using (var cmd = new SQLiteCommand(sql, connection))
        {
            cmd.ExecuteNonQuery();
        }
    }

    private void IniciarTransacao(SQLiteConnection connection)
    {
        // Inicia uma transação SQLite com bloqueio imediato para operações de escrita
        Executar(connection, "BEGIN IMMEDIATE;");
    }

    private void ConfirmarTransacao(SQLiteConnection connection)
    {
        // Confirma a transação SQLite em andamento
        Executar(connection, "COMMIT;");
    }

    private void RollbackTransacao(SQLiteConnection connection)
    {
        // Desfaz a transação SQL em andamento
        using (var cmd = new SQLiteCommand("ROLLBACK;", connection))
        {
            cmd.ExecuteNonQuery();
        }
    }

    private void LogError(string mensagem)
    {
        CPH.LogError(Prefixo + mensagem);
    }

    private void LogWarn(string mensagem)
    {
        CPH.LogWarn(Prefixo + mensagem);
    }

    private void LogInfo(string mensagem)
    {
        CPH.LogInfo(Prefixo + mensagem);
    }

    private void PrepararLogResgateMinecraft(SQLiteConnection connection, string userId, string canalId, long resgateId)
    {
        // Consulta o saldo na mesma transação do resgate para compor o log no consumidor.
        using (var cmd = new SQLiteCommand(@"SELECT userId, userName, coinBalance
                                               FROM YoutubeUsuariosMoeda
                                              WHERE userId=@userId
                                                AND broadcastUserId=@canalId;", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@canalId", canalId);
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return;
                }

                CPH.SetArgument("minecraftLogUsuarioEncontrado", true);
                CPH.SetArgument("minecraftLogUserId", Convert.ToString(reader["userId"]));
                CPH.SetArgument("minecraftLogUserName", Convert.ToString(reader["userName"]));
                CPH.SetArgument("minecraftLogCoinBalance", Convert.ToInt64(reader["coinBalance"]));
                CPH.SetArgument("minecraftLogResgateId", resgateId);
            }
        }
    }

    private bool UserIdExiste(SQLiteConnection connection, string userId)
    {
        // Verifica se um userId já existe na tabela de moedas
        using (var cmd = new SQLiteCommand("SELECT 1 FROM YoutubeUsuariosMoeda WHERE userId = @userId LIMIT 1", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            return cmd.ExecuteScalar() != null;
        }
    }

    private DateTime? BuscarUltimoCreditoAtividade(SQLiteConnection connection, string userId)
    {
        // Busca a data do último crédito por atividade de chat de um usuário
        using (var cmd = new SQLiteCommand("SELECT lastCoinAt FROM YoutubeUsuariosMoeda WHERE userId = @userId", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            if (resultado == null || resultado == DBNull.Value)
            {
                return null;
            }

            return DateTime.Parse(resultado.ToString());
        }
    }

    private string BuscarUserIdPorNome(SQLiteConnection connection, string userName)
    {
        // Auxiliar compartilhado: busca o userId de um usuário a partir do nome exibido.
        using (var cmd = new SQLiteCommand("SELECT userId FROM YoutubeUsuariosMoeda WHERE userName = @userName COLLATE NOCASE ORDER BY CASE WHEN userId LIKE 'UC%' THEN 0 ELSE 1 END LIMIT 1", connection))
        {
            cmd.Parameters.AddWithValue("@userName", userName);
            var resultado = cmd.ExecuteScalar();
            return resultado?.ToString();
        }
    }

    private string BuscarUserNamePorId(SQLiteConnection connection, string userId)
    {
        // Busca o nome exibido de um usuário a partir do userId
        using (var cmd = new SQLiteCommand("SELECT userName FROM YoutubeUsuariosMoeda WHERE userId = @userId", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            return resultado?.ToString();
        }
    }

    private int ObterSaldoUsuario(SQLiteConnection connection, string userId)
    {
        // Retorna o saldo atual de moedas de um usuário
        using (var cmd = new SQLiteCommand("SELECT coinBalance FROM YoutubeUsuariosMoeda WHERE userId = @userId", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            return resultado != null && resultado != DBNull.Value ? Convert.ToInt32(resultado) : 0;
        }
    }

    private int ContarDiasPresenca(SQLiteConnection connection, string userId, DateTime? desde)
    {
        // Conta dias distintos com mensagem no YoutubeChatLog, opcionalmente a partir de uma data
        string sql = "SELECT COUNT(DISTINCT DATE(publishedAt)) FROM YoutubeChatLog WHERE userId = @userId";
        if (desde.HasValue)
        {
            sql += " AND publishedAt >= @desde";
        }

        using (var cmd = new SQLiteCommand(sql, connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            if (desde.HasValue)
            {
                cmd.Parameters.AddWithValue("@desde", desde.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            var resultado = cmd.ExecuteScalar();
            return resultado != null && resultado != DBNull.Value ? Convert.ToInt32(resultado) : 0;
        }
    }

    private PlataformaItem BuscarItemPlataforma(SQLiteConnection connection, string item)
    {
        // Busca um item da Plataforma pelo identificador técnico
        using (var cmd = new SQLiteCommand(@"SELECT id, item,
                    nomeExibicao,
                    descricao,
                    categoria,
                    slot,
                    tier,
                    itemPrerequisitoId,
                    (SELECT p.item FROM YoutubePlataformaItens p WHERE p.id = YoutubePlataformaItens.itemPrerequisitoId) AS itemPrerequisito,
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
                {
                    return null;
                }

                return new PlataformaItem
                {
                    Id = Convert.ToInt64(reader["id"]),
                    ItemPrerequisitoId = reader["itemPrerequisitoId"] == DBNull.Value ? (long?)null : Convert.ToInt64(reader["itemPrerequisitoId"]),
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

    // Representa um grupo de áudio resumido pra listagem do comando !audios
    public class AudioResumo
    {
        public int GrupoId { get; set; }
        public int Custo { get; set; }

        public string Comando { get; set; }
    }

    // Representa uma posição no ranking do comando !topmoedas
    public class TopMoedaItem
    {
        public int Rank { get; set; }
        public int Moedas { get; set; }

        public string NomeExibido { get; set; }
    }

    // Guarda um apostador vencedor enquanto o pagamento proporcional é calculado
    private class ApostaVencedora
    {
        public int BetAmount { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }
    }

    // Representa um item cadastrado no catálogo da Plataforma
    public class PlataformaItem
    {
        public bool Ativo { get; set; }
        public bool Visivel { get; set; }

        public int Tier { get; set; }
        public int Valor { get; set; }
        public int LimiteMaximo { get; set; }

        public int? EstoqueGlobal { get; set; }

        public long Id { get; set; }

        public long? ItemPrerequisitoId { get; set; }

        public string Item { get; set; }
        public string NomeExibicao { get; set; }
        public string Descricao { get; set; }
        public string Categoria { get; set; }
        public string Slot { get; set; }
        // Nome técnico atual do pré-requisito, somente para exibição no chat.
        public string ItemPrerequisito { get; set; }
    }

    // Resolve os caminhos usados pelo script a partir da pasta raiz do Streamer.bot.
    public class Ambiente
    {
        public string PastaRaiz { get; set; } = "";
        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");

        public Ambiente()
        {
            // Construtor vazio necessário para desserialização do contexto.
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
        }
    }
}