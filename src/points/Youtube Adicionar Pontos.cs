using System;
using System.Data.SQLite;
using System.IO;
using Newtonsoft.Json;

// Versão 260715.1331

public class CPHInline
{
    private const int IntervaloMinutos = 10;
    private const int PontosPorIntervalo = 10;

    public bool Execute()
    {
        try
        {
            CPH.TryGetArg("pontosPayload", out string json);
            if (string.IsNullOrEmpty(json))
            {
                CPH.LogError(">>> [PONTOS] ERRO: payload 'pontosPayload' não encontrado!");
                return false;
            }

            var payload = JsonConvert.DeserializeObject<PontosPayload>(json);
            if (payload == null || string.IsNullOrEmpty(payload.UserId))
            {
                CPH.LogError(">>> [PONTOS] ERRO: payload inválido!");
                return false;
            }
			
			Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);
			
			if (string.IsNullOrEmpty(ambiente.PastaRaiz))
            {
                CPH.LogError(">>> [PONTOS] ERRO: Variável 'caminhoPastaStreamerBot' não encontrada!");
                return false;
            }
            
            if (!Directory.Exists(ambiente.PastaStream))
                Directory.CreateDirectory(ambiente.PastaStream);
            
            using (var connection = new SQLiteConnection($"Data Source={ambiente.CaminhoBanco};Version=3;"))
            {
                connection.Open();

                using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;", connection))
                {
                    pragmaCmd.ExecuteNonQuery();
                }

                string tableSql = @"CREATE TABLE IF NOT EXISTS UserPoints (
                                    userId TEXT PRIMARY KEY,
                                    userName TEXT,
                                    moeda INTEGER DEFAULT 0,
                                    lastPointAt TEXT,
                                    broadcastUserId TEXT,
                                    broadcastUserName TEXT);";
                using (var cmd = new SQLiteCommand(tableSql, connection))
                {
                    cmd.ExecuteNonQuery();
                }

				using (var beginCmd = new SQLiteCommand("BEGIN IMMEDIATE;", connection))
				{
					beginCmd.ExecuteNonQuery();
				}
				
				try
                {
                    // Se o registro não existir pelo ID recebido, busca pelo userName
                    if (!VerificarSeIdExiste(connection, payload.UserId))
                    {
                        string idRealPeloNome = BuscarIdPorNome(connection, payload.UserName);

                        if (!string.IsNullOrEmpty(idRealPeloNome))
                        {
                            CPH.LogInfo($">>> [PONTOS] ID Corrigido via Busca Reversa: {payload.UserId} -> {idRealPeloNome}");
                            payload.UserId = idRealPeloNome; // Altera o ID no payload para o ID real do YouTube!
                        }
                    }
                    
                    DateTime? lastPointAt = BuscarUltimoPonto(connection, payload.UserId);

                    bool podePontuar = payload.Pontos.HasValue || lastPointAt == null || (payload.Timestamp - lastPointAt.Value).TotalMinutes >= IntervaloMinutos;
					
					if (!podePontuar)
                    {
						CPH.LogDebug($">>> [PONTOS] {payload.UserName} ainda dentro do cooldown de {IntervaloMinutos}min. Ignorado.");
						using (var rollbackCmd = new SQLiteCommand("ROLLBACK;", connection)) { rollbackCmd.ExecuteNonQuery(); }
						return true;
					}
					
					int pontosAConceder = payload.Pontos ?? PontosPorIntervalo;
					AtualizarPontos(connection, payload, pontosAConceder);
					
					using (var commitCmd = new SQLiteCommand("COMMIT;", connection)) { commitCmd.ExecuteNonQuery(); }
					
					CPH.LogDebug($">>> [PONTOS] {payload.UserName} recebeu {pontosAConceder} pontos. Origem: {payload.Origem}");
                }
                catch
                {
                    using (var rollbackCmd = new SQLiteCommand("ROLLBACK;", connection)) { rollbackCmd.ExecuteNonQuery(); }
                    throw;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [PONTOS] ERRO CRÍTICO: " + ex.Message);
            return false;
        }
    }
    
    private bool VerificarSeIdExiste(SQLiteConnection connection, string userId)
    {
        string query = "SELECT 1 FROM UserPoints WHERE userId = @userId LIMIT 1";
        using (var cmd = new SQLiteCommand(query, connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var result = cmd.ExecuteScalar();
            return result != null;
        }
    }
    
    private string BuscarIdPorNome(SQLiteConnection connection, string userName)
    {
        // Busca o ID real associado a este nickname (dando preferência aos IDs legítimos do YouTube que começam com "UC")
        string query = "SELECT userId FROM UserPoints WHERE userName = @userName COLLATE NOCASE ORDER BY CASE WHEN userId LIKE 'UC%' THEN 0 ELSE 1 END LIMIT 1";
        using (var cmd = new SQLiteCommand(query, connection))
        {
            cmd.Parameters.AddWithValue("@userName", userName);
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        }
    }
    
    private DateTime? BuscarUltimoPonto(SQLiteConnection connection, string userId)
    {
        string selectSql = "SELECT lastPointAt FROM UserPoints WHERE userId = @userId";
        using (var cmd = new SQLiteCommand(selectSql, connection))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            var resultado = cmd.ExecuteScalar();
            if (resultado == null || resultado == DBNull.Value) return null;
            return DateTime.Parse(resultado.ToString());
        }
    }

    private void AtualizarPontos(SQLiteConnection connection, PontosPayload payload, int pontos)
    {
        string upsertSql = @"INSERT INTO UserPoints (userId, userName, moeda, lastPointAt, broadcastUserId, broadcastUserName)
                            VALUES (@userId, @userName, @pontos, @timestamp, @broadcastUserId, @broadcastUserName)
                            ON CONFLICT(userId) DO UPDATE SET
                                userName = @userName,
                                moeda = moeda + @pontos,
                                lastPointAt = CASE WHEN @isDoacao = 0 THEN @timestamp ELSE lastPointAt END,
                                broadcastUserId = COALESCE(NULLIF(@broadcastUserId, ''), broadcastUserId),
                                broadcastUserName = COALESCE(NULLIF(@broadcastUserName, ''), broadcastUserName);";
        int tentativas = 0;
        const int maxTentativas = 3;
        while (true)
        {
            try
            {
                using (var cmd = new SQLiteCommand(upsertSql, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", payload.UserId);
                    cmd.Parameters.AddWithValue("@userName", payload.UserName);
                    cmd.Parameters.AddWithValue("@pontos", pontos);
                    cmd.Parameters.AddWithValue("@timestamp", payload.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@broadcastUserId", payload.BroadcastUserId);
                    cmd.Parameters.AddWithValue("@broadcastUserName", payload.BroadcastUserName);
					cmd.Parameters.AddWithValue("@isDoacao", payload.Pontos.HasValue ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }
                break;
            }
            catch (SQLiteException sqlEx) when (sqlEx.ResultCode == SQLiteErrorCode.Busy || sqlEx.ResultCode == SQLiteErrorCode.Locked)
            {
                tentativas++;
                if (tentativas >= maxTentativas) throw;
                CPH.LogWarn($">>> [PONTOS] Banco ocupado, tentativa {tentativas}/{maxTentativas}...");
                System.Threading.Thread.Sleep(150 * tentativas);
            }
        }
    }

    public class PontosPayload
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public DateTime Timestamp { get; set; }
        public string Origem { get; set; }
        public int? Pontos { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }
        
        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");
    }
}