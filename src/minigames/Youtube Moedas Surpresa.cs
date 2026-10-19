using System;
using System.Linq;
using System.Collections.Generic;

// Atualização 261015.1115
public class CPHInline
{
    private const int TempoEsperaMinimoSegundos = 60;
    private const int TempoEsperaMaximoSegundos = 1800;
    private const int TempoEsperaEnvioMensagemSegundos = 30;

    private const string Prefixo = "[MOEDAS SURPRESA] ";

    public bool Execute()
    {
        bool isContinuacao = CPH.GetGlobalVar<bool>("moedasSurpresaContinuacao", false);
        if (isContinuacao)
        {
            CPH.UnsetGlobalVar("moedasSurpresaContinuacao", false);
        }
        else
        {
            bool jaAtivo = CPH.GetGlobalVar<bool>("moedasSurpresaAtivo", false); // non-persisted
            if (jaAtivo)
            {
                LogInfo("Já existe um ciclo em execução, ignorando novo start.");
                return true;
            }

            CPH.SetGlobalVar("moedasSurpresaAtivo", true, false); // non-persisted
        }

        try
        {
            Random rnd = new Random();

            int tempoEsperaSegundos = rnd.Next(TempoEsperaMinimoSegundos, TempoEsperaMaximoSegundos + 1);

            LogInfo($"Aguardando {tempoEsperaSegundos} segundos para lançar o evento...");
            CPH.Wait(tempoEsperaSegundos * 1000);

            List<string> palavras = new List<string>
            {
                "!batatafrita",
                "!biscoito",
                "!bixcoito",
                "!bolacha",
                "!cafe",
                "!cha",
                "!chocolate",
                "!deixaolike",
                "!diaboverde",
                "!larinha",
                "!letsbora",
                "!milmoedas",
                "!milpontos",
                "!moooca",
                "!moedasgratis",
                "!oncadeirinho",
                "!pontosgratis",
                "!porcorosa",
                "!pizza",
            };

            // Escolhe uma palavra aleatória da lista
            string palavraSorteada = palavras[rnd.Next(palavras.Count)];

            // Inicializa as variáveis globais alinhadas com o Compara Palavra
            CPH.SetGlobalVar("moedasSurpresaGanhadoresCount", 0, false);
            CPH.SetGlobalVar("moedasSurpresaListaGanhadores", "", false);
            CPH.SetGlobalVar("moedasSurpresaPalavra", palavraSorteada, false);

            string broadcastId = CPH.YouTubeGetMonitoredBroadcasts()?.FirstOrDefault(b => b.Title?.Contains("🔴") == true)?.Id ?? "";

            // Dispara o anúncio diretamente no chat do YouTube
            EnviarMensagem($"🎈 MOEDAS SURPRESA - Os 3 primeiros a enviarem {palavraSorteada} ganharão moedas do canal!", broadcastId, somenteNaTransmissao: true);
            LogInfo($"Evento ativo! Palavra: {palavraSorteada}");
            CPH.Wait(TempoEsperaEnvioMensagemSegundos * 1000);

            CPH.UnsetGlobalVar("moedasSurpresaPalavra", false);
            CPH.UnsetGlobalVar("moedasSurpresaGanhadoresCount", false);
            CPH.UnsetGlobalVar("moedasSurpresaListaGanhadores", false);

            LogInfo("Tempo esgotado! Variáveis limpas.");

            // Reinicia o ciclo chamando a Action atualizada, marcando como continuação
            CPH.SetGlobalVar("moedasSurpresaContinuacao", true, false);
            CPH.RunAction("Youtube Moedas Surpresa", false);

            return true;
        }
        catch (Exception ex)
        {
            // Encerra a rodada, limpa os dados e libera a trava do ciclo.
            CPH.UnsetGlobalVar("moedasSurpresaPalavra", false);
            CPH.UnsetGlobalVar("moedasSurpresaGanhadoresCount", false);
            CPH.UnsetGlobalVar("moedasSurpresaListaGanhadores", false);
            CPH.UnsetGlobalVar("moedasSurpresaAtivo", false);

            LogError("ERRO CRÍTICO: " + ex.Message);

            return false;
        }
    }

    private void EnviarMensagem(string mensagem, string broadcastId, bool somenteNaTransmissao)
    {
        // Direciona a resposta quando solicitado e há uma transmissão identificada.
        if (somenteNaTransmissao && !string.IsNullOrWhiteSpace(broadcastId))
        {
            CPH.SendYouTubeMessage(mensagem, true, true, broadcastId);
        }
        else
        {
            CPH.SendYouTubeMessage(mensagem, true, true);
        }
    }

    private void LogError(string mensagem)
    {
        CPH.LogError(Prefixo + mensagem);
    }

    private void LogInfo(string mensagem)
    {
        CPH.LogInfo(Prefixo + mensagem);
    }
}