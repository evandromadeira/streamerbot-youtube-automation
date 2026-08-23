using System;
using Newtonsoft.Json;

// Atualização 260822.1615
public class CPHInline
{
    public bool Execute()
    {
        try
        {
            CPH.TryGetArg("contextoJson", out string contextoJson);
            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);

            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [META] ERRO: contexto ausente ou inválido.");
                return false;
            }

            var evento = contexto.Evento;

            CPH.SetArgument("metaBroadcastUserName", evento.BroadcastUserName);
            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ObterProgressoMeta");
            CPH.TryGetArg("metaProgressoMensal", out int progressoMensal);

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
    }

    public class Evento
    {
        public string BroadcastUserName { get; set; }
    }
}