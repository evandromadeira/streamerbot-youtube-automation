using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

// Versão 260903.1930
public class CPHInline
{
    public bool Execute()
    {
        try
        {
            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            Task.Run(async () =>
            {
                while (true)
                {
                    VariaveisTimer timer = ObtemVariaveis(ambiente.VariaveisTimer);

                    if (timer.SubathonAtivo && !CPH.ObsIsStreaming(0))
                    {
                        timer.TempoTotal = (int)(timer.TempoFinal - DateTime.Now).TotalSeconds;
                        timer.Ativo = false;
                        SalvaVariaveis(timer, ambiente.VariaveisTimer);
                    }

                    if (!timer.Ativo) break;

                    int restanteEmSegundos = (int)(timer.TempoFinal - DateTime.Now).TotalSeconds;

                    if (restanteEmSegundos <= 0)
                    {
                        CPH.ObsSetGdiText("Timer", "Timer SB", "ACABOU!", 0);
                        timer.TempoTotal = 0;
                        timer.Ativo = false;
                        SalvaVariaveis(timer, ambiente.VariaveisTimer);
                        break;
                    }

                    string tempoFormatado = FormatarTempo(restanteEmSegundos);
                    CPH.ObsSetGdiText("Timer", "Timer SB", tempoFormatado, 0);

                    int delay = 1000 - DateTime.Now.Millisecond;
                    await Task.Delay(delay);
                }
            }).Wait();
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [EXECUTA_TIMER] ERRO CRÍTICO no loop do timer: " + ex.Message);
            return false;
        }

        return true;
    }

    public class VariaveisTimer
    {
        public double SegundosPorPonto { get; set; }
        public int MultiplicadorDeTempo { get; set; }
        public int TempoTotal { get; set; }
        public DateTime TempoFinal { get; set; }
        public bool Ativo { get; set; }
        public bool SubathonAtivo { get; set; }
    }

    public VariaveisTimer ObtemVariaveis(string caminho)
    {
        try
        {
            if (!File.Exists(caminho))
            {
                CPH.LogInfo($"[VARIAVEIS] Arquivo não encontrado: {caminho}.");
                return new VariaveisTimer();
            }

            string jsonTimer = File.ReadAllText(caminho);
            var variaveis = JsonConvert.DeserializeObject<VariaveisTimer>(jsonTimer);

            if (variaveis == null)
            {
                CPH.LogInfo($"[VARIAVEIS] JSON vazio ou inválido: {caminho}.");
                return new VariaveisTimer();
            }

            return variaveis;
        }
        catch (Exception ex)
        {
            CPH.LogInfo($"[VARIAVEIS] Erro ao carregar {caminho}: {ex.Message}.");
            return new VariaveisTimer();
        }
    }

    public void SalvaVariaveis(VariaveisTimer timer, string caminho)
    {
        try
        {
            VariaveisTimer variaveisAtuais = ObtemVariaveis(caminho);

            variaveisAtuais.TempoTotal = timer.TempoTotal;
            variaveisAtuais.Ativo = timer.Ativo;

            string jsonTimer = JsonConvert.SerializeObject(variaveisAtuais, Formatting.Indented);
            File.WriteAllText(caminho, jsonTimer);

            CPH.LogInfo($"[VARIAVEIS] Variáveis do timer salvas em {caminho}");
        }
        catch (Exception ex)
        {
            CPH.LogInfo($"[VARIAVEIS] Erro ao salvar variáveis: {ex.Message}");
        }
    }

    public string FormatarTempo(int totalSegundos)
    {
        TimeSpan tempo = TimeSpan.FromSeconds(totalSegundos);

        return $"{(int)tempo.TotalHours:D2}:{tempo.Minutes:D2}:{tempo.Seconds:D2}";
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }

        public string PastaVariaveis => Path.Combine(PastaRaiz, "Variáveis");
        public string VariaveisTimer => Path.Combine(PastaVariaveis, "Timer_Variaveis.json");
    }
}