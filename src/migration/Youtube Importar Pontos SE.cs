using System;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using System.Data.SQLite;
using Newtonsoft.Json.Linq;

// Versão 260715.1334

public class CPHInline
{
    public bool Execute()
    {
        Evento evento = null;
		bool debitoSucesso = false;
		string twitchUser = null;
		int pontosAImportar = 0;
		
        try
        {
            evento = new Evento(CPH);

            if (string.IsNullOrEmpty(evento.RawInput))
            {
                CPH.SendYouTubeMessage($"⚠ Uso correto: !importar [nome_na_twitch] [quantidade ou all]");
                return false;
            }
			
			string[] partes = evento.RawInput.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			
            if (partes.Length < 2)
            {
                CPH.SendYouTubeMessage($"⚠ Uso correto: !importar [nome_na_twitch] [quantidade ou all]");
                return false;
            }

            twitchUser = partes[0].Trim();
            string qtdInput = partes[1].Trim().ToLower();

            Ambiente ambiente = new Ambiente();
			
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);
            ambiente.PastaSE = CPH.GetGlobalVar<string>("caminhoPastaStreamElements", true);

            if (string.IsNullOrEmpty(ambiente.PastaRaiz))
            {
                CPH.LogError(">>> [IMPORT_SE] ERRO: Variável global 'caminhoPastaStreamerBot' não definida!");
                CPH.SendYouTubeMessage("❌ Erro de configuração: pasta raiz do bot não localizada.");
                return false;
            }

            if (string.IsNullOrEmpty(ambiente.PastaSE))
            {
                CPH.LogError(">>> [IMPORT_SE] ERRO: Variável global 'caminhoPastaStreamElements' não definida!");
                CPH.SendYouTubeMessage("❌ Erro de configuração: pasta do StreamElements não localizada.");
                return false;
            }
            
            if (!Directory.Exists(ambiente.PastaStream))
                Directory.CreateDirectory(ambiente.PastaStream);
            
            if (!File.Exists(ambiente.CaminhoConfigSE))
            {
                CPH.LogError($">>> [IMPORT_SE] ERRO: Arquivo {ambiente.CaminhoConfigSE} não localizado!");
                CPH.SendYouTubeMessage("❌ Erro interno: arquivo de credenciais ausente.");
                return false;
            }

            var variaveisConfigSE = CarregarVariaveisDoArquivo(ambiente.CaminhoConfigSE);
            var usuarioEmissao = CPH.GetGlobalVar<string>("usuarioEmissao", true);
			
			if (string.IsNullOrEmpty(usuarioEmissao))
			{
				CPH.LogError(">>> [IMPORT_SE] ERRO: Variável global 'usuarioEmissao' não definida!");
				CPH.SendYouTubeMessage("❌ Erro de configuração: usuário emissor não localizado.");
				return false;
			}
			
			string keyPrefix = usuarioEmissao.ToLower();

            string jwtToken = variaveisConfigSE.ContainsKey($"jwt_token_{keyPrefix}") ? variaveisConfigSE[$"jwt_token_{keyPrefix}"] : null;
            string idCanal = variaveisConfigSE.ContainsKey($"channel_id_{keyPrefix}") ? variaveisConfigSE[$"channel_id_{keyPrefix}"] : null;
            string moedaSE = variaveisConfigSE.ContainsKey($"moeda_se_{keyPrefix}") ? variaveisConfigSE[$"moeda_se_{keyPrefix}"] : "pontos";

            if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(idCanal))
            {
                CPH.LogError($">>> [IMPORT_SE] ERRO: Token ou Channel ID ausentes para o canal {usuarioEmissao}!");
                CPH.SendYouTubeMessage("❌ Falha de autenticação com o StreamElements.");
                return false;
            }
			
			var api = new ApiStreamElements(jwtToken, idCanal);
			
            (int saldoSE, _) = api.ConsultarSaldoERankUsuario(twitchUser);

            if (saldoSE <= 0)
            {
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, o usuário Twitch '{twitchUser}' não possui {moedaSE} para importar.");
                return false;
            }
			
			if (qtdInput == "all")
            {
                pontosAImportar = saldoSE;
            }
            else if (int.TryParse(qtdInput, out int qtdInformada))
            {
                if (qtdInformada <= 0)
                {
                    CPH.SendYouTubeMessage($"⚠ A quantidade informada precisa ser maior que zero!");
                    return false;
                }
                if (qtdInformada > saldoSE)
                {
                    CPH.SendYouTubeMessage($"❌ Saldo insuficiente na Twitch! '{twitchUser}' possui apenas {saldoSE:N0} {moedaSE}.");
                    return false;
                }
                pontosAImportar = qtdInformada;
            }
            else
            {
                CPH.SendYouTubeMessage($"⚠ Entrada inválida! Use um valor numérico ou 'all'.");
                return false;
            }
			
			debitoSucesso = api.DebitarPontosUsuario(twitchUser, pontosAImportar);
			
			if (!debitoSucesso)
            {
                CPH.SendYouTubeMessage($"❌ Erro de comunicação ao tentar debitar pontos do StreamElements.");
                return false;
            }
			
			int totalPontosAtual = 0;
            int rankUsuario;

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
				
				string upsertSql = @"INSERT INTO UserPoints (userId, userName, moeda, lastPointAt, broadcastUserId, broadcastUserName)
                                    VALUES (@userId, @userName, @pontos, @timestamp, @broadcastUserId, @broadcastUserName)
                                    ON CONFLICT(userId) DO UPDATE SET
                                        userName = @userName,
                                        moeda = moeda + @pontos,
                                        lastPointAt = @timestamp,
                                        broadcastUserId = @broadcastUserId,
                                        broadcastUserName = @broadcastUserName;";

                using (var cmd = new SQLiteCommand(upsertSql, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", evento.UserId);
                    cmd.Parameters.AddWithValue("@userName", evento.UserName);
                    cmd.Parameters.AddWithValue("@pontos", pontosAImportar);
                    cmd.Parameters.AddWithValue("@timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@broadcastUserId", evento.BroadcastUserId);
                    cmd.Parameters.AddWithValue("@broadcastUserName", evento.BroadcastUserName);

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
                            if (tentativas >= maxTentativas) throw;
                            System.Threading.Thread.Sleep(150 * tentativas);
                        }
                    }
                }
				
				using (var selectCmd = new SQLiteCommand("SELECT moeda FROM UserPoints WHERE userId = @userId", connection))
				{
					selectCmd.Parameters.AddWithValue("@userId", evento.UserId);
					var resultado = selectCmd.ExecuteScalar();
					if (resultado != null && resultado != DBNull.Value)
						totalPontosAtual = Convert.ToInt32(resultado);
				}

                using (var rankCmd = new SQLiteCommand("SELECT COUNT(*) FROM UserPoints WHERE moeda > @pontos", connection))
                {
                    rankCmd.Parameters.AddWithValue("@pontos", totalPontosAtual);
                    rankUsuario = Convert.ToInt32(rankCmd.ExecuteScalar()) + 1;
                }
            }

            CPH.SendYouTubeMessage($"✅ Importação concluída! {pontosAImportar:N0} {moedaSE} foram retirados de '{twitchUser}' (Twitch) e adicionados à conta de @{evento.UserName} no YouTube! Novo Saldo: {totalPontosAtual:N0} Pontos (#{rankUsuario}). 🎉");
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [IMPORT_SE] ERRO CRÍTICO: " + ex.Message);
			
			if (debitoSucesso)
			{
				CPH.LogError($">>> [IMPORT_SE] ATENÇÃO — AJUSTE MANUAL NECESSÁRIO: {pontosAImportar} pontos já foram debitados de '{twitchUser}' na StreamElements mas NÃO foram creditados no YouTube (destinatário: {evento?.UserName} / userId: {evento?.UserId}).");
			}
			
			CPH.SendYouTubeMessage("❌ Falha técnica ao tentar concluir a importação.");
            return false;
        }
    }

    private Dictionary<string, string> CarregarVariaveisDoArquivo(string arqConfigSE)
    {
        var variaveisArquivo = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(arqConfigSE))
        {
            if (line.Contains("="))
            {
                var partes = line.Split(new[] { '=' }, 2);
                if (partes.Length == 2) 
                    variaveisArquivo[partes[0].Trim()] = partes[1].Trim();
            }
        }
        return variaveisArquivo;
    }
    
    public class Evento
    {
        public string UserId { get; }
        public string UserName { get; }
        public string RawInput { get; }
        public string BroadcastUserId { get; }
        public string BroadcastUserName { get; }

        public Evento(IInlineInvokeProxy CPH)
        {
            CPH.TryGetArg("userId", out string userId);
            CPH.TryGetArg("userName", out string userName);
            CPH.TryGetArg("rawInput", out string rawInput);
            CPH.TryGetArg("broadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("broadcastUserName", out string broadcastUserName);

            UserId = userId;
            UserName = userName;
            RawInput = rawInput;
            BroadcastUserId = broadcastUserId;
            BroadcastUserName = string.IsNullOrEmpty(broadcastUserName) ? "YOUTUBE" : broadcastUserName;
        }
    }

    public class ApiStreamElements
    {
        private readonly string jwtToken;
        private readonly string idCanal;
        private readonly HttpClient client = new HttpClient();

        public ApiStreamElements(string jwtToken, string idCanal)
        {
            this.jwtToken = jwtToken;
            this.idCanal = idCanal;
        }

        public (int saldo, int rank) ConsultarSaldoERankUsuario(string usuario)
        {
            try
            {
                string url = $"https://api.streamelements.com/kappa/v2/points/{this.idCanal}/{usuario}";
                var getRequest = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri(url),
                    Headers =
                    {
                        { "Accept", "application/json; charset=utf-8" },
                        { "Authorization", $"Bearer {this.jwtToken}" }
                    },
                };

                var getResponse = client.SendAsync(getRequest).GetAwaiter().GetResult();
                if (!getResponse.IsSuccessStatusCode) return (0, 0);

                var getBody = getResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var json = JToken.Parse(getBody);

                int usuarioPoints = (int)json["points"];
                int usuarioRank = (int)json["rank"];

                return (usuarioPoints, usuarioRank);
            }
            catch (Exception)
            {
                return (0, 0);
            }
        }

        public bool DebitarPontosUsuario(string usuario, int pontos)
        {
            try
            {
                string url = $"https://api.streamelements.com/kappa/v2/points/{this.idCanal}/{usuario}/-{pontos}";
                var putRequest = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri(url),
                    Headers =
                    {
                        { "Accept", "application/json; charset=utf-8" },
                        { "Authorization", $"Bearer {this.jwtToken}" }
                    },
                };

                var putResponse = client.SendAsync(putRequest).GetAwaiter().GetResult();
                return putResponse.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
    
    public class Ambiente
    {
        public string PastaRaiz { get; set; }
        public string PastaSE { get; set; }

        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");
        public string CaminhoConfigSE => Path.Combine(PastaSE, "ConfigSE.txt");
    }
}