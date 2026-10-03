using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Versão 261003.1415
public class CPHInline
{
    public bool AdicionarTempoPorDoacao()
    {
        string status = "falhou";
        CPH.SetArgument("timerDoacaoStatus", status);
        CPH.SetArgument("timerDoacaoErro", "");
        CPH.SetArgument("timerDoacaoSegundos", 0);

        try
        {
            CPH.TryGetArg("timerUsuario", out string usuario);
            CPH.TryGetArg("timerTipoAcao", out string tipoAcao);
            CPH.TryGetArg("timerTier", out string tier);

            Ambiente ambiente = new Ambiente(CPH);
            if (string.IsNullOrWhiteSpace(ambiente.PastaRaiz))
                throw new InvalidOperationException("A variável caminhoPastaStreamerBot não está configurada.");

            var timer = ObterTimerParaDoacao(ambiente.VariaveisTimer);
            if (timer == null || !timer.SubathonAtivo)
            {
                CPH.SetArgument("timerDoacaoStatus", "ignorado");
                CPH.LogInfo("[GERENTE_DE_TIMER] Tempo da doação ignorado: configuração ausente ou modo Maratona desativado.");
                return true;
            }

            if (!CPH.TryGetArg("timerPontosMeta", out double pontosMeta) || double.IsNaN(pontosMeta) || double.IsInfinity(pontosMeta) || pontosMeta < 0)
                throw new InvalidOperationException("Os pontos da doação estão ausentes ou inválidos.");

            if (double.IsNaN(timer.SegundosPorPonto) || double.IsInfinity(timer.SegundosPorPonto) || timer.SegundosPorPonto < 0 || timer.MultiplicadorDeTempo < 0)
                throw new InvalidOperationException("A conversão de pontos em tempo está inválida.");

            int totalSegundos = checked((int)Math.Round(pontosMeta * timer.SegundosPorPonto * timer.MultiplicadorDeTempo));
            CPH.SetArgument("timerDoacaoSegundos", totalSegundos);

            if (timer.Ativo)
                timer.TempoFinal = timer.TempoFinal.AddSeconds(totalSegundos);
            else
                timer.TempoTotal = checked(timer.TempoTotal + totalSegundos);

            string json = JsonConvert.SerializeObject(timer, Formatting.Indented);
            // Depois de iniciar a escrita, uma exceção pode deixar o arquivo parcialmente alterado.
            status = "incerto";
            CPH.SetArgument("timerDoacaoStatus", status);
            File.WriteAllText(ambiente.VariaveisTimer, json);
            status = "aplicado";
            CPH.SetArgument("timerDoacaoStatus", status);

            if (!timer.Ativo)
                CPH.ObsSetGdiText("Timer", "Timer SB", FormatarTempo(timer.TempoTotal), 0);

            CPH.SetGlobalVar("Subathon_Usuario", usuario, false);
            CPH.SetGlobalVar("Subathon_TotalSegundos", totalSegundos, false);
            if (!CPH.RunAction("Youtube Gerencia Subathon", true))
                throw new InvalidOperationException("O tempo foi salvo, mas a atualização da Maratona não confirmou sucesso.");

            CPH.LogInfo($"[GERENTE_DE_TIMER] Doação processada - Usuário: {usuario} | TipoAcao: {tipoAcao} | Tier: {tier} | PontosMeta: {pontosMeta} | Segundos: {totalSegundos}");
        }
        catch (Exception ex)
        {
            CPH.SetArgument("timerDoacaoStatus", status);
            CPH.SetArgument("timerDoacaoErro", ex.Message);
            CPH.LogInfo($"[GERENTE_DE_TIMER] Falha ao processar tempo da doação | Status: {status} | Erro: {ex.Message}");
            return false;
        }

        return true;
    }

    private VariaveisTimer ObterTimerParaDoacao(string caminho)
    {
        string json;
        try
        {
            json = File.ReadAllText(caminho);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        var timer = JsonConvert.DeserializeObject<VariaveisTimer>(json);
        if (timer == null)
            throw new InvalidOperationException("O arquivo de configuração do Timer está vazio ou não contém um objeto válido.");

        return timer;
    }

    public bool ProcessarComando()
    {
        Evento evento = null;
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                CPH.LogError(">>> [GERENTE_DE_TIMER] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            evento = contexto.Evento;
            Ambiente ambiente = contexto.Ambiente;

            if (!evento.IsMod)
            {
                CPH.LogInfo($">>> [GERENTE_DE_TIMER] Comando !timer ignorado - usuário sem permissão: {evento.UserName}");
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
        catch (FalhaLeituraTimerException ex)
        {
            RegistrarFalhaLeituraTimer(ex, "ProcessarComando", evento);
            return false;
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

        return (primeiroEspaco >= 0 ? messageText.Substring(primeiroEspaco + 1) : "").Trim().ToLowerInvariant();
    }

    public bool IniciarTimer()
    {
        try
        {
            Ambiente ambiente = new Ambiente(CPH);

            var timer = ObtemVariaveis<VariaveisTimer>(ambiente.VariaveisTimer);

            if (!timer.SubathonAtivo)
            {
                CPH.LogInfo("[GERENTE_DE_TIMER] Início automático ignorado: modo Subathon desativado.");
                return true;
            }

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
        catch (FalhaLeituraTimerException ex)
        {
            RegistrarFalhaLeituraTimer(ex, "IniciarTimer", null);
            return false;
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
        entradaUsuario = (entradaUsuario ?? "").Trim().ToLowerInvariant();

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
            string json = File.ReadAllText(caminho);
            var objeto = JObject.Parse(json);
            if (typeof(T) == typeof(VariaveisTimer))
            {
                string[] campos = { "SegundosPorPonto", "MultiplicadorDeTempo", "TempoTotal", "TempoFinal", "Ativo", "SubathonAtivo" };
                foreach (string campo in campos)
                {
                    var valor = objeto.GetValue(campo, StringComparison.OrdinalIgnoreCase);
                    if (valor == null || valor.Type == JTokenType.Null)
                        throw new InvalidDataException("Configuração do Timer sem o campo obrigatório: " + campo);
                }
            }
            var variaveis = objeto.ToObject<T>();
            if (variaveis == null) throw new InvalidDataException("Configuração do Timer vazia.");
            return variaveis;
        }
        catch (Exception ex)
        {
            throw new FalhaLeituraTimerException(caminho, ex);
        }
    }

    public class FalhaLeituraTimerException : Exception
    {
        public string Caminho { get; private set; }
        public FalhaLeituraTimerException(string caminho, Exception erro) : base("Não foi possível ler a configuração do Timer.", erro)
        {
            Caminho = caminho;
        }
    }

    private void RegistrarFalhaLeituraTimer(FalhaLeituraTimerException erro, string operacao, Evento evento)
    {
        CPH.LogError(">>> [GERENTE_DE_TIMER] FALHA_LEITURA " + JsonConvert.SerializeObject(new { operacaoId = Guid.NewGuid().ToString("D"), timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), operacao, acaoSolicitada = evento == null ? "Iniciar automaticamente" : DetectarAcao(ExtrairEntradaUsuario(evento.MessageText), null).ToString(), userId = evento?.UserId, userName = evento?.UserName, broadcastUserId = evento?.BroadcastUserId, broadcastUserName = evento?.BroadcastUserName, comando = evento?.MessageText, caminhoArquivo = erro.Caminho, erro = erro.ToString(), orientacao = "Operação interrompida sem gravar o arquivo ou aplicar alterações ao OBS. Verificar existência, acesso e conteúdo do JSON antes de tentar novamente." }));
        try
        {
            CPH.SendYouTubeMessage("⚠ Não foi possível ler a configuração do Timer. Nenhuma alteração foi feita.", false);
        }
        catch (Exception avisoEx)
        {
            CPH.LogError(">>> [GERENTE_DE_TIMER] Falha ao enviar aviso de leitura: " + avisoEx);
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
        // Uma falha nesta segunda leitura também precisa interromper o comando.
        var variaveisAtuais = ObtemVariaveis<VariaveisTimer>(caminho);
        variaveisAtuais.TempoTotal = timer.TempoTotal;
        variaveisAtuais.TempoFinal = timer.TempoFinal;
        variaveisAtuais.Ativo = timer.Ativo;
        variaveisAtuais.SubathonAtivo = timer.SubathonAtivo;
        SalvaVariaveis(caminho, variaveisAtuais);
    }

    public bool ContemComando(string parametro, params string[] palavrasChave)
    {
        if (string.IsNullOrEmpty(parametro) || palavrasChave == null || palavrasChave.Length == 0) return false;

        foreach (var palavra in palavrasChave)
        {
            if (string.IsNullOrEmpty(palavra)) continue;
            if (parametro.ToLowerInvariant().Contains(palavra)) return true;
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
        }

        SalvaVariaveisTimer(caminhoTimerVariaveis, timer);
        if (!timer.Ativo) CPH.ObsSetGdiText("Timer", "Timer SB", FormatarTempo(timer.TempoTotal), 0);
    }

    public string FormatarTempo(int totalSegundos)
    {
        TimeSpan tempo = TimeSpan.FromSeconds(totalSegundos);

        return $"{(int)tempo.TotalHours:D2}:{tempo.Minutes:D2}:{tempo.Seconds:D2}";
    }

    public void ExecutarGerenciaSubathon(string usuario, int totalSegundos)
    {
        CPH.SetGlobalVar("Subathon_Usuario", usuario, false);
        CPH.SetGlobalVar("Subathon_TotalSegundos", totalSegundos, false);

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
        public string UserId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
        public bool IsMod { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; } = "";

        public string PastaVariaveis => Path.Combine(PastaRaiz, "Variáveis");
        public string VariaveisTimer => Path.Combine(PastaVariaveis, "Timer_Variaveis.json");

        // Construtor vazio necessário para desserialização do contexto.
        public Ambiente()
        {
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
        }
    }
}
