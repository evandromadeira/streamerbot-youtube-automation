using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Streamer.bot.Plugin.Interface.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261026.0940
// Padrões aplicados: 5
// Revisão concluída: 1
// Triggers -> Source: OBS Studio          | Type: Streaming Started | Enabled: Yes | Criteria: OBS
//          -> Source: Youtube > Broadcast | Type: Broadcast Started | Enabled: Yes | Criteria: none
public class CPHInline
{
    private const int MaximoTentativasBroadcast = 24;
    private const int IntervaloTentativasBroadcastMs = 5000;

    private const string Prefixo = "[TAREFAS_AO_INICIAR] ";

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
        string broadcastId = "";
        try
        {
            // Consulta imediatamente e repete a cada cinco segundos até o limite de tentativas.
            var broadcasts = default(List<YouTubeBroadcastInfo>);
            var horizontal = default(YouTubeBroadcastInfo);

            for (int tentativa = 1; horizontal == null && tentativa <= MaximoTentativasBroadcast; tentativa++)
            {
                broadcasts = CPH.YouTubeGetMonitoredBroadcasts();
                horizontal = broadcasts?
                    .Where(b => !string.IsNullOrWhiteSpace(b.Id) && !string.IsNullOrEmpty(b.Title) && b.Title.Contains("🔴"))
                    .OrderByDescending(b => b.MonitoredAt)
                    .FirstOrDefault();

                if (horizontal == null && tentativa < MaximoTentativasBroadcast)
                {
                    LogInfo($"Horizontal ainda indisponível na tentativa {tentativa}/{MaximoTentativasBroadcast}; nova consulta em {IntervaloTentativasBroadcastMs / 1000} segundos.");
                    CPH.Wait(IntervaloTentativasBroadcastMs);
                }
            }

            if (horizontal == null)
            {
                throw new InvalidOperationException($"Nenhuma transmissão horizontal com marcador 🔴 e ID válido foi encontrada após {MaximoTentativasBroadcast} consultas e {(MaximoTentativasBroadcast - 1) * IntervaloTentativasBroadcastMs / 1000} segundos de espera.");
            }

            broadcastId = horizontal.Id;

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
                    LogInfo($"Broadcast vertical registrado: '{vertical.Title}' ({vertical.Id}).");
                }
            }

            string ultimoHorizontalId = CPH.GetGlobalVar<string>("ultimoBroadcastIdHorizontal", false);
            bool novaHorizontal = ultimoHorizontalId != horizontal.Id;

            CPH.SetGlobalVar("youtubeBroadcastIdHorizontal", horizontal.Id, false);

            if (novaHorizontal && vertical == null)
            {
                CPH.UnsetGlobalVar("youtubeBroadcastIdVertical", false);
                LogInfo("ID vertical anterior removido; nenhuma vertical foi encontrada nesta sessão.");
            }

            // Youtube Gerente de Banco de Dados: prepara a estrutura do banco.
            bool schemaOk = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "GarantirSchema");
            if (!schemaOk)
            {
                LogError("ERRO: falha ao garantir schema do banco de dados.");
                EnviarMensagem("⚠ Falha ao preparar o banco de dados. Verifique os logs antes de usar os comandos.", horizontal.Id, somenteNaTransmissao: true);
                return false;
            }

            if (!novaHorizontal)
            {
                LogInfo($"Broadcast horizontal '{horizontal.Id}' já processado; tarefas compartilhadas ignoradas.");
                return true;
            }

            LogInfo($"Nova transmissão horizontal identificada: '{horizontal.Title}' ({horizontal.Id}).");

            CPH.ResetFirstWords();

            if (SubathonEstaAtivo())
            {
                // Youtube Gerente de Subathon: inicializa o estado diário e atualiza o OBS.
                bool subathonOk = CPH.ExecuteMethod("Youtube Gerente de Subathon", "GerenciarSubathon");
                if (!subathonOk)
                {
                    LogError("ERRO: não foi possível inicializar as variáveis do subathon.");
                    EnviarMensagem("⚠ Falha ao inicializar as variáveis do subathon.", horizontal.Id, somenteNaTransmissao: true);
                    return false;
                }

                // Youtube Gerente de Timer: inicia a contagem quando há tempo disponível.
                bool timerOk = CPH.ExecuteMethod("Youtube Gerente de Timer", "IniciarTimer");
                if (!timerOk)
                {
                    LogError("ERRO: não foi possível iniciar o timer.");
                    EnviarMensagem("⚠ Falha ao iniciar o timer.", horizontal.Id, somenteNaTransmissao: true);
                    return false;
                }
            }

            // Youtube Moedas Surpresa: solicita o início do ciclo pela fila da Action.
            bool moedasSurpresaOk = CPH.RunAction("Youtube Moedas Surpresa", false);
            if (!moedasSurpresaOk)
            {
                LogError("ERRO: não foi possível iniciar a ação Youtube Moedas Surpresa.");
                return false;
            }

            CPH.SetGlobalVar("ultimoBroadcastIdHorizontal", horizontal.Id, false);
            LogInfo($"Tarefas compartilhadas concluídas para o broadcast horizontal '{horizontal.Id}'.");
            EnviarMensagem("Todas ações de início de live concluídas com sucesso!", horizontal.Id, somenteNaTransmissao: true);

            return true;
        }
        catch (Exception ex)
        {
            LogError($"ERRO CRÍTICO ao executar as ações de início de live: {ex}");
            EnviarMensagem("Erro ao executar as ações de início de live. Consultar o log.", broadcastId, somenteNaTransmissao: true);

            return false;
        }
    }

    private bool SubathonEstaAtivo()
    {
        Ambiente ambiente = new Ambiente(CPH);
        VariaveisTimer timer = LerVariaveisTimer(ambiente);
        return timer.SubathonAtivo;
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

    private void LogWarn(string mensagem)
    {
        CPH.LogWarn(Prefixo + mensagem);
    }

    private void LogInfo(string mensagem)
    {
        CPH.LogInfo(Prefixo + mensagem);
    }

    private VariaveisTimer LerVariaveisTimer(Ambiente ambiente)
    {
        // Diferencia Subathon desativado de configuração ausente ou inválida.
        if (string.IsNullOrWhiteSpace(ambiente.PastaRaiz))
        {
            throw new InvalidOperationException("Variável 'caminhoPastaStreamerBot' ausente ou vazia.");
        }

        try
        {
            string json = File.ReadAllText(ambiente.VariaveisTimer);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("Arquivo vazio.");
            }

            JObject configuracao = JObject.Parse(json);
            JToken subathonAtivo = configuracao["SubathonAtivo"];
            if (subathonAtivo == null || subathonAtivo.Type != JTokenType.Boolean)
            {
                throw new InvalidDataException("Campo 'SubathonAtivo' ausente ou inválido; esperado true ou false.");
            }

            return configuracao.ToObject<VariaveisTimer>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Falha ao ler a configuração do Timer em '{ambiente.VariaveisTimer}': {ex.Message}", ex);
        }
    }

    private class VariaveisTimer
    {
        public bool SubathonAtivo { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; } = "";

        public string PastaVariaveis => Path.Combine(PastaRaiz, "Variáveis");
        public string VariaveisTimer => Path.Combine(PastaVariaveis, "Timer_Variaveis.json");

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
        }
    }
}