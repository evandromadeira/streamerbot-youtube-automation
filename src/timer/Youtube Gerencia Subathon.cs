using System;
using System.IO;
using Newtonsoft.Json;

// Versão 260904.1050
public class CPHInline
{
    public bool Execute()
    {
        try
        {
            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            var timer = ObtemVariaveis<VariaveisTimer>(ambiente.VariaveisTimer);

            if (timer.SubathonAtivo)
            {
                var subathon = ObtemVariaveis<VariaveisSubathon>(ambiente.VariaveisSubathon);

                Evento evento = new Evento(CPH);

                if (subathon.UltimaData != DateTime.Today)
                {
                    timer.MultiplicadorDeTempo = 1;
                    SalvaVariaveis(timer, ambiente.VariaveisTimer);

                    subathon.TempoAcumuladoHojeSeg = 0;
                    subathon.UltimaData = DateTime.Today;
                    subathon.Doacao1 = subathon.Doacao2 = subathon.Doacao3 = "";
                    subathon.BonusAtivo = false;
                }

                if (evento.TotalSegundos > 0)
                {
                    subathon.TempoAcumuladoHojeSeg += evento.TotalSegundos;

                    subathon.Doacao3 = subathon.Doacao2;
                    subathon.Doacao2 = subathon.Doacao1;
                    subathon.Doacao1 = ($"{evento.Usuario} Add {FormatarTempo(evento.TotalSegundos)}");

                    if (subathon.TempoAcumuladoHojeSeg >= subathon.MetaDiariaSeg && timer.MultiplicadorDeTempo == 1)
                    {
                        subathon.BonusAtivo = true;
                        timer.MultiplicadorDeTempo = 2;
                        SalvaVariaveis(timer, ambiente.VariaveisTimer);
                    }
                }

                int TempoParaBonusSeg = subathon.MetaDiariaSeg - subathon.TempoAcumuladoHojeSeg;
                subathon.TempoParaBonus = (TempoParaBonusSeg > 0) ? ($"{FormatarTempo(TempoParaBonusSeg)} para o Bônus 2x") : ($"Bônus 2x Ativado!");
                SalvaVariaveis(subathon, ambiente.VariaveisSubathon);

                CPH.ObsSetGdiText("Timer", "Subathon_TempoParaBonus", subathon.TempoParaBonus, 0);
                CPH.ObsSetGdiText("Timer", "Subathon_Doacao1", (subathon.Doacao1.Equals("") ? "" : $"1. {subathon.Doacao1}"), 0);
                CPH.ObsSetGdiText("Timer", "Subathon_Doacao2", (subathon.Doacao2.Equals("") ? "" : $"2. {subathon.Doacao2}"), 0);
                CPH.ObsSetGdiText("Timer", "Subathon_Doacao3", (subathon.Doacao3.Equals("") ? "" : $"3. {subathon.Doacao3}"), 0);
            }
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENCIA_SUBATHON] ERRO CRÍTICO ao gerenciar subathon: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao atualizar o subathon.", false);
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

    public class VariaveisSubathon
    {
        public int MetaDiariaSeg { get; set; }
        public int TempoAcumuladoHojeSeg { get; set; }
        public DateTime UltimaData { get; set; }
        public string TempoParaBonus { get; set; }
        public string Doacao1 { get; set; }
        public string Doacao2 { get; set; }
        public string Doacao3 { get; set; }
        public bool BonusAtivo { get; set; }
    }

    public T ObtemVariaveis<T>(string caminho)
    {
        try
        {
            if (!File.Exists(caminho))
            {
                CPH.LogInfo($"[GERENCIA_SUBATHON] Arquivo não encontrado: {caminho}.");
                return Activator.CreateInstance<T>();
            }

            string json = File.ReadAllText(caminho);
            var variaveis = JsonConvert.DeserializeObject<T>(json);

            if (variaveis == null)
            {
                CPH.LogInfo($"[GERENCIA_SUBATHON] JSON vazio ou inválido: {caminho}.");
                return Activator.CreateInstance<T>();
            }

            return variaveis;
        }
        catch (Exception ex)
        {
            CPH.LogInfo($"[GERENCIA_SUBATHON] Erro ao carregar {caminho}: {ex.Message}.");
            return Activator.CreateInstance<T>();
        }
    }

    public void SalvaVariaveis<T>(T variaveis, string caminho)
    {
        try
        {
            string json = JsonConvert.SerializeObject(variaveis, Formatting.Indented);
            File.WriteAllText(caminho, json);

            CPH.LogInfo($"[GERENCIA_SUBATHON] Variáveis do subathon salvas em {caminho}");
        }
        catch (Exception ex)
        {
            CPH.LogInfo($"[GERENCIA_SUBATHON] Erro ao salvar variáveis em {caminho}: {ex.Message}");
        }
    }

    public string FormatarTempo(int totalSegundos)
    {
        TimeSpan tempo = TimeSpan.FromSeconds(totalSegundos);

        string tempoFormatado = "";

        if ((int)tempo.TotalHours > 0) tempoFormatado += ($"{(int)tempo.TotalHours:D2}h ");
        if ((int)tempo.Minutes > 0) tempoFormatado += ($"{(int)tempo.Minutes:D2}m ");
        if ((int)tempo.Seconds > 0) tempoFormatado += ($"{(int)tempo.Seconds:D2}s ");

        return tempoFormatado.Trim();
    }

    public class Evento
    {
        public string Usuario { get; }
        public int TotalSegundos { get; }

        public Evento(IInlineInvokeProxy CPH)
        {
            Usuario = CPH.GetGlobalVar<string>("Subathon_Usuario", true);
            TotalSegundos = CPH.GetGlobalVar<int>("Subathon_TotalSegundos", true);

            CPH.UnsetGlobalVar("Subathon_Usuario", true);
            CPH.UnsetGlobalVar("Subathon_TotalSegundos", true);
        }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }

        public string PastaVariaveis => Path.Combine(PastaRaiz, "Variáveis");
        public string VariaveisTimer => Path.Combine(PastaVariaveis, "Timer_Variaveis.json");
        public string VariaveisSubathon => Path.Combine(PastaVariaveis, "Subathon_Variaveis.json");
    }
}