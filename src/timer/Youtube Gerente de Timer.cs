using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261022.0630
// Padrões aplicados: 5
// Revisão concluída: 1
public class CPHInline
{
    private const int SegundosPorHora = 3600;
    private const int SegundosPorMinuto = 60;

    private const string Prefixo = "[GERENTE_DE_TIMER] ";

    public bool AdicionarTempoPorDoacao()
    {
        Contexto contexto = null;
        Doacao doacao = null;

        string status = "falhou";
        try
        {
            contexto = LerContexto();
            doacao = contexto?.Doacao;
            if (doacao == null || string.IsNullOrWhiteSpace(doacao.ProcessamentoId))
            {
                throw new ArgumentException("Contexto da doação ausente ou inválido.");
            }

            doacao.TimerStatus = "falhou";
            doacao.TimerErro = "";
            doacao.TimerSegundos = 0;
            string usuario = doacao.Usuario;
            string tipoAcao = doacao.TriggerName;
            string tier = doacao.Tier;
            if (tipoAcao == "Jewels Gifted")
            {
                doacao.TimerStatus = "ignorado";
                return true;
            }

            Ambiente ambiente = new Ambiente(CPH);
            if (string.IsNullOrWhiteSpace(ambiente.PastaRaiz))
            {
                throw new InvalidOperationException("A variável caminhoPastaStreamerBot não está configurada.");
            }

            VariaveisTimer timer = ObterTimerParaDoacao(ambiente.VariaveisTimer);
            if (timer == null || !timer.SubathonAtivo)
            {
                doacao.TimerStatus = "ignorado";
                LogInfo("Tempo da doação ignorado: configuração ausente ou modo Maratona desativado.");
                return true;
            }

            if (!doacao.PontosMetaExatos.HasValue)
            {
                throw new ArgumentException("Pontos da doação ausentes.");
            }

            double pontosMeta = doacao.PontosMetaExatos.Value;
            if (double.IsNaN(pontosMeta) || double.IsInfinity(pontosMeta) || pontosMeta < 0)
            {
                throw new InvalidOperationException("Os pontos da doação estão ausentes ou inválidos.");
            }

            if (double.IsNaN(timer.SegundosPorPonto) || double.IsInfinity(timer.SegundosPorPonto) || timer.SegundosPorPonto < 0 || timer.MultiplicadorDeTempo < 0)
            {
                throw new InvalidOperationException("A conversão de pontos em tempo está inválida.");
            }

            int totalSegundos = checked((int)Math.Round(pontosMeta * timer.SegundosPorPonto * timer.MultiplicadorDeTempo));
            doacao.TimerSegundos = totalSegundos;
            if (timer.Ativo)
            {
                timer.TempoFinal = timer.TempoFinal.AddSeconds(totalSegundos);
            }
            else
            {
                timer.TempoTotal = checked(timer.TempoTotal + totalSegundos);
            }

            string json = JsonConvert.SerializeObject(timer, Formatting.Indented);
            // Depois de iniciar a escrita, uma exceção pode deixar o arquivo parcialmente alterado.
            status = "incerto";
            doacao.TimerStatus = status;
            File.WriteAllText(ambiente.VariaveisTimer, json);
            status = "aplicado";
            doacao.TimerStatus = status;
            if (!timer.Ativo)
            {
                CPH.ObsSetGdiText("Timer", "Timer SB", FormatarTempo(timer.TempoTotal), 0);
            }

            if (!EnviarTempoSubathon(contexto, usuario, totalSegundos))
            {
                throw new InvalidOperationException("O tempo foi salvo, mas a atualização da Maratona não confirmou sucesso.");
            }

            LogInfo($"Doação processada - Usuário: {usuario} | TipoAcao: {tipoAcao} | Tier: {tier} | PontosMeta: {pontosMeta} | Segundos: {totalSegundos}");
        }
        catch (Exception ex)
        {
            if (doacao != null)
            {
                doacao.TimerStatus = status;
                doacao.TimerErro = ex.Message;
            }

            LogError("RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { Doacao = doacao, etapa = "timer", status, erro = ex.ToString(), orientacao = "Confira o arquivo do Timer antes de repetir; falha posterior não desfaz tempo salvo." }));
            return false;
        }
        finally
        {
            CPH.SetArgument("contextoJson", contexto == null ? "" : JsonConvert.SerializeObject(contexto));
        }

        return true;
    }

    public bool ProcessarComando()
    {
        Evento evento = null;
        try
        {
            Contexto contexto = LerContexto();
            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }

            evento = contexto.Evento;
            Ambiente ambiente = contexto.Ambiente;
            if (!evento.IsMod)
            {
                LogInfo($"Comando !timer ignorado - usuário sem permissão: {evento.UserName}");
                return true;
            }

            string entradaUsuario = ExtrairEntradaUsuario(evento.MessageText);

            VariaveisTimer timer = ObtemVariaveis<VariaveisTimer>(ambiente.VariaveisTimer);

            int totalSegundos = ExtrairTempoTotalEmSegundos(entradaUsuario);

            AcaoTimer acao = DetectarAcao(entradaUsuario, timer);
            switch (acao)
            {
                case AcaoTimer.Adicionar:
                    AtualizarTempoFinal(ambiente.VariaveisTimer, totalSegundos, timer, evento);
                    break;
                case AcaoTimer.Remover:
                    AtualizarTempoFinal(ambiente.VariaveisTimer, -totalSegundos, timer, evento);
                    break;
                case AcaoTimer.Iniciar:
                    timer.TempoFinal = DateTime.Now.AddSeconds(timer.TempoTotal);
                    timer.Ativo = true;
                    SalvaVariaveisTimer(ambiente.VariaveisTimer, timer, evento, "ProcessarComando");
                    CPH.RunAction("Youtube Executa Timer", false);
                    break;
                case AcaoTimer.Parar:
                    if (!timer.Ativo)
                    {
                        break;
                    }

                    timer.TempoTotal = (int)(timer.TempoFinal - DateTime.Now).TotalSeconds;
                    timer.Ativo = false;
                    SalvaVariaveisTimer(ambiente.VariaveisTimer, timer, evento, "ProcessarComando");
                    break;
                case AcaoTimer.Mostrar:
                    CPH.ObsShowSource("Timer", "Timer SB", 0);
                    break;
                case AcaoTimer.Ocultar:
                    CPH.ObsHideSource("Timer", "Timer SB", 0);
                    break;
                case AcaoTimer.Subathon:
                    timer.SubathonAtivo = !timer.SubathonAtivo;
                    SalvaVariaveisTimer(ambiente.VariaveisTimer, timer, evento, "ProcessarComando");
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

                    EnviarMensagem($"Timer Modo Subathon: {(timer.SubathonAtivo ? "Ativado" : "Desativado")}", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case AcaoTimer.Criar:
                    timer.TempoTotal = totalSegundos;
                    SalvaVariaveisTimer(ambiente.VariaveisTimer, timer, evento, "ProcessarComando");
                    CPH.ObsSetGdiText("Timer", "Timer SB", FormatarTempo(totalSegundos), 0);
                    CPH.ObsShowSource("Timer", "Timer SB", 0);
                    break;
            }

            LogInfo($"Comando processado - Usuário: {evento.UserName} | Ação: {acao} | Entrada: {entradaUsuario}");
        }
        catch (FalhaLeituraTimerException ex)
        {
            RegistrarFalhaLeituraTimer(ex, "ProcessarComando", evento, evento?.BroadcastId);
            return false;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao processar comando do timer: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao processar comando do timer.", evento?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }

        return true;
    }

    public bool IniciarTimer()
    {
        string broadcastId = "";
        try
        {
            try
            {
                broadcastId = CPH.YouTubeGetMonitoredBroadcasts()?.FirstOrDefault(b => b.Title?.Contains("🔴") == true)?.Id ?? "";
            }
            catch (Exception consultaEx)
            {
                LogWarn("Não foi possível identificar a transmissão horizontal. O início do timer continuará sem destino específico para os avisos: " + consultaEx.Message);
            }

            Ambiente ambiente = new Ambiente(CPH);
            VariaveisTimer timer = ObtemVariaveis<VariaveisTimer>(ambiente.VariaveisTimer);
            if (!timer.SubathonAtivo)
            {
                LogInfo("Início automático ignorado: modo Subathon desativado.");
                return true;
            }

            if (timer.Ativo)
            {
                LogInfo("IniciarTimer ignorado: timer já está ativo.");
                return true;
            }

            if (timer.TempoTotal <= 0)
            {
                LogInfo("IniciarTimer ignorado: TempoTotal <= 0.");
                return true;
            }

            timer.TempoFinal = DateTime.Now.AddSeconds(timer.TempoTotal);
            timer.Ativo = true;
            SalvaVariaveisTimer(ambiente.VariaveisTimer, timer, null, "IniciarTimer", broadcastId);
            CPH.RunAction("Youtube Executa Timer", false);
            LogInfo($"Timer iniciado automaticamente - TempoTotal: {timer.TempoTotal}s");
        }
        catch (FalhaLeituraTimerException ex)
        {
            RegistrarFalhaLeituraTimer(ex, "IniciarTimer", null, broadcastId);
            return false;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao iniciar timer automaticamente: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao iniciar o timer automaticamente.", broadcastId, somenteNaTransmissao: true);
            return false;
        }

        return true;
    }

    public void ExecutarGerenciaSubathon(string usuario, int totalSegundos)
    {
        Contexto contexto = LerContexto() ?? new Contexto();
        EnviarTempoSubathon(contexto, usuario, totalSegundos);
    }

    public AcaoTimer DetectarAcao(string entradaUsuario, VariaveisTimer timer)
    {
        entradaUsuario = (entradaUsuario ?? "").Trim().ToLowerInvariant();
        if (ContemComando(entradaUsuario, "+", "adiciona", "soma"))
        {
            return AcaoTimer.Adicionar;
        }

        if (ContemComando(entradaUsuario, "-", "remove", "subtrai"))
        {
            return AcaoTimer.Remover;
        }

        if (ContemComando(entradaUsuario, "inicia", "começa", "retoma", "start", "recomeça"))
        {
            return AcaoTimer.Iniciar;
        }

        if (ContemComando(entradaUsuario, "finaliza", "termina", "suspende", "stop", "pausa", "para"))
        {
            return AcaoTimer.Parar;
        }

        if (ContemComando(entradaUsuario, "mostra", "exibe"))
        {
            return AcaoTimer.Mostrar;
        }

        if (ContemComando(entradaUsuario, "oculta", "apaga"))
        {
            return AcaoTimer.Ocultar;
        }

        if (ContemComando(entradaUsuario, "subathon", "maratona"))
        {
            return AcaoTimer.Subathon;
        }

        if (Regex.IsMatch(entradaUsuario, @"\d+[hms]"))
        {
            return AcaoTimer.Criar;
        }

        return AcaoTimer.Desconhecida;
    }

    public void SalvaVariaveis<T>(string caminho, T variaveis, Evento evento = null, string operacao = "SalvaVariaveis", string broadcastId = null)
    {
        // Mantém o fluxo após falha de gravação e registra os dados para conferência manual, sem aviso no chat.
        bool escritaIniciada = false;

        string operacaoId = Guid.NewGuid().ToString("D");
        string json = null;
        try
        {
            json = JsonConvert.SerializeObject(variaveis, Formatting.Indented);
            escritaIniciada = true;
            File.WriteAllText(caminho, json);
        }
        catch (Exception ex)
        {
            LogError("RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new
            {
                operacaoId,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                operacao,
                etapa = "gravar_timer",
                userId = evento?.UserId,
                userName = evento?.UserName,
                broadcastUserId = evento?.BroadcastUserId,
                broadcastUserName = evento?.BroadcastUserName,
                broadcastId = evento?.BroadcastId ?? broadcastId,
                comando = evento?.MessageText,
                caminhoArquivo = caminho,
                valoresPretendidosJson = json,
                escritaIniciada,
                status = escritaIniciada ? "incerto" : "falhou",
                erro = ex.ToString(),
                orientacao = "O fluxo continuará normalmente. Confira o arquivo e o estado do Timer antes de corrigir; a escrita pode ter sido parcial. Não repita automaticamente o comando."
            }, Formatting.None));
        }
    }

    public void SalvaVariaveisTimer(string caminho, VariaveisTimer timer, Evento evento = null, string operacao = "SalvaVariaveisTimer", string broadcastId = null)
    {
        // Uma falha nesta segunda leitura também precisa interromper o comando.
        VariaveisTimer variaveisAtuais = ObtemVariaveis<VariaveisTimer>(caminho);
        variaveisAtuais.TempoTotal = timer.TempoTotal;
        variaveisAtuais.TempoFinal = timer.TempoFinal;
        variaveisAtuais.Ativo = timer.Ativo;
        variaveisAtuais.SubathonAtivo = timer.SubathonAtivo;
        SalvaVariaveis(caminho, variaveisAtuais, evento, operacao, broadcastId);
    }

    public bool ContemComando(string parametro, params string[] palavrasChave)
    {
        if (string.IsNullOrEmpty(parametro) || palavrasChave == null || palavrasChave.Length == 0)
        {
            return false;
        }

        foreach (var palavra in palavrasChave)
        {
            if (string.IsNullOrEmpty(palavra))
            {
                continue;
            }

            if (parametro.ToLowerInvariant().Contains(palavra))
            {
                return true;
            }
        }

        return false;
    }

    public int ExtrairTempoTotalEmSegundos(string entradaUsuario)
    {
        // Converte a duração e valida o limite antes de multiplicar ou somar.
        long totalSegundos = 0;

        var matches = Regex.Matches(entradaUsuario ?? "", @"(?<valor>\d+)(?<unidade>[hms])");
        foreach (Match match in matches)
        {
            if (!long.TryParse(match.Groups["valor"].Value, out long valor))
            {
                throw new ArgumentException("O tempo informado excede o limite permitido.");
            }

            int fator;
            switch (match.Groups["unidade"].Value)
            {
                case "h":
                    fator = SegundosPorHora;
                    break;
                case "m":
                    fator = SegundosPorMinuto;
                    break;
                default:
                    fator = 1;
                    break;
            }

            if (valor > (int.MaxValue - totalSegundos) / fator)
            {
                throw new ArgumentException("O tempo informado excede o limite permitido.");
            }

            totalSegundos += valor * fator;
        }

        return (int)totalSegundos;
    }

    public void AtualizarTempoFinal(string caminhoTimerVariaveis, int segundos, VariaveisTimer timer, Evento evento = null)
    {
        if (timer.Ativo)
        {
            timer.TempoFinal = timer.TempoFinal.AddSeconds(segundos);
        }
        else
        {
            timer.TempoTotal = checked(timer.TempoTotal + segundos);
        }

        SalvaVariaveisTimer(caminhoTimerVariaveis, timer, evento, "ProcessarComando");
        if (!timer.Ativo)
        {
            CPH.ObsSetGdiText("Timer", "Timer SB", FormatarTempo(timer.TempoTotal), 0);
        }
    }

    public string FormatarTempo(int totalSegundos)
    {
        TimeSpan tempo = TimeSpan.FromSeconds(totalSegundos);
        return $"{(int)tempo.TotalHours:D2}:{tempo.Minutes:D2}:{tempo.Seconds:D2}";
    }

    private bool EnviarTempoSubathon(Contexto contexto, string usuario, int totalSegundos)
    {
        // Youtube Gerente de Subathon: recebe a contribuição desta chamada pelo contexto.
        contexto.Timer = new Timer
        {
            TotalSegundos = totalSegundos,
            Usuario = usuario
        };
        try
        {
            CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(contexto));

            return CPH.ExecuteMethod("Youtube Gerente de Subathon", "GerenciarSubathon");
        }
        finally
        {
            // A contribuição não permanece disponível para outra chamada na mesma pilha.
            contexto.Timer = null;
            CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(contexto));
        }
    }

    private string ExtrairEntradaUsuario(string messageText)
    {
        messageText = (messageText ?? "").Trim();
        int primeiroEspaco = messageText.IndexOf(' ');
        return (primeiroEspaco >= 0 ? messageText.Substring(primeiroEspaco + 1) : "").Trim().ToLowerInvariant();
    }

    private void RegistrarFalhaLeituraTimer(FalhaLeituraTimerException erro, string operacao, Evento evento, string broadcastId)
    {
        LogError("FALHA_LEITURA " + JsonConvert.SerializeObject(new { operacaoId = Guid.NewGuid().ToString("D"), timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), operacao, acaoSolicitada = evento == null ? "Iniciar automaticamente" : DetectarAcao(ExtrairEntradaUsuario(evento.MessageText), null).ToString(), userId = evento?.UserId, userName = evento?.UserName, broadcastUserId = evento?.BroadcastUserId, broadcastUserName = evento?.BroadcastUserName, comando = evento?.MessageText, caminhoArquivo = erro.Caminho, erro = erro.ToString(), orientacao = "Operação interrompida sem gravar o arquivo ou aplicar alterações ao OBS. Verificar existência, acesso e conteúdo do JSON antes de tentar novamente." }));
        try
        {
            EnviarMensagem("⚠ Não foi possível ler a configuração do Timer. Nenhuma alteração foi feita.", broadcastId, somenteNaTransmissao: true);
        }
        catch (Exception avisoEx)
        {
            LogError("Falha ao enviar aviso de leitura: " + avisoEx);
        }
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

    private VariaveisTimer ObterTimerParaDoacao(string caminho)
    {
        try
        {
            return ObtemVariaveis<VariaveisTimer>(caminho);
        }
        catch (FalhaLeituraTimerException ex)
        {
            if (ex.InnerException is FileNotFoundException ||
                ex.InnerException is DirectoryNotFoundException)
            {
                return null;
            }

            throw;
        }
    }

    public static T ObtemVariaveis<T>(string caminho)
    {
        try
        {
            string json = File.ReadAllText(caminho);
            var objeto = JObject.Parse(json);
            if (typeof(T) == typeof(VariaveisTimer))
            {
                string[] campos =
                {
                    "SegundosPorPonto",
                    "MultiplicadorDeTempo",
                    "TempoTotal",
                    "TempoFinal",
                    "Ativo",
                    "SubathonAtivo"
                };
                foreach (string campo in campos)
                {
                    var valor = objeto.GetValue(campo, StringComparison.OrdinalIgnoreCase);
                    if (valor == null || valor.Type == JTokenType.Null)
                    {
                        throw new InvalidDataException("Configuração do Timer sem o campo obrigatório: " + campo);
                    }
                }
            }

            var variaveis = objeto.ToObject<T>();
            if (variaveis == null)
            {
                throw new InvalidDataException("Configuração do Timer vazia.");
            }

            return variaveis;
        }
        catch (Exception ex)
        {
            throw new FalhaLeituraTimerException(caminho, ex);
        }
    }

    private Contexto LerContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrEmpty(contextoJson))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
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

    public class FalhaLeituraTimerException : Exception
    {
        public string Caminho { get; private set; }

        public FalhaLeituraTimerException(string caminho, Exception erro) : base("Não foi possível ler a configuração do Timer.", erro)
        {
            Caminho = caminho;
        }
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

        public Doacao Doacao { get; set; }

        public Evento Evento { get; set; }

        public Ambiente Ambiente { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Timer
    {
        public int TotalSegundos { get; set; }

        public string Usuario { get; set; }
    }

    public class Doacao
    {
        public int TimerSegundos { get; set; }

        public double? PontosMetaExatos { get; set; }

        public string ProcessamentoId { get; set; }
        public string Usuario { get; set; }
        public string TriggerName { get; set; }
        public string Tier { get; set; }
        public string TimerStatus { get; set; }
        public string TimerErro { get; set; }

        // Preserva os resultados das outras etapas ao devolver o contexto.
        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Evento
    {
        public bool IsMod { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; } = "";
        public string PastaVariaveis => Path.Combine(PastaRaiz, "Variáveis");
        public string VariaveisTimer => Path.Combine(PastaVariaveis, "Timer_Variaveis.json");

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }

        public Ambiente()
        {
            // Construtor vazio necessário para desserialização do contexto.
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
        }
    }
}