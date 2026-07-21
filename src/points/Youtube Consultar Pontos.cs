using System;
using System.Data.SQLite;
using System.IO;

// Versão 260718.1510

public class CPHInline
{
    public bool Execute()
    {
        Evento evento = null;

        try
        {
            evento = new Evento(CPH);

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            if (string.IsNullOrEmpty(ambiente.PastaRaiz))
            {
                CPH.LogError(">>> [CONSULTA_PONTOS] ERRO: Variável 'caminhoPastaStreamerBot' não encontrada!");
                CPH.SendYouTubeMessage("❌ Erro de configuração: pasta raiz do bot não localizada.");
                return false;
            }
            
            string[] partesComando = (evento.RawInput ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string mensagem = partesComando.Length > 1 ? partesComando[1].Replace("@", "").Trim() : "";

            bool consultaPropria = string.IsNullOrEmpty(mensagem);
            string nomeConsultado = consultaPropria ? evento.UserName : mensagem.Trim();
            
            if (!File.Exists(ambiente.CaminhoBanco))
            {
                CPH.SendYouTubeMessage($"ℹ @{nomeConsultado} ainda não possui pontos registrados.");
                return true;
            }

            using (var connection = new SQLiteConnection($"Data Source={ambiente.CaminhoBanco};Version=3;"))
            {
                connection.Open();

                using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;", connection))
                {
                    pragmaCmd.ExecuteNonQuery();
                }

                int? pontosUsuario = null;
                string nomeExibido = nomeConsultado;

                string selectSql = consultaPropria
                    ? "SELECT userName, moeda FROM UserPoints WHERE userId = @chave"
                    : "SELECT userName, moeda FROM UserPoints WHERE userName = @chave COLLATE NOCASE";

                using (var cmd = new SQLiteCommand(selectSql, connection))
                {
                    cmd.Parameters.AddWithValue("@chave", consultaPropria ? evento.UserId : nomeConsultado);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            nomeExibido = reader["userName"].ToString();
                            pontosUsuario = Convert.ToInt32(reader["moeda"]);
                        }
                    }
                }

                if (pontosUsuario == null)
                {
                    CPH.SendYouTubeMessage($"ℹ @{nomeExibido} ainda não possui pontos registrados.");
                    return true;
                }

                int rankUsuario;
                
                using (var rankCmd = new SQLiteCommand("SELECT COUNT(*) FROM UserPoints WHERE moeda > @pontos", connection))
                {
                    rankCmd.Parameters.AddWithValue("@pontos", pontosUsuario.Value);
                    rankUsuario = Convert.ToInt32(rankCmd.ExecuteScalar()) + 1;
                }
                
                string posicaoTexto = rankUsuario switch
                {
                    1 => "🥇",
                    2 => "🥈",
                    3 => "🥉",
                    _ => "✨"
                };

                CPH.SendYouTubeMessage($"{posicaoTexto}(#{rankUsuario}) - @{nomeExibido}: {pontosUsuario.Value:N0} Pontos");
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [CONSULTA_PONTOS] ERRO CRÍTICO: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao consultar pontos.");
            return false;
        }
    }
    
    public class Evento
    {
        public string UserId { get; }
        public string UserName { get; }
        public string RawInput { get; }

        public Evento(IInlineInvokeProxy CPH)
        {
            CPH.TryGetArg("userId", out string userId);
            CPH.TryGetArg("userName", out string userName);
            CPH.TryGetArg("rawInput", out string rawInput);

            UserId = userId;
            UserName = userName;
            RawInput = rawInput;
        }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }

        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");
    }
}