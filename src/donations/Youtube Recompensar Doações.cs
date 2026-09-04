using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

// Atualização 260903.2250
public class CPHInline
{
    public bool Execute()
    {
        Evento evento = new Evento(CPH);
        Random rnd = new Random();
        
        int multiplicador = rnd.Next(50, 1001);
        int pontosMeta = 0;
        int moedaGanha = 0;
        double valorEmBRL = 0;

        // ------------------------------------------------------------------
        // Super Chat / Super Sticker
        // ------------------------------------------------------------------
        if (evento.TipoAcao == "Super Chat" || evento.TipoAcao == "Super Sticker")
        {
            double valorConversaoSuperChat = 0.7;
            valorEmBRL = ConverterParaBRL(evento.Valor, evento.CurrencyCode);
            pontosMeta = (int)Math.Round(valorConversaoSuperChat * valorEmBRL * 100);
            moedaGanha = (int)Math.Round(multiplicador * valorEmBRL * 20);
        }
        // ------------------------------------------------------------------
        // Jewels Gifted
        // ------------------------------------------------------------------
        else if (evento.TipoAcao == "Jewels Gifted")
        {
            valorEmBRL = ConverterParaBRL(evento.Valor, evento.CurrencyCode);
            pontosMeta = (int)Math.Round(valorEmBRL * 100);
            moedaGanha = (int)Math.Round(multiplicador * valorEmBRL * 20);
        }
        // ------------------------------------------------------------------
        // New Sponsor (novo membro)
        // ------------------------------------------------------------------
        else if (evento.TipoAcao == "New Sponsor" || evento.TipoAcao == "Member Milestone")
        {
            if (EventoDuplicado(evento))
            {
                CPH.LogInfo($">>> [RECOMPENSAR_DOAÇÕES] Evento 'New Sponsor' duplicado ignorado: {evento.Usuario} / {evento.Tier}");
                return true;
            }

            double valorConversaoNewSponsor = 0.7;
            valorEmBRL = evento.Valor;
            pontosMeta = (int)Math.Round(valorConversaoNewSponsor * valorEmBRL * 100);
            moedaGanha = (int)Math.Round(multiplicador * valorEmBRL * 20);
        }
        // ------------------------------------------------------------------
        // Membership Gift (presente de membership)
        // ------------------------------------------------------------------
        else if (evento.TipoAcao == "Membership Gift")
        {
            double valorConversaoMembership = 0.7;
            valorEmBRL = ConverterParaBRL(evento.Valor, evento.CurrencyCode);
            pontosMeta = (int)Math.Round(valorConversaoMembership * valorEmBRL * 100);
            moedaGanha = (int)Math.Round(multiplicador * valorEmBRL * 20);
        }
        // ------------------------------------------------------------------
        // Tip via LivePix (StreamElements)
        // ------------------------------------------------------------------
        else if (evento.TipoAcao == "Tip")
        {
            double valorConversaoLivePix = 0.95;
            valorEmBRL = ConverterParaBRL(evento.Valor, evento.CurrencyCode);
            pontosMeta = (int)Math.Round(valorConversaoLivePix * valorEmBRL * 100);
            moedaGanha = (int)Math.Round(multiplicador * valorEmBRL * 20);
        }
        else
        {
            CPH.LogWarn($">>> [RECOMPENSAR_DOAÇÕES] Tipo de ação não tratado: '{evento.TipoAcao}'. Ignorado.");
            return false;
        }

        if (pontosMeta <= 0 && moedaGanha <= 0)
        {
            CPH.LogWarn(">>> [RECOMPENSAR_DOAÇÕES] Valores calculados inválidos, ignorando.");
            return false;
        }

        CPH.SetArgument("origem", "doacao");
        CPH.SetArgument("targetUserId", evento.UsuarioId);
        CPH.SetArgument("targetUserName", evento.Usuario);
        CPH.SetArgument("coinsToAdd", moedaGanha);
        CPH.SetArgument("broadcastUserId", evento.BroadcastUserId);
        CPH.SetArgument("broadcastUserName", evento.BroadcastUserName);

        bool executou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
        if (!executou)
        {
            CPH.SendYouTubeMessage("❌ Falha técnica ao adicionar moedas.");
            return false;
        }

        if (SubathonEstaAtivo())
        {
            CPH.SetArgument("timerUsuario", evento.Usuario);
            CPH.SetArgument("timerTipoAcao", evento.TipoAcao);
            CPH.SetArgument("timerTier", evento.Tier ?? "");
            CPH.SetArgument("timerPontosMeta", pontosMeta);
            CPH.ExecuteMethod("Youtube Gerente de Timer", "AdicionarTempoPorDoacao");
        }

        // Insere a transação na tabela YoutubeDoacoes
        InserirDoacao(evento, pontosMeta, moedaGanha, multiplicador, valorEmBRL);

        // Mensagem de agradecimento no chat
        string mensagem = MontarMensagem(evento, pontosMeta, moedaGanha, multiplicador, valorEmBRL);
        if (mensagem.Length > 200)
            mensagem = mensagem.Substring(0, 197) + "...";

        CPH.SendYouTubeMessage(mensagem, true);

        return true;
    }

    private double ConverterParaBRL(double valor, string moeda)
    {
        // Taxas de conversão manuais para BRL. Ajustar periodicamente conforme cotação real.
        var taxasConversaoParaBRL = new Dictionary<string, double>
        {
            { "BRL", 1.00 },
            { "USD", 5.00 },
            { "EUR", 5.80 },
            { "GBP", 6.80 }
        };
        
        double taxaCambio = taxasConversaoParaBRL.TryGetValue(moeda, out double taxaEncontrada) ? taxaEncontrada : 1.0;
        if (taxaCambio == 1.0 && moeda != "BRL")
        {
            CPH.LogWarn($">>> [RECOMPENSAR_DOAÇÕES] Moeda '{moeda}' sem taxa cadastrada, usando 1:1 como fallback.");
        }

        return valor * taxaCambio;
    }

    private bool SubathonEstaAtivo()
    {
        try
        {
            Ambiente ambiente = new Ambiente(CPH);

            if (!File.Exists(ambiente.VariaveisTimer)) return false;

            string json = File.ReadAllText(ambiente.VariaveisTimer);
            var timer = JsonConvert.DeserializeObject<VariaveisTimer>(json);

            return timer?.SubathonAtivo ?? false;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [RECOMPENSAR_DOAÇÕES] Erro ao verificar SubathonAtivo: {ex.Message}");
            return false;
        }
    }

    private class VariaveisTimer
    {
        public bool SubathonAtivo { get; set; }
    }

    private bool EventoDuplicado(Evento evento)
    {
        try
        {
            CPH.SetArgument("doacaoDupUserId", evento.UsuarioId);
            CPH.SetArgument("doacaoDupBroadcastUserId", evento.BroadcastUserId);
            CPH.SetArgument("doacaoDupTipoAcao", evento.TipoAcao);
            CPH.SetArgument("doacaoDupTier", evento.Tier ?? "");

            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "VerificarDoacaoDuplicada");

            CPH.TryGetArg("doacaoDuplicada", out bool duplicado);

            return duplicado;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [RECOMPENSAR_DOAÇÕES] Erro ao checar duplicidade: {ex.Message}");

            return false; // falha na checagem não deve bloquear uma doação real
        }
    }

    private string MontarMensagem(Evento evento, int pontosMeta, int moedaGanha, int multiplicador, double valorEmBRL)
    {
        var (nomeEvento, artigo) = evento.TipoAcao switch
        {
            "Super Chat"        => ("Super Chat", "pelo"),
            "Super Sticker"     => ("Super Sticker", "pelo"),
            "Jewels Gifted"     => ("Joias", "pelas"),
            "New Sponsor"       => ("Novo Membro", "pelo"),
            "Member Milestone"  => ("Renovação de Assinatura", "pela"),
            "Membership Gift"   => ("Presente de Assinatura", "pelo"),
            "Tip"               => ("Contribuição", "pela"),
            _                   => ("Contribuição", "pela")
        };
        string detalheTier = (evento.IsMembershipGift || evento.IsNewSponsor) && !string.IsNullOrEmpty(evento.Tier)
            ? $" ({evento.Tier}" + (evento.QuantidadeGifts > 1
                ? $" x{evento.QuantidadeGifts})"
                : ")")
            : "";
        string simboloMoeda = ObterSimboloMoeda(evento.CurrencyCode);
        string agradecimento = evento.IsJewels
            ? $"Obrigado {artigo} {evento.JewelsAmount:N0} {nomeEvento}"
            : $"Obrigado {artigo} {nomeEvento}{detalheTier} de {simboloMoeda} {evento.Valor:F2}";
        string moedasCalculo = $"({multiplicador:N0} Multiplicador x {valorEmBRL:0.00#} x 20)";
        return $"{agradecimento}, @{evento.Usuario}! " + $"Você contribuiu com {pontosMeta:N0} Pontos para as metas " + $"e ganhou {moedaGanha:N0} Moedas! {moedasCalculo}";
    }

    private string ObterSimboloMoeda(string moeda)
    {
        var simbolosMoeda = new Dictionary<string, string>
        {
            { "BRL", "R$" },
            { "USD", "U$" },
            { "GBP", "£" },
            { "EUR", "€" }
        };
        return simbolosMoeda.TryGetValue(moeda, out string simbolo) ? simbolo : moeda; // fallback: mostra o código (ex: "JPY") se a moeda não estiver na lista
    }

    private void InserirDoacao(Evento evento, int pontosMeta, int moedaGanha, int multiplicador, double valorEmBRL)
    {
        CPH.SetArgument("doacaoUserId", evento.UsuarioId);
        CPH.SetArgument("doacaoUserName", evento.Usuario);
        CPH.SetArgument("doacaoTipoAcao", evento.TipoAcao);
        CPH.SetArgument("doacaoValorOriginal", evento.Valor);
        CPH.SetArgument("doacaoMoedaOrigem", evento.CurrencyCode ?? "BRL");
        CPH.SetArgument("doacaoValorBRL", valorEmBRL);
        CPH.SetArgument("doacaoPontosMeta", pontosMeta);
        CPH.SetArgument("doacaoMoedaGanha", moedaGanha);
        CPH.SetArgument("doacaoMultiplicador", multiplicador);
        CPH.SetArgument("doacaoBroadcastUserId", evento.BroadcastUserId);
        CPH.SetArgument("doacaoBroadcastUserName", evento.BroadcastUserName);
        CPH.SetArgument("doacaoTier", evento.Tier);
        CPH.SetArgument("doacaoBroadcastId", evento.BroadcastId);
        CPH.SetArgument("doacaoMessageId", evento.MessageId);

        bool salvou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SalvarDoacao");
        if (!salvou)
        {
            CPH.LogError($">>> [RECOMPENSAR_DOAÇÕES] Erro ao inserir doação (usuário: {evento.Usuario}).");
        }
    }

    public class Evento
    {
        public bool IsJewels { get; }
        public bool IsNewSponsor { get; }
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
            Tier = IsNewSponsor ? levelName : tier;

            // Define o valor com base na ação correta
            if (IsJewels)
            {
                Valor = JewelsAmount / 200; // 2 Jóias = 0,01 Dólar
                CurrencyCode = "USD";
            }
            else if (IsNewSponsor)
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