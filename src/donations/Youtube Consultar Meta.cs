using System;
using Newtonsoft.Json;

// Atualização 260903.1605
public class CPHInline
{
    public bool Execute()
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [META] ERRO: não foi possível ler o contexto do evento.");
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
        // ==========================================
        // CANAL: MADEIRA (1ª Live 12h + 2ª Live Dinâmica de +1h a cada 5k)
        // ==========================================
        if (string.Equals(broadcastUserName, "Madeira", StringComparison.OrdinalIgnoreCase))
        {
            const int metaPrimeiraLive = 60000;
            const int metaSegundaLive = 120000;

            // Fase 1: Em busca da 1ª Live
            if (progressoMensal < metaPrimeiraLive)
            {
                int faltam = metaPrimeiraLive - progressoMensal;
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🎬 1ª Live 12h: Faltam {faltam:N0}/60.000 |";
            }

            // Fase 2: Em busca da 2ª Live (12h fechadas)
            if (progressoMensal < metaSegundaLive)
            {
                int faltamSegunda = metaSegundaLive - progressoMensal;
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆 1ª Live 12h Garantida! | 🎬 2ª Live 12h: Faltam {faltamSegunda:N0}/60.000 |";
            }

            // Ambas concluídas
            return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆🏆 1ª Live 12h + 2ª Live 12h 100% Batidas! 🎉 |";
        }

        // ==========================================
        // CANAL: CAMPOSRAPHA (1ª Live 12h + 2ª Live 12h Fechada)
        // ==========================================
        else if (string.Equals(broadcastUserName, "CamposRapha", StringComparison.OrdinalIgnoreCase))
        {
            const int metaBase = 60000;
            const int pontosPorHoraExtra = 5000;
            const int maxHorasSegundaLive = 12;

            // Fase 1: Em busca da 1ª Live de 12h
            if (progressoMensal < metaBase)
            {
                int faltamBase = metaBase - progressoMensal;
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🎬 Live 12h: Faltam {faltamBase:N0}/{metaBase:N0} |";
            }

            // Fase 2: Calculando progresso dinâmico da 2ª Live
            int pontosExtras = progressoMensal - metaBase;
            int horasSegundaLive = Math.Min(pontosExtras / pontosPorHoraExtra, maxHorasSegundaLive);

            // Entre 60.000 e 64.999 pts (buscando a 1ª hora)
            if (horasSegundaLive == 0)
            {
                int faltamPrimeiraHora = pontosPorHoraExtra - (pontosExtras % pontosPorHoraExtra);
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆 1ª Live 12h Garantida! | 🚀 2ª Live (1h): Faltam {faltamPrimeiraHora:N0}/{pontosPorHoraExtra:N0} |";
            }

            // Entre 1h e 11h acumuladas na 2ª Live
            if (horasSegundaLive < maxHorasSegundaLive)
            {
                int progressoHoraAtual = pontosExtras % pontosPorHoraExtra;
                int faltamProximaHora = pontosPorHoraExtra - progressoHoraAtual;
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆 1ª Live 12h + 2ª Live {horasSegundaLive}h Garantidas! | 🚀 2ª Live (+1h): Faltam {faltamProximaHora:N0}/{pontosPorHoraExtra:N0} |";
            }

            // 120.000+ pts (12h + 12h batidas)
            return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆🏆 1ª Live 12h + 2ª Live 12h 100% Batidas! 🎉 |";
        }

        return $"✨ Meta Mensal: {progressoMensal:N0} pts |";
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
    }

    private Contexto ObterContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrEmpty(contextoJson))
            return null;

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class Evento
    {
        public string BroadcastUserName { get; set; }
    }
}