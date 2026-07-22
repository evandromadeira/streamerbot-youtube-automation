using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Atualização 260722.1120
public class CPHInline
{
    private static readonly HashSet<string> TabelasPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "YoutubeComandosAudio"
        // Adicionar aqui só tabelas de configuração simples (sem regra de negócio condicional)
    };
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
                // ------------------------------------------------------------------
                // Migrações incrementais — adicione uma linha aqui quando precisar
                // de uma coluna nova. Seguro rodar múltiplas vezes.
                // ------------------------------------------------------------------
                // Exemplo de uso futuro:
                // AdicionarColunaSeNaoExistir(connection, "YoutubeComandosAudio", "criadoPor", "TEXT");
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
                    listaUpdate = $"{chaveConflito} = {chaveConflito}"; // no-op, evita SQL inválido se todas as colunas forem só-inserção
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
                cmd.Parameters.AddWithValue("@agora", DateTime.UtcNow.ToString("o"));
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

    public bool DebitarPontos()
    {
        try
        {
            CPH.TryGetArg("debitarUserId", out string userId);
            CPH.TryGetArg("debitarBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("debitarCusto", out int custo);
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(broadcastUserId))
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: userId/broadcastUserId ausente para DebitarPontos.");
                return false;
            }

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            using (var connection = AbrirConexao(ambiente))
            using (var cmd = new SQLiteCommand(@"UPDATE UserPoints
                SET moeda = moeda - @custo
                WHERE userId = @userId AND broadcastUserId = @broadcastUserId AND moeda >= @custo;", connection))
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
            CPH.LogError(">>> [GERENTE_DB] ERRO ao debitar pontos: " + ex.Message);
            return false;
        }
    }

    public bool TransferirPontos()
    {
        try
        {
            CPH.TryGetArg("transferirRemetenteUserId", out string remetenteUserId);
            CPH.TryGetArg("transferirDestinatarioNome", out string destinatarioNome);
            CPH.TryGetArg("transferirQuantidade", out int quantidade);
            if (string.IsNullOrEmpty(remetenteUserId) || string.IsNullOrEmpty(destinatarioNome) || quantidade <= 0)
            {
                CPH.LogError(">>> [GERENTE_DB] ERRO: parâmetros inválidos para TransferirPontos.");
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

                    using (var debitoCmd = new SQLiteCommand("UPDATE UserPoints SET moeda = moeda - @quantidade WHERE userId = @userId;", connection))
                    {
                        debitoCmd.Parameters.AddWithValue("@quantidade", quantidade);
                        debitoCmd.Parameters.AddWithValue("@userId", remetenteUserId);
                        debitoCmd.ExecuteNonQuery();
                    }

                    using (var creditoCmd = new SQLiteCommand("UPDATE UserPoints SET moeda = moeda + @quantidade WHERE userId = @userId;", connection))
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
                    CPH.SetArgument("transferirDestinatarioId", destinatarioId);
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
            CPH.LogError(">>> [GERENTE_DB] ERRO ao transferir pontos: " + ex.Message);
            CPH.SetArgument("transferirResultado", "Erro");
            return false;
        }
    }

    private void Executar(SQLiteConnection connection, string sql)
    {
        using (var cmd = new SQLiteCommand(sql, connection))
        {
            cmd.ExecuteNonQuery();
        }
    }

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

    private void RollbackTransacao(SQLiteConnection connection)
    {
        using (var cmd = new SQLiteCommand("ROLLBACK;", connection))
        {
            cmd.ExecuteNonQuery();
        }
    }

    private int ObterSaldoUsuario(SQLiteConnection connection, string userId)
    {
        using (var cmd = new SQLiteCommand("SELECT moeda FROM UserPoints WHERE userId = @userId", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            return resultado != null && resultado != DBNull.Value ? Convert.ToInt32(resultado) : 0;
        }
    }

    private string BuscarUserIdPorNome(SQLiteConnection connection, string userName)
    {
        using (var cmd = new SQLiteCommand("SELECT userId FROM UserPoints WHERE userName = @userName COLLATE NOCASE LIMIT 1", connection))
        {
            cmd.Parameters.AddWithValue("@userName", userName);
            var resultado = cmd.ExecuteScalar();
            return resultado?.ToString();
        }
    }

    private string BuscarUserNamePorId(SQLiteConnection connection, string userId)
    {
        using (var cmd = new SQLiteCommand("SELECT userName FROM UserPoints WHERE userId = @userId", connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            return resultado?.ToString();
        }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }
        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");
    }
}