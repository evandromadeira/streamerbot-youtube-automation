using System;
using Newtonsoft.Json;

// Atualização 261014.1055
public class CPHInline
{
    private const int MetaPrimeiraLiveMadeira = 60000;
    private const int MetaSegundaLiveMadeira = 120000;
    private const int MetaBaseRapha = 60000;
    private const int PontosPorHoraExtraRapha = 5000;
    private const int MaxHorasSegundaLiveRapha = 12;

    private const string Prefixo = "[CONSULTAR_META] ";

    public bool ConsultarMeta()
    {
        Evento evento = null;

        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }

            evento = contexto.Evento;

            if (string.IsNullOrWhiteSpace(evento.BroadcastUserName))
            {
                LogError("ERRO: não foi possível obter o nome do canal conectado.");
                EnviarMensagem("❌ Não foi possível consultar a meta. Tente novamente.", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            bool consultou = false;

            int progressoMensal = 0;

            for (int tentativa = 1; tentativa <= 3; tentativa++)
            {
                CPH.SetArgument("metaBroadcastUserName", evento.BroadcastUserName);
                CPH.SetArgument("metaConsultaSucesso", false);
                CPH.SetArgument("metaProgressoMensal", null);

                try
                {
                    bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ObterProgressoMeta");
                    CPH.TryGetArg("metaConsultaSucesso", out bool sucesso);
                    consultou = executou && sucesso && CPH.TryGetArg("metaProgressoMensal", out progressoMensal);
                }
                catch (Exception ex)
                {
                    LogWarn($"Tentativa {tentativa}/3 lançou erro: {ex.Message}");
                }

                if (consultou)
                {
                    break;
                }

                LogWarn($"Consulta sem sucesso: canal '{evento.BroadcastUserName}', tentativa {tentativa}/3.");
                if (tentativa < 3)
                {
                    CPH.Wait(500);
                }
            }

            if (!consultou)
            {
                LogError($"Consulta da meta falhou após 3 tentativas para o canal '{evento.BroadcastUserName}'.");
                EnviarMensagem("❌ Não foi possível consultar a meta. Tente novamente.", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string mensagem = MontarMensagem(evento.BroadcastUserName, progressoMensal);

            EnviarMensagem(mensagem, evento.BroadcastId, somenteNaTransmissao: true);

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao consultar a meta.", evento?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }
    }

    private string MontarMensagem(string broadcastUserName, int progressoMensal)
    {
        // CANAL: MADEIRA (1ª Live 12h + 2ª Live 12h Fechada)
        if (string.Equals(broadcastUserName, "madeira_on", StringComparison.OrdinalIgnoreCase))
        {
            // Fase 1: Em busca da 1ª Live
            if (progressoMensal < MetaPrimeiraLiveMadeira)
            {
                int faltam = MetaPrimeiraLiveMadeira - progressoMensal;
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🎬 1ª Live 12h: Faltam {faltam:N0}/60.000 |";
            }

            // Fase 2: Em busca da 2ª Live (12h fechadas)
            if (progressoMensal < MetaSegundaLiveMadeira)
            {
                int faltamSegunda = MetaSegundaLiveMadeira - progressoMensal;
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆 1ª Live 12h Garantida! | 🎬 2ª Live 12h: Faltam {faltamSegunda:N0}/60.000 |";
            }

            // Ambas concluídas
            return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆🏆 1ª Live 12h + 2ª Live 12h 100% Batidas! 🎉 |";
        }

        // CANAL: CAMPOSRAPHA (1ª Live 12h + 2ª Live Dinâmica de +1h a cada 5k)
        else if (string.Equals(broadcastUserName, "canalcamposrapha", StringComparison.OrdinalIgnoreCase))
        {
            // Fase 1: Em busca da 1ª Live de 12h
            if (progressoMensal < MetaBaseRapha)
            {
                int faltamBase = MetaBaseRapha - progressoMensal;
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🎬 Live 12h: Faltam {faltamBase:N0}/{MetaBaseRapha:N0} |";
            }

            // Fase 2: Calculando progresso dinâmico da 2ª Live
            int pontosExtras = progressoMensal - MetaBaseRapha;
            int horasSegundaLive = Math.Min(pontosExtras / PontosPorHoraExtraRapha, MaxHorasSegundaLiveRapha);

            // Entre 60.000 e 64.999 pts (buscando a 1ª hora)
            if (horasSegundaLive == 0)
            {
                int faltamPrimeiraHora = PontosPorHoraExtraRapha - (pontosExtras % PontosPorHoraExtraRapha);
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆 1ª Live 12h Garantida! | 🚀 2ª Live (1h): Faltam {faltamPrimeiraHora:N0}/{PontosPorHoraExtraRapha:N0} |";
            }

            // Entre 1h e 11h acumuladas na 2ª Live
            if (horasSegundaLive < MaxHorasSegundaLiveRapha)
            {
                int progressoHoraAtual = pontosExtras % PontosPorHoraExtraRapha;
                int faltamProximaHora = PontosPorHoraExtraRapha - progressoHoraAtual;
                return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆 1ª Live 12h + 2ª Live {horasSegundaLive}h Garantidas! | 🚀 2ª Live (+1h): Faltam {faltamProximaHora:N0}/{PontosPorHoraExtraRapha:N0} |";
            }

            // 120.000+ pts (12h + 12h batidas)
            return $"✨ Meta Mensal: {progressoMensal:N0} pts | 🏆🏆 1ª Live 12h + 2ª Live 12h 100% Batidas! 🎉 |";
        }

        return $"✨ Meta Mensal: {progressoMensal:N0} pts |";
    }

    private void EnviarMensagem(string mensagem, string broadcastId, bool somenteNaTransmissao)
    {
        // Direciona a resposta quando solicitado e há uma transmissão identificada.
        if (somenteNaTransmissao && !string.IsNullOrWhiteSpace(broadcastId))
        {
            CPH.SendYouTubeMessage(mensagem, false, true, broadcastId);
        }
        else
        {
            CPH.SendYouTubeMessage(mensagem);
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

    private Contexto ObterContexto()
    {
        // Obtém o contexto criado pelo Gerente de Chat
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrEmpty(contextoJson))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
    }

    public class Evento
    {
        public string BroadcastId { get; set; }
        public string BroadcastUserName { get; set; }
    }
}