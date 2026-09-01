using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Atualização 260901.1125
// Camada de dados da automação da live: Centraliza todo o acesso ao SQLite (YoutubeStream.db)
public class CPHInline
{
    private static readonly HashSet<string> TabelasPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "YoutubeComandosAudio",
        "YoutubeUsuariosMoeda",
        "YoutubeChatLog"
    };

    // ------------------------------------------------------------------
    // Cria as tabelas (idempotente) e aplica migrações incrementais de coluna
    // ------------------------------------------------------------------
    public bool GarantirSchema()
    {
        try
        {
            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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
                // Migrações incrementais
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
    // Resolve (ou cria) o grupoId de um comando de áudio a partir de uma lista de apelidos
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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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
    // Faz upsert genérico numa tabela permitida, a partir de colunas recebidas em JSON
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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);
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
            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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
    // Atualiza o timestamp de último uso de um grupo de áudio (controle de cooldown)
    // ------------------------------------------------------------------
    public bool AtualizarUltimoUsoAudio()
    {
        try
        {
            CPH.TryGetArg("atualizarUltimoUsoGrupoId", out int grupoId);
            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);
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
    // Consulta saldo, ranking e último crédito de um usuário, por id ou por nome
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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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
        try
        {
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
                CPH.SetArgument("adicionarResultado", "ParametrosInvalidos");
                return true;
            }

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.SetArgument("adicionarResultado", "BancoNaoEncontrado");
                return true;
            }

            using (var connection = AbrirConexao(ambiente))
            {
                using (var beginCmd = new SQLiteCommand("BEGIN IMMEDIATE;", connection))
                {
                    beginCmd.ExecuteNonQuery();
                }

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
                        cmd.ExecuteNonQuery();
                    }

                    using (var commitCmd = new SQLiteCommand("COMMIT;", connection))
                    {
                        commitCmd.ExecuteNonQuery();
                    }

                    CPH.SetArgument("adicionarResultado", "Sucesso");
                    CPH.SetArgument("adicionarDestinatarioNomeExibido", BuscarUserNamePorId(connection, destinatarioId) ?? userName);
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
            CPH.LogError(">>> [GERENTE_DB] ERRO ao adicionar moedas: " + ex.Message);
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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            using (var connection = AbrirConexao(ambiente))
            {
                using (var beginCmd = new SQLiteCommand("BEGIN IMMEDIATE;", connection))
                {
                    beginCmd.ExecuteNonQuery();
                }

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

                    using (var debitoCmd = new SQLiteCommand("UPDATE YoutubeUsuariosMoeda SET coinBalance = coinBalance - @quantidade WHERE userId = @userId;", connection))
                    {
                        debitoCmd.Parameters.AddWithValue("@quantidade", quantidade);
                        debitoCmd.Parameters.AddWithValue("@userId", remetenteUserId);
                        debitoCmd.ExecuteNonQuery();
                    }

                    using (var creditoCmd = new SQLiteCommand("UPDATE YoutubeUsuariosMoeda SET coinBalance = coinBalance + @quantidade WHERE userId = @userId;", connection))
                    {
                        creditoCmd.Parameters.AddWithValue("@quantidade", quantidade);
                        creditoCmd.Parameters.AddWithValue("@userId", destinatarioId);
                        creditoCmd.ExecuteNonQuery();
                    }

                    using (var commitCmd = new SQLiteCommand("COMMIT;", connection))
                    {
                        commitCmd.ExecuteNonQuery();
                    }

                    CPH.SetArgument("transferirResultado", "Sucesso");
                    CPH.SetArgument("transferirDestinatarioNomeExibido", BuscarUserNamePorId(connection, destinatarioId) ?? destinatarioNome);
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
    // Debita moedas de um usuário (usado pela importação de moedas da Twitch)
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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            using (var connection = AbrirConexao(ambiente))
            using (var cmd = new SQLiteCommand(@"UPDATE YoutubeUsuariosMoeda
                                                    SET coinBalance = coinBalance - @custo
                                                  WHERE userId = @userId
                                                    AND broadcastUserId = @broadcastUserId
                                                    AND coinBalance >= @custo;", connection))
            {
                cmd.Parameters.AddWithValue("@custo", custo);
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@broadcastUserId", broadcastUserId);

                int linhasAfetadas = cmd.ExecuteNonQuery();
                return linhasAfetadas > 0;
            }
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao debitar moedas: " + ex.Message);
            return false;
        }
    }

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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            using (var connection = AbrirConexao(ambiente))
            {
                using (var beginCmd = new SQLiteCommand("BEGIN IMMEDIATE;", connection))
                {
                    beginCmd.ExecuteNonQuery();
                }

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

                    using (var commitCmd = new SQLiteCommand("COMMIT;", connection))
                    {
                        commitCmd.ExecuteNonQuery();
                    }

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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            using (var connection = AbrirConexao(ambiente))
            {
                using (var beginCmd = new SQLiteCommand("BEGIN IMMEDIATE;", connection))
                {
                    beginCmd.ExecuteNonQuery();
                }

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

                        using (var commitCmd = new SQLiteCommand("COMMIT;", connection))
                        {
                            commitCmd.ExecuteNonQuery();
                        }

                        CPH.SetArgument("resolverPalpiteResultado", "SemGanhadores");
                        return true;
                    }

                    int potePerdedores = poteTotal - poteVencedores;

                    // Ordena por valor apostado (desc); em empate, mantém a ordem de chegada
                    // (já vem ASC por betAt da consulta), pra decidir quem recebe a sobra de arredondamento.
                    var vencedoresOrdenados = vencedores.OrderByDescending(v => v.BetAmount).ToList();

                    var pagamentos = new Dictionary<string, int>();
                    int totalDistribuido = 0;
                    foreach (var vencedor in vencedoresOrdenados)
                    {
                        int parteProporcional = poteVencedores > 0
                            ? (int)((long)vencedor.BetAmount * potePerdedores / poteVencedores)
                            : 0;
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

                        using (var creditoCmd = new SQLiteCommand("UPDATE YoutubeUsuariosMoeda SET coinBalance = coinBalance + @valor WHERE userId = @userId;", connection))
                        {
                            creditoCmd.Parameters.AddWithValue("@valor", valorPago);
                            creditoCmd.Parameters.AddWithValue("@userId", vencedor.UserId);
                            creditoCmd.ExecuteNonQuery();
                        }
                    }

                    using (var statusCmd = new SQLiteCommand("UPDATE YoutubePalpites SET status = 'resolved' WHERE id = @id;", connection))
                    {
                        statusCmd.Parameters.AddWithValue("@id", predictionId);
                        statusCmd.ExecuteNonQuery();
                    }

                    using (var commitCmd = new SQLiteCommand("COMMIT;", connection))
                    {
                        commitCmd.ExecuteNonQuery();
                    }

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
            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            using (var connection = AbrirConexao(ambiente))
            {
                using (var beginCmd = new SQLiteCommand("BEGIN IMMEDIATE;", connection))
                {
                    beginCmd.ExecuteNonQuery();
                }

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

                    using (var commitCmd = new SQLiteCommand("COMMIT;", connection))
                    {
                        commitCmd.ExecuteNonQuery();
                    }

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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            using (var connection = AbrirConexao(ambiente))
            {
                using (var beginCmd = new SQLiteCommand("BEGIN IMMEDIATE;", connection))
                {
                    beginCmd.ExecuteNonQuery();
                }

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

                    using (var debitoCmd = new SQLiteCommand("UPDATE YoutubeUsuariosMoeda SET coinBalance = coinBalance - @valor WHERE userId = @userId;", connection))
                    {
                        debitoCmd.Parameters.AddWithValue("@valor", valor);
                        debitoCmd.Parameters.AddWithValue("@userId", userId);
                        debitoCmd.ExecuteNonQuery();
                    }

                    using (var commitCmd = new SQLiteCommand("COMMIT;", connection))
                    {
                        commitCmd.ExecuteNonQuery();
                    }

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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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

    // ------------------------------------------------------------------
    // Consulta o progresso atual da meta de doações/pontos
    // ------------------------------------------------------------------
    public bool ObterProgressoMeta()
    {
        try
        {
            CPH.TryGetArg("metaBroadcastUserName", out string broadcastUserName);
            if (string.IsNullOrEmpty(broadcastUserName))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: broadcastUserName ausente para ObterProgressoMeta.");
                CPH.SetArgument("metaProgressoMensal", 0);
                return false;
            }

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.SetArgument("metaProgressoMensal", 0);
                return true;
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

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao obter progresso da meta: " + ex.Message);
            CPH.SetArgument("metaProgressoMensal", 0);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Verifica se uma doação já foi registrada antes, pra evitar duplicidade
    // ------------------------------------------------------------------
    public bool VerificarDoacaoDuplicada()
    {
        try
        {
            CPH.TryGetArg("doacaoDupUserId", out string userId);
            CPH.TryGetArg("doacaoDupBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("doacaoDupTipoAcao", out string tipoAcao);
            CPH.TryGetArg("doacaoDupTier", out string tier);

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.SetArgument("doacaoDuplicada", false);
                return true;
            }

            using (var connection = AbrirConexao(ambiente))
            {
                TimeSpan janelaDedup = TimeSpan.FromSeconds(15); // Janela de tempo do Evento
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
            CPH.SetArgument("doacaoDuplicada", false); // falha na checagem não deve bloquear uma doação real
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Registra uma doação (Super Chat, Membership, Tip, etc.) na tabela de doações
    // ------------------------------------------------------------------
    public bool SalvarDoacao()
    {
        try
        {
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

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

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
                    cmd.Parameters.AddWithValue("@timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@tier", (object)tier ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@broadcastId", (object)broadcastId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@messageId", (object)messageId ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DB] ERRO ao salvar doação: " + ex.Message);
            return false;
        }
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
    // Abre e configura (WAL, timeout) a conexão com o banco SQLite
    // ------------------------------------------------------------------
    private SQLiteConnection AbrirConexao(Ambiente ambiente)
    {
        var connection = new SQLiteConnection($"Data Source={ambiente.CaminhoBanco};Version=3;");
        connection.Open();
        using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;", connection))
        {
            pragmaCmd.ExecuteNonQuery();
        }

        return connection;
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
            using (var creditoCmd = new SQLiteCommand("UPDATE YoutubeUsuariosMoeda SET coinBalance = coinBalance + @valor WHERE userId = @userId;", connection))
            {
                creditoCmd.Parameters.AddWithValue("@valor", reembolso.Value);
                creditoCmd.Parameters.AddWithValue("@userId", reembolso.Key);
                creditoCmd.ExecuteNonQuery();
            }
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
    // Guarda um apostador vencedor enquanto o pagamento proporcional é calculado
    // ------------------------------------------------------------------
    private class ApostaVencedora
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public int BetAmount { get; set; }
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

    // ------------------------------------------------------------------
    // Representa uma posição no ranking do comando !topmoedas
    // ------------------------------------------------------------------
    public class TopMoedaItem
    {
        public int Rank { get; set; }
        public string NomeExibido { get; set; }
        public int Moedas { get; set; }
    }

    // ------------------------------------------------------------------
    // Resolve os caminhos de arquivo usados pelo script a partir da pasta raiz do Streamer.bot
    // ------------------------------------------------------------------
    public class Ambiente
    {
        public string PastaRaiz { get; set; }
        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");
    }
}