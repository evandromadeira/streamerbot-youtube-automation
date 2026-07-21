using System;
using System.Data.SQLite;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

// Versão 260714.2125

public class CPHInline
{
    public bool Execute()
    {
        Evento evento = null;
        
        try
        {
            evento = new Evento(CPH);
            
            CPH.LogInfo($"userId: {evento.UserId}");
            CPH.LogInfo($"userName: {evento.UserName}");
            CPH.LogInfo($"messageId: {evento.MessageId}");
            CPH.LogInfo($"message: {evento.MessageText}");
            CPH.LogInfo($"broadcastUserId: {evento.BroadcastUserId}");
            CPH.LogInfo($"broadcastUserName: {evento.BroadcastUserName}");
            CPH.LogInfo($"isSubscribed: {evento.IsSub}");
            CPH.LogInfo($"userIsSponsor: {evento.IsSpo}");
            CPH.LogInfo($"isModerator: {evento.IsMod}");
            CPH.LogInfo($"userPreviousActive: {evento.UserPreviousActive}");
            CPH.LogInfo($"publishedAt: {evento.PublishedAt}");

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);
            
            if (string.IsNullOrEmpty(ambiente.PastaRaiz))
            {
                CPH.LogError(">>> [CHAT_LOG] ERRO: Variável 'caminhoPastaStreamerBot' não encontrada!");
                return false;
            }
            
            if (!Directory.Exists(ambiente.PastaStream))
                Directory.CreateDirectory(ambiente.PastaStream);
            
            using (var connection = new SQLiteConnection($"Data Source={ambiente.CaminhoBanco};Version=3;"))
            {
                connection.Open();
                
                // WAL reduz conflitos de lock em escrita concorrente (mensagens quase simultâneas)
                using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;", connection))
                {
                    pragmaCmd.ExecuteNonQuery();
                }
                string tableSql = @"CREATE TABLE IF NOT EXISTS ChatLog (
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
                                    publishedAt TEXT);";
                using (var cmd = new SQLiteCommand(tableSql, connection))
                {
                    cmd.ExecuteNonQuery();
                }

                string insertSql = @"INSERT INTO ChatLog
                                    (userId, userName, messageId, message, broadcastUserId, broadcastUserName, isSubscribed, isSponsor, isModerator, userPreviousActive, publishedAt)
                                    VALUES
                                    (@userId, @userName, @messageId, @message, @broadcastUserId, @broadcastUserName, @isSubscribed, @isSponsor, @isModerator, @userPreviousActive, @publishedAt)";
                using (var insertCmd = new SQLiteCommand(insertSql, connection))
                {
                    insertCmd.Parameters.AddWithValue("@userId", evento.UserId);
                    insertCmd.Parameters.AddWithValue("@userName", evento.UserName);
                    insertCmd.Parameters.AddWithValue("@messageId", evento.MessageId);
                    insertCmd.Parameters.AddWithValue("@message", evento.MessageText);
                    insertCmd.Parameters.AddWithValue("@broadcastUserId", evento.BroadcastUserId);
                    insertCmd.Parameters.AddWithValue("@broadcastUserName", evento.BroadcastUserName);

                    insertCmd.Parameters.AddWithValue("@isSubscribed", evento.IsSub ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("@isSponsor", evento.IsSpo ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("@isModerator", evento.IsMod ? 1 : 0);

                    insertCmd.Parameters.AddWithValue("@userPreviousActive", evento.UserPreviousActive);
                    insertCmd.Parameters.AddWithValue("@publishedAt", evento.PublishedAt);

                    // Retry simples em caso de "database is locked" mesmo com WAL (picos de concorrência)
                    int tentativas = 0;
                    const int maxTentativas = 3;
                    while (true)
                    {
                        try
                        {
                            insertCmd.ExecuteNonQuery();
                            break;
                        }
                        catch (SQLiteException sqlEx) when (sqlEx.ResultCode == SQLiteErrorCode.Busy || sqlEx.ResultCode == SQLiteErrorCode.Locked)
                        {
                            tentativas++;
                            if (tentativas >= maxTentativas)
                                throw;
                            CPH.LogWarn($">>> [CHAT_LOG] Banco ocupado, tentativa {tentativas}/{maxTentativas}...");
                            System.Threading.Thread.Sleep(150 * tentativas);
                        }
                    }
                }
            }

            CPH.LogDebug(">>> [CHAT_LOG] DADO SALVO COM SUCESSO!");

			DispararPontuacao(evento);

            return true;
        }
        catch (Exception ex)
        {
			CPH.LogError(">>> [CHAT_LOG] ERRO CRÍTICO: " + ex.Message);
            return false;
        }
    }

	private void DispararPontuacao(Evento evento)
	{
		var payload = new
		{
			UserId = evento.UserId,
			UserName = evento.UserName,
			Timestamp = DateTime.Now,
			Origem = "chat",
			BroadcastUserId = evento.BroadcastUserId,
			BroadcastUserName = evento.BroadcastUserName
		};

		string json = JsonConvert.SerializeObject(payload);

		CPH.SetArgument("pontosPayload", json);
		CPH.RunAction("Youtube Adicionar Pontos");
	}

    public class Evento
    {
        public string UserId { get; }
        public string UserName { get; }
        public string MessageId { get; }
        public string MessageText { get; }
        public string BroadcastUserId { get; }
        public string BroadcastUserName { get; }

        public bool IsSub { get; }
        public bool IsSpo { get; }
        public bool IsMod { get; }

        public string UserPreviousActive { get; }
        public string PublishedAt { get; }

        public Evento(IInlineInvokeProxy CPH)
        {
            CPH.TryGetArg("userId", out string userId);
            CPH.TryGetArg("userName", out string userName);
            CPH.TryGetArg("messageId", out string messageId);
            CPH.TryGetArg("message", out string messageText);
            CPH.TryGetArg("broadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("broadcastUserName", out string broadcastUserName);

            CPH.TryGetArg("isSubscribed", out bool isSub);
            CPH.TryGetArg("userIsSponsor", out bool isSpo);
            CPH.TryGetArg("isModerator", out bool isMod);

            CPH.TryGetArg("userPreviousActive", out DateTime userPreviousActive);

            UserId = userId;
            UserName = userName;
            MessageId = messageId;
            MessageText = messageText;
            BroadcastUserId = broadcastUserId;
            BroadcastUserName = string.IsNullOrEmpty(broadcastUserName) ? "YOUTUBE" : broadcastUserName;

            IsSub = isSub;
            IsSpo = isSpo;
            IsMod = isMod;

            UserPreviousActive = userPreviousActive != DateTime.MinValue ? userPreviousActive.ToString("yyyy-MM-dd HH:mm:ss") : "Primeira Mensagem";
            PublishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }
        
        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");
    }
}