using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261022.0630
// Padrões aplicados: 5
// Revisão concluída: 1
public class CPHInline
{
    private const int MultiplicadorBonus = 2;

    private const string Prefixo = "[GERENTE_SUBATHON] ";

    public bool Execute()
    {
        return GerenciarSubathon();
    }

    public bool GerenciarSubathon()
    {
        try
        {
            Contexto contexto = LerContexto();
            Timer contribuicao = contexto?.Timer ?? new Timer();
            if (contexto?.Timer != null)
            {
                // Consome a contribuição antes dos efeitos, sem permitir repetição automática.
                contexto.Timer = null;
                CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(contexto));
            }

            Ambiente ambiente = new Ambiente(CPH);
            VariaveisTimer timer = ObtemVariaveis<VariaveisTimer>(ambiente.VariaveisTimer);
            if (timer.SubathonAtivo)
            {
                VariaveisSubathon subathon = ObtemVariaveis<VariaveisSubathon>(ambiente.VariaveisSubathon);
                if (subathon.UltimaData != DateTime.Today)
                {
                    timer.MultiplicadorDeTempo = 1;
                    if (!SalvaVariaveis(timer, ambiente.VariaveisTimer))
                    {
                        return false;
                    }

                    subathon.BonusAtivo = false;
                    subathon.TempoAcumuladoHojeSeg = 0;
                    subathon.Doacao1 = subathon.Doacao2 = subathon.Doacao3 = "";
                    subathon.UltimaData = DateTime.Today;
                }

                if (contribuicao.TotalSegundos > 0)
                {
                    subathon.TempoAcumuladoHojeSeg += contribuicao.TotalSegundos;
                    subathon.Doacao3 = subathon.Doacao2;
                    subathon.Doacao2 = subathon.Doacao1;
                    subathon.Doacao1 = $"{contribuicao.Usuario} Add {FormatarTempo(contribuicao.TotalSegundos)}";
                    if (subathon.TempoAcumuladoHojeSeg >= subathon.MetaDiariaSeg && timer.MultiplicadorDeTempo == 1)
                    {
                        subathon.BonusAtivo = true;
                        timer.MultiplicadorDeTempo = MultiplicadorBonus;
                        if (!SalvaVariaveis(timer, ambiente.VariaveisTimer))
                        {
                            return false;
                        }
                    }
                }

                int tempoParaBonusSeg = subathon.MetaDiariaSeg - subathon.TempoAcumuladoHojeSeg;
                subathon.TempoParaBonus = (tempoParaBonusSeg > 0) ? $"{FormatarTempo(tempoParaBonusSeg)} para o Bônus 2x" : $"Bônus 2x Ativado!";
                if (!SalvaVariaveis(subathon, ambiente.VariaveisSubathon))
                {
                    return false;
                }

                CPH.ObsSetGdiText("Timer", "Subathon_TempoParaBonus", subathon.TempoParaBonus, 0);
                CPH.ObsSetGdiText("Timer", "Subathon_Doacao1", subathon.Doacao1.Equals("") ? "" : $"1. {subathon.Doacao1}", 0);
                CPH.ObsSetGdiText("Timer", "Subathon_Doacao2", subathon.Doacao2.Equals("") ? "" : $"2. {subathon.Doacao2}", 0);
                CPH.ObsSetGdiText("Timer", "Subathon_Doacao3", subathon.Doacao3.Equals("") ? "" : $"3. {subathon.Doacao3}", 0);
            }
        }
        catch (Exception ex)
        {
            string broadcastId = "";
            try
            {
                broadcastId = CPH.YouTubeGetMonitoredBroadcasts()?.FirstOrDefault(b => b.Title?.Contains("🔴") == true)?.Id ?? "";
            }
            catch (Exception consultaEx)
            {
                LogWarn("Não foi possível identificar a transmissão: " + consultaEx.Message);
            }

            LogError("ERRO CRÍTICO ao gerenciar subathon: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao atualizar o subathon.", broadcastId, somenteNaTransmissao: true);
            return false;
        }

        return true;
    }

    public bool SalvaVariaveis<T>(T variaveis, string caminho)
    {
        try
        {
            string json = JsonConvert.SerializeObject(variaveis, Formatting.Indented);
            File.WriteAllText(caminho, json);
            LogInfo($"Variáveis salvas em {caminho}");
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Erro ao salvar variáveis em {caminho}: {ex.Message}");
            return false;
        }
    }

    public string FormatarTempo(int totalSegundos)
    {
        TimeSpan tempo = TimeSpan.FromSeconds(totalSegundos);

        string tempoFormatado = "";
        if ((int)tempo.TotalHours > 0)
        {
            tempoFormatado += $"{(int)tempo.TotalHours:D2}h ";
        }

        if ((int)tempo.Minutes > 0)
        {
            tempoFormatado += $"{(int)tempo.Minutes:D2}m ";
        }

        if ((int)tempo.Seconds > 0)
        {
            tempoFormatado += $"{(int)tempo.Seconds:D2}s ";
        }

        return tempoFormatado.Trim();
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

    public T ObtemVariaveis<T>(string caminho)
    {
        // Interrompe o processamento se a configuração não puder ser lida.
        try
        {
            string json = File.ReadAllText(caminho);
            var variaveis = JsonConvert.DeserializeObject<T>(json);
            if (variaveis == null)
            {
                throw new InvalidDataException("O arquivo está vazio ou não contém uma configuração válida.");
            }

            return variaveis;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Erro ao carregar {caminho}: {ex.Message}", ex);
        }
    }

    private Contexto LerContexto()
    {
        // A execução automática pode não receber uma contribuição do Timer.
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrWhiteSpace(contextoJson))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class VariaveisSubathon
    {
        public bool BonusAtivo { get; set; }

        public int MetaDiariaSeg { get; set; }
        public int TempoAcumuladoHojeSeg { get; set; }

        public string Doacao1 { get; set; }
        public string Doacao2 { get; set; }
        public string Doacao3 { get; set; }
        public string TempoParaBonus { get; set; }

        public DateTime UltimaData { get; set; }
    }

    public class VariaveisTimer
    {
        public bool Ativo { get; set; }
        public bool SubathonAtivo { get; set; }

        public int MultiplicadorDeTempo { get; set; }
        public int TempoTotal { get; set; }

        public double SegundosPorPonto { get; set; }

        public DateTime TempoFinal { get; set; }
    }

    public class Contexto
    {
        public Timer Timer { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Timer
    {
        public int TotalSegundos { get; set; }

        public string Usuario { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; } = "";
        public string PastaVariaveis => Path.Combine(PastaRaiz, "Variáveis");
        public string VariaveisTimer => Path.Combine(PastaVariaveis, "Timer_Variaveis.json");
        public string VariaveisSubathon => Path.Combine(PastaVariaveis, "Subathon_Variaveis.json");

        public Ambiente()
        {
            // Permite criar o ambiente sem consultar variáveis globais.
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
        }
    }
}