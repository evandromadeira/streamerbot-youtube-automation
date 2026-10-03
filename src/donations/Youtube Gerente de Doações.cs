using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// Atualização 261002.0945
// Triggers -> Source: Youtube > Chat       | Type: Super Chat       | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > Chat       | Type: Super Sticker    | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > Chat       | Type: Jewels Gifted    | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > General    | Type: New Sponsor      | Enabled: Yes | Criteria: none
//          -> Source: Youtube > Membership | Type: Member Milestone | Enabled: Yes | Criteria: none
//          -> Source: Youtube > Membership | Type: Membership Gift  | Enabled: Yes | Criteria: none
//          -> Source: StreamElements       | Type: Tip              | Enabled: Yes | Criteria: Any
// Recebe as triggers antes atribuídas a Youtube Recompensar Doações. Executar na fila Doações.
public class CPHInline
{
    public bool Execute()
    {
        Evento evento = new Evento(CPH);

        string processamentoId = Guid.NewGuid().ToString("D");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        double valorEmBRL;
        double pontosMeta;

        if (!CalcularDoacao(evento, out valorEmBRL, out pontosMeta)) return false;

        int pontosMetaInt = (int)Math.Round(pontosMeta);
        PrepararArgumentos(evento, processamentoId, timestamp, valorEmBRL, pontosMeta, pontosMetaInt);

        if ((evento.IsNewSponsor || evento.IsMemberMilestone) && EventoDuplicado(evento)) return true;

        bool sucesso = true;
        string erro;
        bool recompensou = ExecutarMetodo("Youtube Recompensar Doações", "RecompensarDoacao", out erro);
        bool creditoConfirmado = LerTexto("doacaoCreditoStatus") == "creditado";
        if (!recompensou || !creditoConfirmado)
        {
            sucesso = false;
            RegistrarFalha("moedas", LerTexto("doacaoCreditoStatus"), JuntarErro(erro, LerTexto("doacaoCreditoErro")));
            Avisar(creditoConfirmado ? "⚠ Crédito de moedas confirmado, mas houve uma falha posterior. Confira o log." : "❌ Falha técnica ao adicionar moedas.");
        }

        // Só registra como moedaGanha o crédito confirmado. A quantidade calculada fica no log de recuperação.
        CPH.TryGetArg("doacaoMoedasCalculadas", out int moedasCalculadas);
        CPH.SetArgument("doacaoMoedaGanha", creditoConfirmado ? moedasCalculadas : 0);

        bool salvou = ExecutarMetodo("Youtube Gerente de Banco de Dados", "SalvarDoacao", out erro);
        bool registroConfirmado = LerTexto("doacaoRegistroStatus") == "salvo";
        if (!salvou || !registroConfirmado)
        {
            sucesso = false;
            RegistrarFalha("registro", LerTexto("doacaoRegistroStatus"), JuntarErro(erro, LerTexto("doacaoRegistroErro")));
            Avisar(registroConfirmado ? "⚠ Doação registrada, mas houve uma falha posterior. Confira o log." : "❌ Falha técnica ao registrar a doação. Dados para conferência no log.");
        }

        CPH.SetArgument("timerUsuario", evento.Usuario);
        CPH.SetArgument("timerTipoAcao", evento.TipoAcao);
        CPH.SetArgument("timerTier", evento.Tier ?? "");
        CPH.SetArgument("timerPontosMeta", pontosMeta);

        bool adicionouTempo = ExecutarMetodo("Youtube Gerente de Timer", "AdicionarTempoPorDoacao", out erro);
        string statusTimer = LerTexto("timerDoacaoStatus");
        if (!adicionouTempo || (statusTimer != "aplicado" && statusTimer != "ignorado"))
        {
            sucesso = false;
            RegistrarFalha("timer", statusTimer, JuntarErro(erro, LerTexto("timerDoacaoErro")));
            Avisar(statusTimer == "aplicado" ? "⚠ Tempo da doação salvo, mas houve uma falha ao atualizar a exibição da Maratona. Confira o log." : "❌ Falha técnica ao processar tempo da doação.");
        }

        // Novas dinâmicas recebem os mesmos dados doacao*. A escolha dos efeitos do Minecraft será uma etapa posterior.
        if (!ExecutarMetodo("Youtube Recompensar Doações", "EnviarAgradecimento", out erro))
        {
            sucesso = false;
            RegistrarFalha("agradecimento", "incerto", erro);
        }

        CPH.SetArgument("doacaoProcessamentoSucesso", sucesso);
        // As etapas já foram tentadas uma vez. Uma falha isolada não aborta o restante nem dispara repetição.
        return true;
    }

    private bool CalcularDoacao(Evento evento, out double valorEmBRL, out double pontosMeta)
    {
        valorEmBRL = 0;
        pontosMeta = 0;

        double valorConversao;

        switch (evento.TipoAcao)
        {
            case "Super Chat":
            case "Super Sticker":
                valorConversao = 0.692307;
                break;
            case "New Sponsor":
            case "Member Milestone":
            case "Membership Gift":
                valorConversao = 0.693174;
                break;
            case "Tip":
                valorConversao = 0.923076;
                break;
            case "Jewels Gifted":
                valorConversao = 1;
                break;
            default:
                CPH.LogWarn($">>> [GERENTE_DOAÇÕES] Tipo de ação não tratado: '{evento.TipoAcao}'. Ignorado.");
                return false;
        }

        valorEmBRL = evento.IsNewSponsor || evento.IsMemberMilestone ? evento.Valor : ConverterParaBRL(evento.Valor, evento.CurrencyCode);
        pontosMeta = valorConversao * valorEmBRL * 100;
        if (string.IsNullOrWhiteSpace(evento.Usuario) || double.IsNaN(valorEmBRL) || double.IsInfinity(valorEmBRL) || valorEmBRL <= 0 || pontosMeta > int.MaxValue || double.IsNaN(pontosMeta) || double.IsInfinity(pontosMeta))
        {
            CPH.LogError($">>> [GERENTE_DOAÇÕES] Doação inválida, sem processamento: {JsonConvert.SerializeObject(new { evento, valorEmBRL, pontosMeta })}");
            return false;
        }

        return true;
    }

    private double ConverterParaBRL(double valor, string moeda)
    {
        // Taxas manuais preservadas. Ajustar periodicamente conforme cotação real.
        var taxas = new Dictionary<string, double>
        {
            { "BRL", 1.00 },
            { "USD", 5.00 },
            { "EUR", 5.80 },
            { "GBP", 6.80 }
        };
        double taxa = moeda != null && taxas.TryGetValue(moeda, out double encontrada) ? encontrada : 1.0;
        if (taxa == 1.0 && moeda != "BRL") CPH.LogWarn($">>> [GERENTE_DOAÇÕES] Moeda '{moeda}' sem taxa cadastrada, usando 1:1 como fallback.");
        return valor * taxa;
    }

    private void PrepararArgumentos(Evento evento, string processamentoId, string timestamp, double valorEmBRL, double pontosMeta, int pontosMetaInt)
    {
        CPH.SetArgument("doacaoProcessamentoId", processamentoId);
        CPH.SetArgument("doacaoTimestamp", timestamp);
        CPH.SetArgument("doacaoUserId", evento.UsuarioId);
        CPH.SetArgument("doacaoUserName", evento.Usuario);
        CPH.SetArgument("doacaoTipoAcao", evento.TipoAcao);
        CPH.SetArgument("doacaoValorOriginal", evento.Valor);
        CPH.SetArgument("doacaoMoedaOrigem", evento.CurrencyCode ?? "BRL");
        CPH.SetArgument("doacaoValorBRL", valorEmBRL);
        CPH.SetArgument("doacaoPontosMeta", pontosMetaInt);
        CPH.SetArgument("doacaoPontosMetaExatos", pontosMeta);
        CPH.SetArgument("doacaoBroadcastUserId", evento.BroadcastUserId);
        CPH.SetArgument("doacaoBroadcastUserName", evento.BroadcastUserName);
        CPH.SetArgument("doacaoTier", evento.Tier);
        CPH.SetArgument("doacaoBroadcastId", evento.BroadcastId);
        CPH.SetArgument("doacaoMessageId", evento.MessageId);
        CPH.SetArgument("doacaoQuantidadeGifts", evento.QuantidadeGifts);
        CPH.SetArgument("doacaoJewelsAmount", evento.JewelsAmount);
        CPH.SetArgument("doacaoMoedasCalculadas", 0);
        CPH.SetArgument("doacaoMoedaGanha", 0);
        CPH.SetArgument("doacaoMultiplicador", 0);
        CPH.SetArgument("doacaoCreditoStatus", "incerto");
        CPH.SetArgument("doacaoCreditoErro", "");
        CPH.SetArgument("adicionarDestinatarioId", "");
        CPH.SetArgument("doacaoRegistroStatus", "incerto");
        CPH.SetArgument("doacaoRegistroErro", "");
        CPH.SetArgument("doacaoRegistroId", 0L);
        CPH.SetArgument("timerDoacaoStatus", "incerto");
        CPH.SetArgument("timerDoacaoErro", "");
        CPH.SetArgument("timerDoacaoSegundos", 0);
        CPH.SetArgument("doacaoProcessamentoSucesso", false);
    }

    private bool EventoDuplicado(Evento evento)
    {
        CPH.SetArgument("doacaoDupUserId", evento.UsuarioId);
        CPH.SetArgument("doacaoDupBroadcastUserId", evento.BroadcastUserId);
        CPH.SetArgument("doacaoDupTipoAcao", evento.TipoAcao);
        CPH.SetArgument("doacaoDupTier", evento.Tier ?? "");
        CPH.SetArgument("doacaoDuplicada", false);
        CPH.SetArgument("doacaoDuplicidadeErro", "");
        bool consultou = ExecutarMetodo("Youtube Gerente de Banco de Dados", "VerificarDoacaoDuplicada", out string erro);
        if (!consultou || !CPH.TryGetArg("doacaoDuplicada", out bool duplicado))
        {
            RegistrarFalha("duplicidade", "consulta_falhou_continuando", JuntarErro(erro, LerTexto("doacaoDuplicidadeErro")));
            return false;
        }

        if (duplicado)
        {
            CPH.LogInfo($">>> [GERENTE_DOAÇÕES] Evento duplicado ignorado: {JsonConvert.SerializeObject(evento)}");
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

    private string JuntarErro(string chamada, string detalhe)
    {
        return string.IsNullOrEmpty(detalhe) ? (string.IsNullOrEmpty(chamada) ? "A etapa não confirmou o resultado esperado." : chamada) : chamada + " " + detalhe;
    }

    private void Avisar(string mensagem)
    {
        try
        {
            CPH.SendYouTubeMessage(mensagem);
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DOAÇÕES] Falha ao enviar aviso ao chat: {ex.Message}");
        }
    }

    private void RegistrarFalha(string etapa, string status, string erro)
    {
        // JSON em uma linha: preserva valores e nomes sem ambiguidade para correção manual; nunca executa SQL.
        var dados = new Dictionary<string, object>();
        string[] argumentos = { "doacaoProcessamentoId", "doacaoTimestamp", "doacaoUserId", "doacaoUserName", "doacaoTipoAcao", "doacaoValorOriginal", "doacaoMoedaOrigem", "doacaoValorBRL", "doacaoPontosMeta", "doacaoPontosMetaExatos", "doacaoMoedasCalculadas", "doacaoMoedaGanha", "doacaoMultiplicador", "doacaoCreditoStatus", "adicionarDestinatarioId", "doacaoBroadcastUserId", "doacaoBroadcastUserName", "doacaoTier", "doacaoBroadcastId", "doacaoMessageId", "doacaoQuantidadeGifts", "doacaoJewelsAmount", "doacaoRegistroStatus", "doacaoRegistroId", "timerDoacaoStatus", "timerDoacaoSegundos" };
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
        CPH.LogError($">>> [GERENTE_DOAÇÕES] RECUPERACAO_MANUAL {JsonConvert.SerializeObject(dados)}");
    }
    public class Evento
    {
        public bool IsJewels { get; }
        public bool IsNewSponsor { get; }
        public bool IsMemberMilestone { get; }
        public bool IsMembershipGift { get; }
        public bool IsTipLivePix { get; }

        public string Usuario { get; }
        public string UsuarioId { get; }
        public string TipoAcao { get; }
        public string CurrencyCode { get; }
        public string BroadcastId { get; }
        public string BroadcastUserId { get; }
        public string BroadcastUserName { get; }
        public string MessageId { get; }
        public string Tier { get; }

        public double Valor { get; }
        public double JewelsAmount { get; }

        public int QuantidadeGifts { get; }

        public Evento(IInlineInvokeProxy CPH)
        {
            // Campos nativos do YouTube
            CPH.TryGetArg("user", out string usuario);
            CPH.TryGetArg("userId", out string usuarioId);
            CPH.TryGetArg("microAmount", out long microAmount);
            CPH.TryGetArg("currencyCode", out string currencyCode);
            CPH.TryGetArg("broadcast.id", out string broadcastId);
            CPH.TryGetArg("broadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("broadcastUserName", out string broadcastUserName);
            CPH.TryGetArg("triggerName", out string tipoAcao);
            CPH.TryGetArg("messageId", out string messageId);

            // Campos exclusivos do Jewels Gifted
            CPH.TryGetArg("gift.jewelsAmount", out double jewelsAmount);

            // Campos exclusivos do New Sponsor (YouTube não informa valor em dinheiro, só o levelName)
            CPH.TryGetArg("levelName", out string levelName);

            // Campos exclusivos do Membership Gift (YouTube não informa valor em dinheiro, só o tier)
            CPH.TryGetArg("tier", out string tier);
            CPH.TryGetArg("count", out int count);

            // Campos exclusivos do Tip via LivePix (StreamElements)
            CPH.TryGetArg("tipUsername", out string tipUsername);
            CPH.TryGetArg("tipAmount", out double tipAmount);
            CPH.TryGetArg("tipCurrency", out string tipCurrency);

            string usuarioEmissao = CPH.GetGlobalVar<string>("usuarioEmissao", true);

            TipoAcao = tipoAcao;
            IsJewels = tipoAcao == "Jewels Gifted";
            IsNewSponsor = tipoAcao == "New Sponsor";
            IsMemberMilestone = tipoAcao == "Member Milestone";
            IsMembershipGift = tipoAcao == "Membership Gift";
            IsTipLivePix = tipoAcao == "Tip";
            JewelsAmount = jewelsAmount;
            QuantidadeGifts = count > 0 ? count : 1;
            Usuario = IsTipLivePix ? tipUsername : usuario;
            UsuarioId = IsTipLivePix ? "" : usuarioId;
            BroadcastId = broadcastId;
            BroadcastUserId = IsTipLivePix ? "" : broadcastUserId;
            BroadcastUserName = IsTipLivePix ? (string.IsNullOrEmpty(usuarioEmissao) ? "YOUTUBE" : usuarioEmissao) : (string.IsNullOrEmpty(broadcastUserName) ? "YOUTUBE" : broadcastUserName);
            MessageId = messageId;
            Tier = IsNewSponsor || IsMemberMilestone ? levelName : (IsMembershipGift ? tier : null);

            // Define o valor com base na ação correta
            if (IsJewels)
            {
                Valor = JewelsAmount / 200; // 2 Jóias = 0,01 Dólar
                CurrencyCode = "USD";
            }
            else if (IsNewSponsor || IsMemberMilestone)
            {
                Valor = ObterValorTier(Tier);
                CurrencyCode = "BRL";
            }
            else if (IsMembershipGift)
            {
                Valor = ObterValorTier(Tier) * QuantidadeGifts;
                CurrencyCode = "BRL";
            }
            else if (IsTipLivePix)
            {
                Valor = tipAmount;
                CurrencyCode = tipCurrency;
            }
            else
            {
                Valor = microAmount / 1000000.0;
                CurrencyCode = currencyCode;
            }
        }

        // Tabela de preços das membros (tiers de membership do canal).
        private static double ObterValorTier(string tier)
        {
            var precosTier = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "Tulipa Bronze", 7.99 },
                { "Tulipa Prata", 11.99 },
                { "Tulipa Ouro", 15.99 },
                { "Tulipa Platina", 23.99 },
                { "Ferro", 7.99 },
                { "Diamante", 11.99 },
                { "Netherite", 15.99 },
                { "Suprema", 23.99 }
            };

            return precosTier.TryGetValue(tier ?? "", out double preco) ? preco : 0;
        }
    }
}