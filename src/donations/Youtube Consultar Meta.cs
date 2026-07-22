using System;
using System.IO;
using System.Data.SQLite;
using Newtonsoft.Json;

// Atualização 260721.2040

public class CPHInline
{
    public bool Execute()
    {
        try
        {
            CPH.TryGetArg("contextoMensagem", out string contextoJson);
            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);

            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                CPH.LogError(">>> [META] ERRO: contexto ausente ou inválido.");
                return false;
            }

            var evento = contexto.Evento;
            var ambiente = contexto.Ambiente;

            CPH.LogInfo($">>> [META] BroadcastUserName: {evento.BroadcastUserName}");
            CPH.LogInfo($">>> [META] CaminhoBanco resolvido: {ambiente.CaminhoBanco}");
            CPH.LogInfo($">>> [META] Arquivo existe? {File.Exists(ambiente.CaminhoBanco)}");
            
            int progressoMensal = File.Exists(ambiente.CaminhoBanco) ? ObterProgressoMeta(ambiente.CaminhoBanco, evento.BroadcastUserName) : 0;

            string mensagem = MontarMensagem(evento.BroadcastUserName, progressoMensal);

            CPH.SendYouTubeMessage(mensagem, false);
            
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [META] ERRO CRÍTICO: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao consultar a meta.");
            return false;
        }
    }

    private int ObterProgressoMeta(string caminhoBanco, string broadcastUserName)
    {
        using (var connection = new SQLiteConnection($"Data Source={caminhoBanco};Version=3;"))
        {
            connection.Open();

            using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;", connection))
            {
                pragmaCmd.ExecuteNonQuery();
            }

            string selectSql = @"SELECT COALESCE(SUM(pontosMeta), 0) FROM YoutubeDoacoes
                                WHERE broadcastUserName = @broadcastUserName COLLATE NOCASE AND timestamp >= date('now', 'start of month');";

            using (var cmd = new SQLiteCommand(selectSql, connection))
            {
                cmd.Parameters.AddWithValue("@broadcastUserName", broadcastUserName);
                var resultado = cmd.ExecuteScalar();
                return resultado != null ? Convert.ToInt32(resultado) : 0;
            }
        }
    }

    private string MontarMensagem(string broadcastUserName, int progressoMensal)
    {
        string mensagem = $"✨ Meta Mensal {progressoMensal:N0} Pontos";

        if (string.Equals(broadcastUserName, "Madeira", StringComparison.OrdinalIgnoreCase))
        {
            mensagem += " | Live de 12 Horas " + (progressoMensal < 60000 ? "- Faltam " + (60000 - progressoMensal).ToString("N0") + "/60.000 |" : "- 100% |");
        }
        else if (string.Equals(broadcastUserName, "CamposRapha", StringComparison.OrdinalIgnoreCase))
        {
            mensagem += " | Live de 12 Horas " + (progressoMensal < 60000 ? "- Faltam " + (60000 - progressoMensal).ToString("N0") + "/60.000 |" : "- 100% |");

            if (progressoMensal >= 60000)
            {
                mensagem += " Presente Bodas de Algodão " + (progressoMensal < 90000 ? "- Faltam " + (90000 - progressoMensal).ToString("N0") + "/30.000 |" : "- 100% |");
            }
        }

        return mensagem;
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
        public Ambiente Ambiente { get; set; }
    }

    public class Evento
    {
        public string BroadcastUserName { get; set; }
    }

    public class Ambiente
    {
        public string CaminhoBanco { get; set; }
    }
}