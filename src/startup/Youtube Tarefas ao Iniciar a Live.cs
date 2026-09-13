using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Atualização 260913.1514
// Triggers -> Source: OBS Studio          | Type: Streaming Started | Enabled: Yes | Criteria: OBS
//          -> Source: Youtube > Broadcast | Type: Broadcast Started | Enabled: Yes | Criteria: none
public class CPHInline
{
    private static readonly object inicioLiveLock = new object();

    public bool Execute()
    {
        lock (inicioLiveLock)
        {
            return ExecutarInicio();
        }
    }

    private bool ExecutarInicio()
    {
        try
        {
            var broadcasts = CPH.YouTubeGetMonitoredBroadcasts();

            if (broadcasts == null || broadcasts.Count == 0)
            {
                CPH.LogWarn(">>> [INICIO_LIVE] Nenhum broadcast do YouTube está sendo monitorado.");
                return false;
            }

            var horizontal = broadcasts
                .Where(b => !string.IsNullOrEmpty(b.Title) && b.Title.Contains("🔴"))
                .OrderByDescending(b => b.MonitoredAt)
                .FirstOrDefault();

            var vertical = broadcasts
                .Where(b => !string.IsNullOrEmpty(b.Title) && b.Title.Contains("🛑"))
                .OrderByDescending(b => b.MonitoredAt)
                .FirstOrDefault();

            if (vertical != null)
            {
                string broadcastIdVerticalAtual = CPH.GetGlobalVar<string>("youtubeBroadcastIdVertical", false);

                if (broadcastIdVerticalAtual != vertical.Id)
                {
                    CPH.SetGlobalVar("youtubeBroadcastIdVertical", vertical.Id, false);
                    CPH.LogInfo($">>> [INICIO_LIVE] Broadcast vertical registrado: '{vertical.Title}' ({vertical.Id}).");
                }
            }

            if (horizontal == null)
            {
                CPH.LogWarn(">>> [INICIO_LIVE] Nenhum broadcast horizontal com o marcador 🔴 foi encontrado.");
                return true;
            }

            string ultimoHorizontalId = CPH.GetGlobalVar<string>("ultimoBroadcastIdHorizontal", false);
            bool novaHorizontal = ultimoHorizontalId != horizontal.Id;

            CPH.SetGlobalVar("youtubeBroadcastIdHorizontal", horizontal.Id, false);

            if (novaHorizontal && vertical == null)
            {
                CPH.UnsetGlobalVar("youtubeBroadcastIdVertical", false);
                CPH.LogInfo(">>> [INICIO_LIVE] ID vertical anterior removido; nenhuma vertical foi encontrada nesta sessão.");
            }

            bool schemaOk = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "GarantirSchema");

            if (!schemaOk)
            {
                CPH.LogError(">>> [INICIO_LIVE] ERRO: falha ao garantir schema do banco de dados.");
                CPH.SendYouTubeMessage("⚠ Falha ao preparar o banco de dados. Verifique os logs antes de usar os comandos.", true, true, horizontal.Id);
                return false;
            }

            if (!novaHorizontal)
            {
                CPH.LogInfo($">>> [INICIO_LIVE] Broadcast horizontal '{horizontal.Id}' já processado; tarefas compartilhadas ignoradas.");
                return true;
            }

            CPH.LogInfo($">>> [INICIO_LIVE] Nova transmissão horizontal identificada: '{horizontal.Title}' ({horizontal.Id}).");

            bool resetOk = CPH.RunAction("Reset First Words", false);

            if (!resetOk)
            {
                CPH.LogError(">>> [INICIO_LIVE] ERRO: não foi possível iniciar a ação Reset First Words.");
                return false;
            }

            if (SubathonEstaAtivo())
            {
                CPH.UnsetGlobalVar("Subathon_Usuario", false);
                CPH.UnsetGlobalVar("Subathon_TotalSegundos", false);

                bool subathonOk = CPH.RunAction("Youtube Gerencia Subathon", true);

                if (!subathonOk)
                {
                    CPH.LogError(">>> [INICIO_LIVE] ERRO: não foi possível inicializar as variáveis do subathon.");
                    CPH.SendYouTubeMessage("⚠ Falha ao inicializar as variáveis do subathon.", true, true, horizontal.Id);
                    return false;
                }

                bool timerOk = CPH.ExecuteMethod("Youtube Gerente de Timer", "IniciarTimer");

                if (!timerOk)
                {
                    CPH.LogError(">>> [INICIO_LIVE] ERRO: não foi possível iniciar o timer.");
                    CPH.SendYouTubeMessage("⚠ Falha ao iniciar o timer.", true, true, horizontal.Id);
                    return false;
                }
            }

            bool moedasSurpresaOk = CPH.RunAction("Youtube Moedas Surpresa", false);

            if (!moedasSurpresaOk)
            {
                CPH.LogError(">>> [INICIO_LIVE] ERRO: não foi possível iniciar a ação Youtube Moedas Surpresa.");
                return false;
            }

            CPH.SetGlobalVar("ultimoBroadcastIdHorizontal", horizontal.Id, false);
            CPH.LogInfo($">>> [INICIO_LIVE] Tarefas compartilhadas concluídas para o broadcast horizontal '{horizontal.Id}'.");
            CPH.SendYouTubeMessage("Todas ações de início de live concluídas com sucesso!", true, true, horizontal.Id);

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [INICIO_LIVE] ERRO CRÍTICO: {ex.Message}");
            return false;
        }
    }

    private bool SubathonEstaAtivo()
    {
        try
        {
            Ambiente ambiente = new Ambiente(CPH);
            VariaveisTimer timer = ObterVariaveisTimer(ambiente);

            return timer?.SubathonAtivo ?? false;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [INICIO_LIVE] Erro ao verificar SubathonAtivo: {ex.Message}");
            return false;
        }
    }

    private VariaveisTimer ObterVariaveisTimer(Ambiente ambiente)
    {
        if (!File.Exists(ambiente.VariaveisTimer)) return null;

        string json = File.ReadAllText(ambiente.VariaveisTimer);
        if (string.IsNullOrEmpty(json))
            return null;

        return JsonConvert.DeserializeObject<VariaveisTimer>(json);
    }

    private class VariaveisTimer
    {
        public bool SubathonAtivo { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }

        public string PastaVariaveis => Path.Combine(PastaRaiz, "Variáveis");
        public string VariaveisTimer => Path.Combine(PastaVariaveis, "Timer_Variaveis.json");

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
        }
    }
}