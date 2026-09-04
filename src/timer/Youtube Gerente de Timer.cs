using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

// Versão 260904.1055
public class CPHInline
{
    public bool AdicionarTempoPorDoacao()
    {
        try
        {
            CPH.TryGetArg("timerUsuario", out string usuario);
            CPH.TryGetArg("timerTipoAcao", out string tipoAcao);
            CPH.TryGetArg("timerTier", out string tier);
            CPH.TryGetArg("timerPontosMeta", out int pontosMeta);

            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            var timer = ObtemVariaveis<VariaveisTimer>(ambiente.VariaveisTimer);

            int totalSegundos = (int)Math.Floor(pontosMeta * timer.SegundosPorPonto * timer.MultiplicadorDeTempo);

            AtualizarTempoFinal(ambiente.VariaveisTimer, totalSegundos, timer);

            if (timer.SubathonAtivo)
            {
                ExecutarGerenciaSubathon(usuario, totalSegundos);
            }

            CPH.LogInfo($"[GERENTE_DE_TIMER] Doação processada - Usuário: {usuario} | TipoAcao: {tipoAcao} | Tier: {tier} | PontosMeta: {pontosMeta} | Segundos: {totalSegundos}");
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DE_TIMER] ERRO CRÍTICO ao adicionar tempo por doação: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao processar tempo da doação.", false);
            return false;
        }

        return true;
    }

    public bool ProcessarComando()
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                CPH.LogError(">>> [GERENTE_DE_TIMER] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            var evento = contexto.Evento;
            var ambiente = contexto.Ambiente;

            if (!evento.IsMod)
            {
                CPH.LogDebug($">>> [GERENTE_DE_TIMER] Comando !timer ignorado - usuário sem permissão: {evento.UserName}");
                return true;
            }

            string entradaUsuario = ExtrairEntradaUsuario(evento.MessageText);

            var timer = ObtemVariaveis<VariaveisTimer>(ambiente.VariaveisTimer);
            int totalSegundos = ExtrairTempoTotalEmSegundos(entradaUsuario);
            AcaoTimer acao = DetectarAcao(entradaUsuario, timer);

            switch (acao)
            {
                case AcaoTimer.Adicionar:
                    AtualizarTempoFinal(ambiente.VariaveisTimer, totalSegundos, timer);
                    break;

                case AcaoTimer.Remover:
                    AtualizarTempoFinal(ambiente.VariaveisTimer, -totalSegundos, timer);
                    break;

                case AcaoTimer.Iniciar:
                    timer.TempoFinal = DateTime.Now.AddSeconds(timer.TempoTotal);
                    timer.Ativo = true;
                    SalvaVariaveisTimer(ambiente.VariaveisTimer, timer);
                    CPH.RunAction("Youtube Executa Timer", false);
                    break;

                case AcaoTimer.Parar:
                    timer.TempoTotal = (int)(timer.TempoFinal - DateTime.Now).TotalSeconds;
                    timer.Ativo = false;
                    SalvaVariaveisTimer(ambiente.VariaveisTimer, timer);
                    break;

                case AcaoTimer.Mostrar:
                    CPH.ObsShowSource("Timer", "Timer SB", 0);
                    break;

                case AcaoTimer.Ocultar:
                    CPH.ObsHideSource("Timer", "Timer SB", 0);
                    break;

                case AcaoTimer.Subathon:
                    timer.SubathonAtivo = !timer.SubathonAtivo;
                    SalvaVariaveisTimer(ambiente.VariaveisTimer, timer);

                    if (timer.SubathonAtivo)
                    {
                        CPH.ObsShowSource("Timer", "Subathon_TempoParaBonus", 0);
                        CPH.ObsShowSource("Timer", "Subathon_Doacao1", 0);
                        CPH.ObsShowSource("Timer", "Subathon_Doacao2", 0);
                        CPH.ObsShowSource("Timer", "Subathon_Doacao3", 0);
                    }
                    else
                    {
                        CPH.ObsHideSource("Timer", "Subathon_TempoParaBonus", 0);
                        CPH.ObsHideSource("Timer", "Subathon_Doacao1", 0);
                        CPH.ObsHideSource("Timer", "Subathon_Doacao2", 0);
                        CPH.ObsHideSource("Timer", "Subathon_Doacao3", 0);
                    }
                    CPH.SendYouTubeMessage($"Timer Modo Subathon: {(timer.SubathonAtivo ? "Ativado" : "Desativado")}", false);
                    break;

                case AcaoTimer.Criar:
                    timer.TempoTotal = totalSegundos;
                    SalvaVariaveisTimer(ambiente.VariaveisTimer, timer);
                    CPH.ObsSetGdiText("Timer", "Timer SB", FormatarTempo(totalSegundos), 0);
                    CPH.ObsShowSource("Timer", "Timer SB", 0);
                    break;
            }

            CPH.LogInfo($"[GERENTE_DE_TIMER] Comando processado - Usuário: {evento.UserName} | Ação: {acao} | Entrada: {entradaUsuario}");
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DE_TIMER] ERRO CRÍTICO ao processar comando do timer: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao processar comando do timer.", false);
            return false;
        }

        return true;
    }

    private string ExtrairEntradaUsuario(string messageText)
    {
        messageText = (messageText ?? "").Trim();
        int primeiroEspaco = messageText.IndexOf(' ');

        return (primeiroEspaco >= 0 ? messageText.Substring(primeiroEspaco + 1) : "").Trim().ToLower();
    }

    public bool IniciarTimer()
    {
        try
        {
            Ambiente ambiente = new Ambiente();
            ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

            var timer = ObtemVariaveis<VariaveisTimer>(ambiente.VariaveisTimer);

            if (timer.Ativo)
            {
                CPH.LogInfo("[GERENTE_DE_TIMER] IniciarTimer ignorado: timer já está ativo.");
                return true;
            }

            if (timer.TempoTotal <= 0)
            {
                CPH.LogInfo("[GERENTE_DE_TIMER] IniciarTimer ignorado: TempoTotal <= 0.");
                return true;
            }

            timer.TempoFinal = DateTime.Now.AddSeconds(timer.TempoTotal);
            timer.Ativo = true;
            SalvaVariaveisTimer(ambiente.VariaveisTimer, timer);
            CPH.RunAction("Youtube Executa Timer", false);

            CPH.LogInfo($"[GERENTE_DE_TIMER] Timer iniciado automaticamente - TempoTotal: {timer.TempoTotal}s");
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DE_TIMER] ERRO CRÍTICO ao iniciar timer automaticamente: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao iniciar o timer automaticamente.");
            return false;
        }

        return true;
    }

    public AcaoTimer DetectarAcao(string entradaUsuario, VariaveisTimer timer)
    {
        entradaUsuario = (entradaUsuario ?? "").Trim().ToLower();

        if (ContemComando(entradaUsuario, "+", "adiciona", "soma"))
            return AcaoTimer.Adicionar;

        if (ContemComando(entradaUsuario, "-", "remove", "subtrai"))
            return AcaoTimer.Remover;

        if (ContemComando(entradaUsuario, "inicia", "começa", "retoma", "start", "recomeça"))
            return AcaoTimer.Iniciar;

        if (ContemComando(entradaUsuario, "finaliza", "termina", "suspende", "stop", "pausa", "para"))
            return AcaoTimer.Parar;

        if (ContemComando(entradaUsuario, "mostra", "exibe"))
            return AcaoTimer.Mostrar;

        if (ContemComando(entradaUsuario, "oculta", "apaga"))
            return AcaoTimer.Ocultar;

        if (ContemComando(entradaUsuario, "subathon", "maratona"))
            return AcaoTimer.Subathon;

        if (Regex.IsMatch(entradaUsuario, @"\d+[hms]"))
            return AcaoTimer.Criar;

        return AcaoTimer.Desconhecida;
    }

    public enum AcaoTimer
    {
        Adicionar,
        Remover,
        Iniciar,
        Parar,
        Mostrar,
        Ocultar,
        Subathon,
        Criar,
        Desconhecida
    }

    public static T ObtemVariaveis<T>(string caminho)
    {
        try
        {
            if (!File.Exists(caminho)) return Activator.CreateInstance<T>();

            string json = File.ReadAllText(caminho);
            var variaveis = JsonConvert.DeserializeObject<T>(json);

            if (variaveis == null) return Activator.CreateInstance<T>();

            return variaveis;
        }
        catch (Exception ex)
        {
            return Activator.CreateInstance<T>();
        }
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

    public void SalvaVariaveis<T>(string caminho, T variaveis)
    {
        try
        {
            string json = JsonConvert.SerializeObject(variaveis, Formatting.Indented);
            File.WriteAllText(caminho, json);
        }
        catch (Exception ex)
        {
            CPH.LogError($"[GERENTE_DE_TIMER] Erro ao salvar variáveis em {caminho}: {ex.Message}");
        }
    }

    public void SalvaVariaveisTimer(string caminho, VariaveisTimer timer)
    {
        try
        {
            var variaveisAtuais = ObtemVariaveis<VariaveisTimer>(caminho);

            variaveisAtuais.TempoTotal = timer.TempoTotal;
            variaveisAtuais.TempoFinal = timer.TempoFinal;
            variaveisAtuais.Ativo = timer.Ativo;
            variaveisAtuais.SubathonAtivo = timer.SubathonAtivo;

            SalvaVariaveis(caminho, variaveisAtuais);
        }
        catch (Exception ex)
        {
            CPH.LogError($"[GERENTE_DE_TIMER] Erro ao salvar Timer: {ex.Message}");
        }
    }

    public bool ContemComando(string parametro, params string[] palavrasChave)
    {
        if (string.IsNullOrEmpty(parametro) || palavrasChave == null || palavrasChave.Length == 0) return false;

        foreach (var palavra in palavrasChave)
        {
            if (string.IsNullOrEmpty(palavra)) continue;
            if (parametro.ToLower().Contains(palavra)) return true;
        }

        return false;
    }

    public int ExtrairTempoTotalEmSegundos(string entradaUsuario)
    {
        double totalSegundos = 0;
        var matches = Regex.Matches(entradaUsuario ?? "", @"(?<valor>\d+)(?<unidade>[hms])");

        foreach (Match match in matches)
        {
            int valor = int.Parse(match.Groups["valor"].Value);
            string unidade = match.Groups["unidade"].Value;

            switch (unidade)
            {
                case "h":
                    totalSegundos += valor * 3600;
                    break;
                case "m":
                    totalSegundos += valor * 60;
                    break;
                case "s":
                    totalSegundos += valor;
                    break;
            }
        }

        return (int)Math.Floor(totalSegundos);
    }

    public void AtualizarTempoFinal(string caminhoTimerVariaveis, int segundos, VariaveisTimer timer)
    {
        if (timer.Ativo)
        {
            timer.TempoFinal = timer.TempoFinal.AddSeconds(segundos);
        }
        else
        {
            timer.TempoTotal += segundos;
            CPH.ObsSetGdiText("Timer", "Timer SB", FormatarTempo(timer.TempoTotal), 0);
        }

        SalvaVariaveisTimer(caminhoTimerVariaveis, timer);
    }

    public string FormatarTempo(int totalSegundos)
    {
        TimeSpan tempo = TimeSpan.FromSeconds(totalSegundos);

        return $"{(int)tempo.TotalHours:D2}:{tempo.Minutes:D2}:{tempo.Seconds:D2}";
    }

    public void ExecutarGerenciaSubathon(string usuario, int totalSegundos)
    {
        CPH.SetGlobalVar("Subathon_Usuario", usuario, true);
        CPH.SetGlobalVar("Subathon_TotalSegundos", totalSegundos, true);

        CPH.RunAction("Youtube Gerencia Subathon", true);
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
        public Ambiente Ambiente { get; set; }
    }

    private Contexto ObterContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrEmpty(contextoJson))
            return null;

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class Evento
    {
        public bool IsMod { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }

        public string PastaVariaveis => Path.Combine(PastaRaiz, "Variáveis");
        public string VariaveisTimer => Path.Combine(PastaVariaveis, "Timer_Variaveis.json");
    }
}