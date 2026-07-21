using System;
using System.Collections.Generic;

// Versão 260718.1602

public class CPHInline
{
    public bool Execute()
    {
        try
        {
            var usuarioEmissao = CPH.GetGlobalVar<string>("usuarioEmissao", true);
            
            string nomeMoeda = "";
            
            if (string.Equals(usuarioEmissao, "Madeira", StringComparison.OrdinalIgnoreCase))
            {
                nomeMoeda = "Gravetoins";
            }
            else if (string.Equals(usuarioEmissao, "CamposRapha", StringComparison.OrdinalIgnoreCase))
            {
                nomeMoeda = "Brotinhos";
            }
            else
            {
                nomeMoeda = "Pontos";
            }
            
            Random rnd = new Random();
            int tempoEsperaSegundos = rnd.Next(15, 1801);
            
            CPH.LogInfo($">>> [PONTOS SURPRESA] Aguardando {tempoEsperaSegundos} segundos para lançar o evento...");
            CPH.Wait(tempoEsperaSegundos * 1000);
            
            List<string> palavras = new List<string>
            {
                "!batatadoce",
                "!batatafrita",
                "!biscoito",
                "!bixcoito",
                "!bolacha",
                "!borabora",
                "!brotinhos",
                "!brotoins",
                "!cafe",
                "!campeirinho",
                "!cha",
                "!chocolate",
                "!deixaolike",
                "!fogonocampinho",
                "!gravetoins",
                "!letsbora",
                "!milpontos",
                "!oncadeirinho",
                "!pizza",
                "!pontosgratis"
            };
            
            // Escolhe uma palavra aleatória da lista
            string palavraSorteada = palavras[rnd.Next(palavras.Count)];
            
            // Inicializa as variáveis
            CPH.SetGlobalVar("pontosSurpresaPalavra", palavraSorteada, true);
            CPH.SetGlobalVar("pontosSurpresaGanhadoresCount", 0, true);
            CPH.SetGlobalVar("pontosSurpresaListaGanhadores", "", true);
            
            // Dispara o anúncio diretamente no chat do YouTube
            CPH.SendYouTubeMessage($"🎈 [PONTOS SURPRESA] Os 3 primeiros a enviarem {palavraSorteada} no chat vão ganhar {nomeMoeda}!", false);
            CPH.LogInfo($">>> [PONTOS SURPRESA] Evento ativo! Palavra: {palavraSorteada}");

            CPH.EnableAction("Youtube Compara Palavra");
            CPH.Wait(30000);
            CPH.DisableAction("Youtube Compara Palavra");

            CPH.UnsetGlobalVar("pontosSurpresaPalavra", true);
            CPH.UnsetGlobalVar("pontosSurpresaGanhadoresCount", true);
            CPH.UnsetGlobalVar("pontosSurpresaListaGanhadores", true);
            
            CPH.LogInfo(">>> [PONTOS SURPRESA] Tempo esgotado! Variáveis limpas.");
            
            // Reinicia o ciclo chamando a própria Action
            CPH.RunAction("Youtube Pontos Surpresa", false);

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [PONTOS SURPRESA] ERRO CRÍTICO: " + ex.Message);
            return false;
        }
    }
}