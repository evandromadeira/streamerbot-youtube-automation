using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using Newtonsoft.Json;

// Atualização 260820.1215
public class CPHInline
{
    public bool Execute()
    {
        Evento evento = new Evento(CPH);

        Ambiente ambiente = new Ambiente();
        ambiente.PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);

        if (string.IsNullOrEmpty(ambiente.PastaRaiz))
        {
            CPH.LogError(">>> [YT DOAÇÃO] ERRO: Variável 'caminhoPastaStreamerBot' não encontrada!");
            return false;
        }

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
            if (EventoDuplicado(ambiente.CaminhoBanco, evento))
            {
                CPH.LogInfo($">>> [YT DOAÇÃO] Evento 'New Sponsor' duplicado ignorado: {evento.Usuario} / {evento.Tier}");
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
            CPH.LogWarn($">>> [YT DOAÇÃO] Tipo de ação não tratado: '{evento.TipoAcao}'. Ignorado.");
            return false;
        }

        if (pontosMeta <= 0 && moedaGanha <= 0)
        {
            CPH.LogWarn(">>> [YT DOAÇÃO] Valores calculados inválidos, ignorando.");
            return false;
        }

        // Credita moeda do usuário na tabela UserPoints (coluna "moeda") via action existente
        var payload = new
        {
            UserId = evento.UsuarioId,
            UserName = evento.Usuario,
            Timestamp = DateTime.Now,
            Origem = evento.TipoAcao,
            Pontos = moedaGanha,
            BroadcastUserId = evento.BroadcastUserId,
            BroadcastUserName = evento.BroadcastUserName
        };

        string json = JsonConvert.SerializeObject(payload);
        CPH.SetArgument("pontosPayload", json);
        CPH.RunAction("Youtube Adicionar Pontos", true); // true = aguarda conclusão antes de seguir

        // Insere a transação na tabela YoutubeDoacoes
        InserirDoacao(ambiente.CaminhoBanco, evento, pontosMeta, moedaGanha, multiplicador, valorEmBRL);

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
            CPH.LogWarn($">>> [YT DOAÇÃO] Moeda '{moeda}' sem taxa cadastrada, usando 1:1 como fallback.");
        }

        return valor * taxaCambio;
    }

    private bool EventoDuplicado(string caminhoBanco, Evento evento)
    {
        try
        {
            using (var connection = new SQLiteConnection($"Data Source={caminhoBanco};Version=3;"))
            {
                connection.Open();
                using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;", connection))
                {
                    pragmaCmd.ExecuteNonQuery();
                }

                TimeSpan janelaDedup = TimeSpan.FromSeconds(15); // Janela de tempo do Evento

                string limiteTimestamp = DateTime.Now.Subtract(janelaDedup).ToString("yyyy-MM-dd HH:mm:ss");
                string sql = @"SELECT COUNT(*) FROM YoutubeDoacoes
								WHERE userId = @userId
                                  AND broadcastUserId = @broadcastUserId
                                  AND tipoAcao = @tipoAcao
                                  AND tier = @tier
                                  AND timestamp >= @limiteTimestamp;";
                using (var cmd = new SQLiteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", evento.UsuarioId);
                    cmd.Parameters.AddWithValue("@broadcastUserId", evento.BroadcastUserId);
                    cmd.Parameters.AddWithValue("@tipoAcao", evento.TipoAcao);
                    cmd.Parameters.AddWithValue("@tier", evento.Tier ?? "");
                    cmd.Parameters.AddWithValue("@limiteTimestamp", limiteTimestamp);

                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [YT DOAÇÃO] Erro ao checar duplicidade: {ex.Message}");

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

    private void InserirDoacao(string caminhoBanco, Evento evento, int pontosMeta, int moedaGanha, int multiplicador, double valorEmBRL)
	{
		try
		{
			using (var connection = new SQLiteConnection($"Data Source={caminhoBanco};Version=3;"))
			{
				connection.Open();

				using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;", connection))
				{
					pragmaCmd.ExecuteNonQuery();
				}

				string insertSql = @"INSERT INTO YoutubeDoacoes
									(userId, userName, tipoAcao, valorOriginal, moedaOrigem, valorBRL, pontosMeta, moedaGanha, multiplicador, broadcastUserId, broadcastUserName, timestamp, tier, broadcastId, messageId)
									VALUES (@userId, @userName, @tipoAcao, @valorOriginal, @moedaOrigem, @valorBRL, @pontosMeta, @moedaGanha, @multiplicador, @broadcastUserId, @broadcastUserName, @timestamp, @tier, @broadcastId, @messageId);";

				using (var cmd = new SQLiteCommand(insertSql, connection))
				{
					cmd.Parameters.AddWithValue("@userId", evento.UsuarioId);
					cmd.Parameters.AddWithValue("@userName", evento.Usuario);
					cmd.Parameters.AddWithValue("@tipoAcao", evento.TipoAcao);
					cmd.Parameters.AddWithValue("@valorOriginal", evento.Valor);
					cmd.Parameters.AddWithValue("@moedaOrigem", evento.CurrencyCode ?? "BRL");
					cmd.Parameters.AddWithValue("@valorBRL", valorEmBRL);
					cmd.Parameters.AddWithValue("@pontosMeta", pontosMeta);
					cmd.Parameters.AddWithValue("@moedaGanha", moedaGanha);
					cmd.Parameters.AddWithValue("@multiplicador", multiplicador);
					cmd.Parameters.AddWithValue("@broadcastUserId", evento.BroadcastUserId);
					cmd.Parameters.AddWithValue("@broadcastUserName", evento.BroadcastUserName);
					cmd.Parameters.AddWithValue("@timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
					cmd.Parameters.AddWithValue("@tier", evento.Tier ?? (object)DBNull.Value);
					cmd.Parameters.AddWithValue("@broadcastId", evento.BroadcastId ?? (object)DBNull.Value);
					cmd.Parameters.AddWithValue("@messageId", evento.MessageId ?? (object)DBNull.Value);
					cmd.ExecuteNonQuery();
				}
			}
		}
		catch (Exception ex)
		{
			CPH.LogError($">>> [YT DOAÇÃO] Erro ao inserir doação: {ex.Message}");
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
            UsuarioId = IsTipLivePix ? tipUsername : usuarioId;
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
                { "Netherite", 19.99 },
                { "Suprema", 49.99 }
            };

            return precosTier.TryGetValue(tier ?? "", out double preco) ? preco : 0;
        }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }
        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");
    }
}