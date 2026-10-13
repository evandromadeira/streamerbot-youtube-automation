using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

// Atualização 261013.0750
// Triggers -> Source: Youtube > Chat       | Type: Super Chat       | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > Chat       | Type: Super Sticker    | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > Chat       | Type: Jewels Gifted    | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > General    | Type: New Sponsor      | Enabled: Yes | Criteria: none
//          -> Source: Youtube > Membership | Type: Member Milestone | Enabled: Yes | Criteria: none
//          -> Source: Youtube > Membership | Type: Membership Gift  | Enabled: Yes | Criteria: none
//          -> Source: StreamElements       | Type: Tip              | Enabled: Yes | Criteria: Any
public class CPHInline
{
    private const int PontosPorReal = 100;
    private const int JoiasPorDolar = 200;
    private const int MicroUnidadesPorUnidadeMonetaria = 1000000;

    private const double FatorPontosTip = 0.923076;
    private const double FatorPontosSuperChatSticker = 0.692307;
    private const double FatorPontosMembros = 0.693174;
    private const double FatorPontosJoias = 1;

    private const string Prefixo = "[GERENTE_DOAÇÕES] ";

    public bool Execute()
    {
        CPH.TryGetArg("triggerName", out string triggerName);
        if (string.IsNullOrWhiteSpace(triggerName))
        {
            LogError("Tipo de evento não informado.");
            return false;
        }

        Evento evento;

        switch (triggerName)
        {
            case "Tip":
                evento = LerTip();
                break;
            case "Super Chat":
            case "Super Sticker":
                evento = LerSuperChatSticker();
                break;
            case "New Sponsor":
            case "Member Milestone":
                evento = LerMembro();
                break;
            case "Membership Gift":
                evento = LerPresentesDeMembro();
                break;
            case "Jewels Gifted":
                evento = LerJoias();
                break;
            default:
                LogError($"Tipo de evento não suportado: {triggerName}.");
                return false;
        }

        if (evento == null)
        {
            return false;
        }

        var broadcaster = CPH.YouTubeGetBroadcaster();

        evento.BroadcastUserId = broadcaster?.UserId ?? "";
        evento.BroadcastUserName = string.IsNullOrEmpty(broadcaster?.UserName) ? "YOUTUBE" : broadcaster?.UserName;
        evento.TriggerName = triggerName;

        decimal valorEmBRL;

        double pontosMeta;

        string processamentoId = Guid.NewGuid().ToString("D");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (!CalcularDoacao(evento, out valorEmBRL, out pontosMeta))
        {
            return false;
        }

        int pontosMetaInt = (int)Math.Round(pontosMeta);

        PrepararArgumentos(evento, processamentoId, timestamp, valorEmBRL, pontosMeta, pontosMetaInt);

        if ((evento.IsNewSponsor || evento.IsMemberMilestone) && EventoDuplicado(evento))
        {
            return true;
        }

        // ------------------------------------------------------------------
        // Youtube Recompensar Doações: calcula a recompensa e solicita o crédito de moedas.
        // ------------------------------------------------------------------
        string erro;

        bool sucesso = true;
        bool recompensou = ExecutarMetodo("Youtube Recompensar Doações", "RecompensarDoacao", out erro);
        bool creditoConfirmado = LerTexto("doacaoCreditoStatus") == "creditado";

        if (!recompensou || !creditoConfirmado)
        {
            sucesso = false;

            RegistrarFalha("moedas", LerTexto("doacaoCreditoStatus"), ComporMensagemErro(erro, LerTexto("doacaoCreditoErro")));
            Avisar(creditoConfirmado ? "⚠ Crédito de moedas confirmado, mas houve uma falha posterior. Confira o log." : "❌ Falha técnica ao adicionar moedas.", evento.BroadcastId);
        }

        // ------------------------------------------------------------------
        // Youtube Gerente de Banco de Dados: registra a doação e as moedas confirmadas.
        // ------------------------------------------------------------------
        // Só registra como moedaGanha o crédito confirmado. A quantidade calculada fica no log de recuperação.
        CPH.TryGetArg("doacaoMoedasCalculadas", out int moedasCalculadas);

        CPH.SetArgument("doacaoMoedaGanha", creditoConfirmado ? moedasCalculadas : 0);

        bool salvou = ExecutarMetodo("Youtube Gerente de Banco de Dados", "SalvarDoacao", out erro);
        bool registroConfirmado = LerTexto("doacaoRegistroStatus") == "salvo";

        if (!salvou || !registroConfirmado)
        {
            sucesso = false;

            RegistrarFalha("registro", LerTexto("doacaoRegistroStatus"), ComporMensagemErro(erro, LerTexto("doacaoRegistroErro")));
            Avisar(registroConfirmado ? "⚠ Doação registrada, mas houve uma falha posterior. Confira o log." : "❌ Falha técnica ao registrar a doação. Dados para conferência no log.", evento.BroadcastId);
        }

        // ------------------------------------------------------------------
        // Youtube Gerente de Timer: processa o tempo da Maratona com os pontos da doação.
        // ------------------------------------------------------------------
        CPH.SetArgument("timerUsuario", evento.Usuario);
        CPH.SetArgument("timerTipoAcao", evento.TriggerName);
        CPH.SetArgument("timerTier", evento.Tier ?? "");
        CPH.SetArgument("timerPontosMeta", pontosMeta);

        bool adicionouTempo = ExecutarMetodo("Youtube Gerente de Timer", "AdicionarTempoPorDoacao", out erro);

        string statusTimer = LerTexto("timerDoacaoStatus");

        if (!adicionouTempo || (statusTimer != "aplicado" && statusTimer != "ignorado"))
        {
            sucesso = false;

            RegistrarFalha("timer", statusTimer, ComporMensagemErro(erro, LerTexto("timerDoacaoErro")));
            Avisar(statusTimer == "aplicado" ? "⚠ Tempo da doação salvo, mas houve uma falha ao atualizar a exibição da Maratona. Confira o log." : "❌ Falha técnica ao processar tempo da doação.", evento.BroadcastId);
        }

        // ------------------------------------------------------------------
        // Youtube Recompensar Doações: envia a mensagem com o resultado da recompensa.
        // ------------------------------------------------------------------
        if (!ExecutarMetodo("Youtube Recompensar Doações", "EnviarAgradecimento", out erro))
        {
            sucesso = false;
            RegistrarFalha("agradecimento", "incerto", erro);
        }

        // ------------------------------------------------------------------
        // Youtube Gerente de Minecraft: encaminha a doação quando a integração está habilitada.
        // ------------------------------------------------------------------
        bool minecraftBridgeEnabled = CPH.GetGlobalVar<bool>("minecraftYoutubeBridgeEnabled", true);
        if (minecraftBridgeEnabled && (evento.TriggerName == "Tip" || evento.TriggerName == "Super Chat" || evento.TriggerName == "Super Sticker"))
        {
            CPH.SetArgument("minecraftAceito", false);
            CPH.SetArgument("minecraftStatus", "NAO_ENVIADO");
            CPH.SetArgument("minecraftMensagem", "");

            bool encaminhou = ExecutarMetodo("Youtube Gerente de Minecraft", "ProcessarDoacao", out erro);

            CPH.TryGetArg("minecraftAceito", out bool aceito);

            if (!encaminhou || !aceito)
            {
                sucesso = false;
                RegistrarFalha("minecraft", LerTexto("minecraftStatus"), ComporMensagemErro(erro, LerTexto("minecraftMensagem")));
            }
        }

        CPH.SetArgument("doacaoProcessamentoSucesso", sucesso);

        return true;
    }

    private Evento LerTip()
    {
        CPH.TryGetArg("tipAmount", out double tipAmount);
        CPH.TryGetArg("tipUsername", out string tipUsername);
        CPH.TryGetArg("tipCurrency", out string tipCurrency);
        CPH.TryGetArg("messageId", out string messageId);
        CPH.TryGetArg("tipMessage", out string tipMessage);

        if (!TentarConverterValorMonetario(tipAmount, "tipAmount", out decimal valor))
        {
            return null;
        }

        string broadcastId = CPH.YouTubeGetMonitoredBroadcasts()?.FirstOrDefault(b => b.Title?.Contains("🔴") == true)?.Id ?? "";

        Evento evento = new Evento
        {
            Valor = valor,
            UsuarioId = "",
            Usuario = tipUsername ?? "",
            CurrencyCode = tipCurrency?.Trim().ToUpperInvariant(),
            MessageId = messageId ?? "",
            Message = tipMessage ?? "",
            BroadcastId = broadcastId,
        };

        return evento;
    }

    private Evento LerSuperChatSticker()
    {
        Evento evento = LerEventoYoutube();

        CPH.TryGetArg("microAmount", out long microAmount);
        CPH.TryGetArg("currencyCode", out string currencyCode);

        evento.Valor = microAmount / (decimal)MicroUnidadesPorUnidadeMonetaria;
        evento.CurrencyCode = currencyCode?.Trim().ToUpperInvariant();

        return evento;
    }

    private Evento LerMembro()
    {
        Evento evento = LerEventoYoutube();

        CPH.TryGetArg("months", out int months);
        CPH.TryGetArg("levelName", out string levelName);

        evento.Months = months > 0 ? months : 1;
        evento.Tier = levelName;
        evento.Valor = ObterValorTier(levelName);
        evento.CurrencyCode = "BRL";

        return evento;
    }

    private Evento LerPresentesDeMembro()
    {
        Evento evento = LerEventoYoutube();

        CPH.TryGetArg("count", out int count);
        CPH.TryGetArg("tier", out string tier);

        evento.QuantidadeGifts = count > 0 ? count : 1;
        evento.Tier = tier;
        evento.Valor = ObterValorTier(tier) * evento.QuantidadeGifts;
        evento.CurrencyCode = "BRL";

        return evento;
    }

    private Evento LerJoias()
    {
        Evento evento = LerEventoYoutube();

        CPH.TryGetArg("gift.jewelsAmount", out double jewelsAmount);
        if (!TentarConverterValorMonetario(jewelsAmount, "gift.jewelsAmount", out decimal quantidadeJoias))
        {
            return null;
        }

        evento.JewelsAmount = jewelsAmount;
        evento.Valor = quantidadeJoias / JoiasPorDolar; // 2 Joias = 0,01 Dólar.
        evento.CurrencyCode = "USD";

        return evento;
    }

    private Evento LerEventoYoutube()
    {
        // Identificação compartilhada pelos eventos nativos do YouTube.
        CPH.TryGetArg("userId", out string usuarioId);
        CPH.TryGetArg("user", out string usuario);
        CPH.TryGetArg("messageId", out string messageId);
        CPH.TryGetArg("broadcast.id", out string broadcastId);

        Evento evento = new Evento
        {
            UsuarioId = usuarioId,
            Usuario = usuario,
            MessageId = messageId,
            BroadcastId = broadcastId,
        };

        return evento;
    }

    private bool TentarConverterValorMonetario(double valor, string argumento, out decimal valorMonetario)
    {
        valorMonetario = 0m;

        try
        {
            valorMonetario = Convert.ToDecimal(valor);
            return true;
        }
        catch (OverflowException)
        {
            LogError($"Valor inválido no argumento '{argumento}', sem processamento: {valor}.");
            return false;
        }
    }

    private static decimal ObterValorTier(string tier)
    {
        var precosTier = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            { "Tulipa Bronze", 7.99m },
            { "Tulipa Prata", 11.99m },
            { "Tulipa Ouro", 15.99m },
            { "Tulipa Platina", 23.99m },
            { "Ferro", 7.99m },
            { "Diamante", 11.99m },
            { "Netherite", 15.99m },
            { "Suprema", 23.99m }
        };

        return precosTier.TryGetValue(tier ?? "", out decimal preco) ? preco : 0m;
    }

    private bool CalcularDoacao(Evento evento, out decimal valorEmBRL, out double pontosMeta)
    {
        valorEmBRL = 0m;
        pontosMeta = 0;

        double valorConversao;

        switch (evento.TriggerName)
        {
            case "Tip":
                valorConversao = FatorPontosTip;
                break;
            case "Super Chat":
            case "Super Sticker":
                valorConversao = FatorPontosSuperChatSticker;
                break;
            case "New Sponsor":
            case "Member Milestone":
            case "Membership Gift":
                valorConversao = FatorPontosMembros;
                break;
            case "Jewels Gifted":
                valorConversao = FatorPontosJoias;
                break;
            default:
                LogWarn($"Tipo de ação não tratado: '{evento.TriggerName}'. Ignorado.");
                return false;
        }

        try
        {
            valorEmBRL = evento.IsNewSponsor || evento.IsMemberMilestone || evento.IsMembershipGift ? evento.Valor : ConverterParaBRL(evento.Valor, evento.CurrencyCode);
        }
        catch (OverflowException ex)
        {
            LogError($"Valor da doação fora do intervalo monetário, sem processamento: {JsonConvert.SerializeObject(evento)}. {ex.Message}");
            return false;
        }

        // Pontos permanecem double para preservar a fórmula e o contrato com o Timer.
        pontosMeta = valorConversao * (double)valorEmBRL * PontosPorReal;

        if (string.IsNullOrWhiteSpace(evento.Usuario) || valorEmBRL <= 0m || pontosMeta > int.MaxValue || double.IsNaN(pontosMeta) || double.IsInfinity(pontosMeta))
        {
            LogError($"Doação inválida, sem processamento: {JsonConvert.SerializeObject(new { evento, valorEmBRL, pontosMeta })}");
            return false;
        }

        return true;
    }

    private decimal ConverterParaBRL(decimal valor, string moeda)
    {
        var taxas = new Dictionary<string, decimal>
        {
            { "ARS", 0.003277m },
            { "BOB", 0.451025m },
            { "BRL", 1.000000m },
            { "CLP", 0.005078m },
            { "COP", 0.001539m },
            { "CRC", 0.010901m },
            { "CZK", 0.228564m },
            { "DKK", 0.745482m },
            { "DOP", 0.082090m },
            { "EGP", 0.094834m },
            { "EUR", 5.550000m },
            { "GBP", 6.550000m },
            { "GTQ", 0.649692m },
            { "HKD", 0.633195m },
            { "HNL", 0.184626m },
            { "HUF", 0.015244m },
            { "INR", 0.051321m },
            { "ISK", 0.040730m },
            { "JPY", 0.031439m },
            { "KRW", 0.003697m },
            { "MKD", 0.090480m },
            { "MXN", 0.273149m },
            { "NIO", 0.134916m },
            { "NOK", 0.519521m },
            { "PHP", 0.078935m },
            { "RSD", 0.047474m },
            { "RUB", 0.058453m },
            { "SEK", 0.498464m },
            { "TRY", 0.100698m },
            { "TWD", 0.155577m },
            { "UGX", 0.001213m },
            { "USD", 4.950000m },
            { "UYU", 0.123711m },
            { "ZAR", 0.299546m }
        };

        decimal taxa = moeda != null && taxas.TryGetValue(moeda, out decimal encontrada) ? encontrada : 1m;
        if (taxa == 1m && moeda != "BRL")
        {
            LogWarn($"Moeda '{moeda}' sem taxa cadastrada, usando 1:1 como fallback.");
        }

        return valor * taxa;
    }

    private void PrepararArgumentos(Evento evento, string processamentoId, string timestamp, decimal valorEmBRL, double pontosMeta, int pontosMetaInt)
    {
        // Os destinatários atuais ainda recebem os valores monetários como double.
        CPH.SetArgument("doacaoMonths", evento.Months);
        CPH.SetArgument("doacaoQuantidadeGifts", evento.QuantidadeGifts);
        CPH.SetArgument("doacaoPontosMeta", pontosMetaInt);

        CPH.SetArgument("doacaoJewelsAmount", evento.JewelsAmount);
        CPH.SetArgument("doacaoPontosMetaExatos", pontosMeta);

        CPH.SetArgument("doacaoValorOriginal", (double)evento.Valor);
        CPH.SetArgument("doacaoValorBRL", (double)valorEmBRL);

        CPH.SetArgument("doacaoProcessamentoId", processamentoId);
        CPH.SetArgument("doacaoTimestamp", timestamp);
        CPH.SetArgument("doacaoTipoAcao", evento.TriggerName);
        CPH.SetArgument("doacaoUserId", evento.UsuarioId);
        CPH.SetArgument("doacaoUserName", evento.Usuario);
        CPH.SetArgument("doacaoMoedaOrigem", evento.CurrencyCode ?? "BRL");
        CPH.SetArgument("doacaoMessageId", evento.MessageId);
        CPH.SetArgument("doacaoMessage", evento.Message);
        CPH.SetArgument("doacaoTier", evento.Tier);
        CPH.SetArgument("doacaoBroadcastId", evento.BroadcastId);
        CPH.SetArgument("doacaoBroadcastUserId", evento.BroadcastUserId);
        CPH.SetArgument("doacaoBroadcastUserName", evento.BroadcastUserName);

        CPH.SetArgument("doacaoMoedasCalculadas", 0);
        CPH.SetArgument("doacaoMoedaGanha", 0);
        CPH.SetArgument("doacaoMultiplicador", 0);
        CPH.SetArgument("timerDoacaoSegundos", 0);

        CPH.SetArgument("doacaoRegistroId", 0L);

        CPH.SetArgument("doacaoProcessamentoSucesso", false);

        CPH.SetArgument("doacaoCreditoStatus", "incerto");
        CPH.SetArgument("doacaoCreditoErro", "");
        CPH.SetArgument("doacaoRegistroStatus", "incerto");
        CPH.SetArgument("doacaoRegistroErro", "");
        CPH.SetArgument("timerDoacaoStatus", "incerto");
        CPH.SetArgument("timerDoacaoErro", "");

        CPH.SetArgument("adicionarDestinatarioId", "");

    }

    private bool EventoDuplicado(Evento evento)
    {
        CPH.SetArgument("doacaoDupUserId", evento.UsuarioId);
        CPH.SetArgument("doacaoDupBroadcastUserId", evento.BroadcastUserId);
        CPH.SetArgument("doacaoDupTipoAcao", evento.TriggerName);
        CPH.SetArgument("doacaoDupTier", evento.Tier ?? "");
        CPH.SetArgument("doacaoDuplicada", false);
        CPH.SetArgument("doacaoDuplicidadeErro", "");

        bool consultou = ExecutarMetodo("Youtube Gerente de Banco de Dados", "VerificarDoacaoDuplicada", out string erro);

        if (!consultou || !CPH.TryGetArg("doacaoDuplicada", out bool duplicado))
        {
            RegistrarFalha("duplicidade", "consulta_falhou_continuando", ComporMensagemErro(erro, LerTexto("doacaoDuplicidadeErro")));
            return false;
        }

        if (duplicado)
        {
            LogInfo($"Evento duplicado ignorado: {JsonConvert.SerializeObject(evento)}");
            CPH.SetArgument("doacaoProcessamentoSucesso", true);
        }
        return duplicado;
    }

    private bool ExecutarMetodo(string action, string metodo, out string erro)
    {
        try
        {
            bool resultado = CPH.ExecuteMethod(action, metodo);
            erro = resultado ? "" : $"{action}.{metodo} retornou false.";
            return resultado;
        }
        catch (Exception ex)
        {
            erro = $"{action}.{metodo}: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private string LerTexto(string argumento)
    {
        CPH.TryGetArg(argumento, out string valor);
        return valor ?? "";
    }

    private string ComporMensagemErro(string chamada, string detalhe)
    {
        return string.IsNullOrEmpty(detalhe) ? (string.IsNullOrEmpty(chamada) ? "A etapa não confirmou o resultado esperado." : chamada) : chamada + " " + detalhe;
    }

    private void Avisar(string mensagem, string broadcastId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(broadcastId))
            {
                CPH.SendYouTubeMessage(mensagem, false, true);
            }
            else
            {
                CPH.SendYouTubeMessage(mensagem, false, true, broadcastId);
            }
        }
        catch (Exception ex)
        {
            LogError($"Falha ao enviar aviso ao chat: {ex.Message}");
        }
    }

    private void RegistrarFalha(string etapa, string status, string erro)
    {
        // JSON em uma linha: preserva valores e nomes sem ambiguidade para correção manual; nunca executa SQL.
        var dados = new Dictionary<string, object>();

        string[] argumentos = { "doacaoProcessamentoId", "doacaoTimestamp", "doacaoUserId", "doacaoUserName", "doacaoTipoAcao", "doacaoValorOriginal", "doacaoMoedaOrigem", "doacaoValorBRL", "doacaoPontosMeta", "doacaoPontosMetaExatos", "doacaoMoedasCalculadas", "doacaoMoedaGanha", "doacaoMultiplicador", "doacaoCreditoStatus", "adicionarDestinatarioId", "adicionarOperacaoId", "adicionarBancoInicioId", "adicionarBancoRespostaId", "adicionarBancoConcluido", "adicionarResultado", "adicionarErro", "doacaoBroadcastUserId", "doacaoBroadcastUserName", "doacaoTier", "doacaoBroadcastId", "doacaoMessageId", "doacaoMonths", "doacaoQuantidadeGifts", "doacaoJewelsAmount", "doacaoRegistroStatus", "doacaoRegistroId", "timerDoacaoStatus", "timerDoacaoSegundos" };
        foreach (string argumento in argumentos)
        {
            CPH.TryGetArg(argumento, out object valor);
            dados[argumento] = valor;
        }

        dados["pastaStreamerBot"] = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);
        dados["etapa"] = etapa;
        dados["status"] = status;
        dados["erro"] = erro;
        dados["orientacao"] = "Sem repetição automática. Confira o banco/timer antes da recuperação manual se o resultado for incerto. doacaoMoedasCalculadas é o crédito pretendido; doacaoMoedaGanha é apenas o confirmado. Use origem=doacao, usuário/canal e a quantidade original ao recuperar moedas, sem novo sorteio.";

        LogError($"RECUPERACAO_MANUAL {JsonConvert.SerializeObject(dados)}");
    }

    private void LogInfo(string mensagem)
    {
        CPH.LogInfo(Prefixo + mensagem);
    }

    private void LogWarn(string mensagem)
    {
        CPH.LogWarn(Prefixo + mensagem);
    }

    private void LogError(string mensagem)
    {
        CPH.LogError(Prefixo + mensagem);
    }

    public class Evento
    {
        public bool IsTip => TriggerName == "Tip";
        public bool IsNewSponsor => TriggerName == "New Sponsor";
        public bool IsMemberMilestone => TriggerName == "Member Milestone";
        public bool IsMembershipGift => TriggerName == "Membership Gift";
        public bool IsJewels => TriggerName == "Jewels Gifted";

        public int Months { get; set; }
        public int QuantidadeGifts { get; set; } = 1;

        public decimal Valor { get; set; }

        public double JewelsAmount { get; set; }

        public string TriggerName { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string CurrencyCode { get; set; }
        public string MessageId { get; set; }
        public string Message { get; set; } = "";
        public string Tier { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }

    public class Doacao
    {
        // Gerente de Doações: identificação e controle do processamento.
        [JsonIgnore]
        public bool IsTip => TriggerName == "Tip";
        [JsonIgnore]
        public bool IsNewSponsor => TriggerName == "New Sponsor";
        [JsonIgnore]
        public bool IsMemberMilestone => TriggerName == "Member Milestone";
        [JsonIgnore]
        public bool IsMembershipGift => TriggerName == "Membership Gift";
        [JsonIgnore]
        public bool IsJewels => TriggerName == "Jewels Gifted";

        public bool ProcessamentoSucesso { get; set; }

        public string ProcessamentoId { get; set; }
        public string Timestamp { get; set; }
        public string TriggerName { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string MessageId { get; set; }
        public string Message { get; set; } = "";
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }

        // Gerente de Doações: valores e características da doação.
        public int Months { get; set; }
        public int QuantidadeGifts { get; set; } = 1;
        public int PontosMeta { get; set; }

        public decimal ValorOriginal { get; set; }
        public decimal ValorBRL { get; set; }

        public double JewelsAmount { get; set; }
        public double PontosMetaExatos { get; set; }

        public string CurrencyCode { get; set; }
        public string Tier { get; set; }

        // Recompensar Doações: moedas calculadas e resultado do crédito.
        public int MoedasCalculadas { get; set; }
        public int MoedaGanha { get; set; }
        public int Multiplicador { get; set; }

        public string CreditoStatus { get; set; } = "incerto";
        public string CreditoErro { get; set; } = "";
        public string DestinatarioId { get; set; } = "";

        // Gerente de Banco de Dados: resultado da gravação da doação.
        public long RegistroId { get; set; }

        public string RegistroStatus { get; set; } = "incerto";
        public string RegistroErro { get; set; } = "";

        // Gerente de Timer: tempo adicionado e resultado do processamento.
        public int TimerSegundos { get; set; }

        public string TimerStatus { get; set; } = "incerto";
        public string TimerErro { get; set; } = "";
    }
}