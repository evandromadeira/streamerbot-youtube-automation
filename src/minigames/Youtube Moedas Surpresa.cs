using System;
using System.Collections.Generic;

// Atualização 260824.1135
public class CPHInline
{
    public bool Execute()
    {
        try
        {
            Random rnd = new Random();
            int tempoEsperaSegundos = rnd.Next(15, 1801);

            CPH.LogInfo($">>> [MOEDAS SURPRESA] Aguardando {tempoEsperaSegundos} segundos para lançar o evento...");
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
            CPH.SetGlobalVar("moedasSurpresaPalavra", palavraSorteada, true);
            CPH.SetGlobalVar("moedasSurpresaGanhadoresCount", 0, true);
            CPH.SetGlobalVar("moedasSurpresaListaGanhadores", "", true);

            // Dispara o anúncio diretamente no chat do YouTube
            CPH.SendYouTubeMessage($"🎈 [MOEDAS SURPRESA] Os 3 primeiros a enviarem {palavraSorteada} no chat vão ganhar Moedas!", false);
            CPH.LogInfo($">>> [MOEDAS SURPRESA] Evento ativo! Palavra: {palavraSorteada}");

            CPH.EnableAction("Youtube Compara Palavra");
            CPH.Wait(30000);
            CPH.DisableAction("Youtube Compara Palavra");

            CPH.UnsetGlobalVar("moedasSurpresaPalavra", true);
            CPH.UnsetGlobalVar("moedasSurpresaGanhadoresCount", true);
            CPH.UnsetGlobalVar("moedasSurpresaListaGanhadores", true);

            CPH.LogInfo(">>> [MOEDAS SURPRESA] Tempo esgotado! Variáveis limpas.");

            // Reinicia o ciclo chamando a Action atualizada
            CPH.RunAction("Youtube Moedas Surpresa", false);

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [MOEDAS SURPRESA] ERRO CRÍTICO: " + ex.Message);
            return false;
        }
    }
}