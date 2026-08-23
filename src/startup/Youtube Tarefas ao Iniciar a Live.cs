using System;
using System.IO;
using Newtonsoft.Json;

// Atualização 260820.2045
public class CPHInline
{
    public bool Execute()
    {
        var broadcastAtual = CPH.YouTubeGetLatestMonitoredBroadcast();

        string broadcastIdAtual = broadcastAtual?.Id;

        if (string.IsNullOrEmpty(broadcastIdAtual))
        {
            CPH.LogWarn(">>> [INICIO_LIVE] Não foi possível obter o broadcast monitorado; Ativando Moedas Surpresa sem checar duplicidade.");

            CPH.RunAction("Reset First Words", false); // Ativa a ação de resetar a primeira mensagem
            CPH.RunAction("Youtube Moedas Surpresa", false);
        }
        else
        {
            string ultimoBroadcastIdSurpresa = CPH.GetGlobalVar<string>("ultimoBroadcastId", true);

            if (ultimoBroadcastIdSurpresa != broadcastIdAtual)
            {
                CPH.RunAction("Reset First Words", false); // Ativa a ação de resetar a primeira mensagem
                CPH.RunAction("Youtube Moedas Surpresa", false); // Ativa a ação das moedas surpresa

                CPH.SetGlobalVar("ultimoBroadcastId", broadcastIdAtual, true);
            }
            else
            {
                CPH.LogDebug(">>> [INICIO_LIVE] Moedas Surpresa já ativado para esse broadcast, ignorando repetição.");
            }
        }

        bool schemaOk = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "GarantirSchema");

        if (!schemaOk)
        {
            CPH.LogError(">>> [INICIO_LIVE] ERRO: falha ao garantir schema do banco de dados.");
            CPH.SendYouTubeMessage("⚠ Falha ao preparar o banco de dados. Verifique os logs antes de usar comandos de áudio.");

            return false;
        }

        CPH.SendYouTubeMessage("Todas ações de início de live concluídas com sucesso!");
        
        return true;
    }
}