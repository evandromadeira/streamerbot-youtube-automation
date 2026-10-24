using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261022.0630
// Padrões aplicados: 5
// Revisão concluída: 1
// Triggers -> Source: Youtube > Chat       | Type: Super Chat       | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > Chat       | Type: Super Sticker    | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > Chat       | Type: Jewels Gifted    | Enabled: Yes | Criteria: Any
//          -> Source: Youtube > General    | Type: New Sponsor      | Enabled: Yes | Criteria: none
//          -> Source: Youtube > Membership | Type: Member Milestone | Enabled: Yes | Criteria: none
//          -> Source: Youtube > Membership | Type: Membership Gift  | Enabled: Yes | Criteria: none
//          -> Source: StreamElements       | Type: Tip              | Enabled: Yes | Criteria: Any
public class CPHInline
{
    private const int LimiteTentativasRegistro = 2;
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
        Doacao doacao = LerEvento();
        if (doacao == null)
        {
            return false;
        }

        if (!CalcularDoacao(doacao))
        {
            return false;
        }

        doacao.CurrencyCode = doacao.CurrencyCode ?? "BRL";

        if ((doacao.IsNewSponsor || doacao.IsMemberMilestone) && DoacaoDuplicada(doacao))
        {
            return true;
        }

        CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(new Contexto { Doacao = doacao }));

        // Youtube Recompensar Doações: calcula a recompensa e solicita o crédito de moedas.
        bool sucesso = true;
        bool recompensou = ExecutarMetodo("Youtube Recompensar Doações", "RecompensarDoacao", out string erro);
        bool recebeuResultado = ReceberResultadoRecompensa(doacao);
        bool creditoConfirmado = doacao.CreditoStatus == "creditado";
        if (!recompensou || !recebeuResultado || !creditoConfirmado)
        {
            sucesso = false;
            RegistrarFalha(doacao, "moedas", doacao.CreditoStatus, ComporMensagemErro(erro, doacao.CreditoErro));
        }

        // Youtube Gerente de Banco de Dados: registra a doação e a recompensa calculada, mantendo falhas no log.
        bool salvou = RegistrarDoacaoComRecuperacao(doacao, out erro);
        bool registroConfirmado = doacao.RegistroStatus == "salvo";
        if (!salvou || !registroConfirmado)
        {
            sucesso = false;
            RegistrarFalha(doacao, "registro", doacao.RegistroStatus, ComporMensagemErro(erro, doacao.RegistroErro));
        }

        if (!doacao.IsJewels)
        {
            // Youtube Gerente de Timer: processa o tempo da Maratona com os pontos da doação.
            CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(new Contexto { Doacao = doacao }));

            bool adicionouTempo = ExecutarMetodo("Youtube Gerente de Timer", "AdicionarTempoPorDoacao", out erro);
            bool recebeuTimer = ReceberResultadoEtapa(doacao, "timer");
            if (!adicionouTempo || !recebeuTimer || (doacao.TimerStatus != "aplicado" && doacao.TimerStatus != "ignorado"))
            {
                sucesso = false;
                RegistrarFalha(doacao, "timer", doacao.TimerStatus, ComporMensagemErro(erro, doacao.TimerErro));
            }
        }
        else
        {
            // Jóias não acrescentam tempo ao Subathon.
            doacao.TimerStatus = "ignorado";
            doacao.TimerSegundos = 0;
            doacao.TimerErro = "";
        }

        // Youtube Recompensar Doações: envia a mensagem com o resultado da recompensa.
        CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(new Contexto { Doacao = doacao }));

        if (!ExecutarMetodo("Youtube Recompensar Doações", "EnviarAgradecimento", out erro))
        {
            sucesso = false;
            RegistrarFalha(doacao, "agradecimento", "incerto", erro);
        }

        // Youtube Gerente de Minecraft: encaminha a doação quando a integração está habilitada.
        bool minecraftBridgeEnabled = CPH.GetGlobalVar<bool>("minecraftYoutubeBridgeEnabled", true);
        if (minecraftBridgeEnabled && (doacao.IsTip || doacao.TriggerName == "Super Chat" || doacao.TriggerName == "Super Sticker"))
        {
            doacao.MinecraftAceito = false;
            doacao.MinecraftStatus = "NAO_ENVIADO";
            doacao.MinecraftMensagem = "";
            CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(new Contexto { Doacao = doacao }));

            bool encaminhou = ExecutarMetodo("Youtube Gerente de Minecraft", "ProcessarDoacao", out erro);
            bool recebeuMinecraft = ReceberResultadoEtapa(doacao, "minecraft");
            if (!encaminhou || !recebeuMinecraft || !doacao.MinecraftAceito)
            {
                sucesso = false;
                RegistrarFalha(doacao, "minecraft", doacao.MinecraftStatus, ComporMensagemErro(erro, doacao.MinecraftMensagem));
            }
        }

        doacao.ProcessamentoSucesso = sucesso;
        CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(new Contexto { Doacao = doacao }));
        return true;
    }

    private bool CalcularDoacao(Doacao doacao)
    {
        doacao.ValorBRL = 0m;
        doacao.PontosMetaExatos = 0;

        double valorConversao;

        switch (doacao.TriggerName)
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
                LogWarn($"Tipo de ação não tratado: '{doacao.TriggerName}'. Ignorado.");
                return false;
        }

        try
        {
            doacao.ValorBRL = doacao.IsNewSponsor || doacao.IsMemberMilestone || doacao.IsMembershipGift ? doacao.ValorOriginal : ConverterParaBRL(doacao.ValorOriginal, doacao.CurrencyCode);
        }
        catch (OverflowException ex)
        {
            LogError($"Valor da doação fora do intervalo monetário, sem processamento: {JsonConvert.SerializeObject(doacao)}. {ex.Message}");
            return false;
        }

        // Pontos permanecem double para preservar a fórmula e o contrato com o Timer.
        doacao.PontosMetaExatos = valorConversao * (double)doacao.ValorBRL * PontosPorReal;
        if (string.IsNullOrWhiteSpace(doacao.Usuario) || doacao.ValorBRL <= 0m || doacao.PontosMetaExatos > int.MaxValue || double.IsNaN(doacao.PontosMetaExatos) || double.IsInfinity(doacao.PontosMetaExatos))
        {
            LogError($"Doação inválida, sem processamento: {JsonConvert.SerializeObject(new { doacao, valorBRL = doacao.ValorBRL, pontosMeta = doacao.PontosMetaExatos })}");
            return false;
        }

        doacao.PontosMeta = (int)Math.Round(doacao.PontosMetaExatos);
        return true;
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

    private bool RegistrarDoacaoComRecuperacao(Doacao doacao, out string erro)
    {
        // Repete somente o registro idempotente, preservando GUID, valores, recompensa e timestamp.
        erro = "";

        for (int tentativa = 1; tentativa <= LimiteTentativasRegistro; tentativa++)
        {
            PrepararArgumentosBanco(doacao);

            bool executou = ExecutarMetodo("Youtube Gerente de Banco de Dados", "SalvarDoacao", out erro);

            LerResultadoRegistro(doacao);

            if (doacao.RegistroStatus == "salvo" && doacao.RegistroId > 0)
            {
                if (tentativa > 1)
                {
                    LogInfo("REGISTRO_RECUPERADO " + JsonConvert.SerializeObject(new { doacao.ProcessamentoId, doacao.RegistroId, tentativa }));
                }

                return executou;
            }

            if (doacao.RegistroStatus != "falhou")
            {
                doacao.RegistroStatus = "incerto";
            }

            if (tentativa < LimiteTentativasRegistro)
            {
                LogWarn("NOVA_TENTATIVA_REGISTRO " + JsonConvert.SerializeObject(new { doacao.ProcessamentoId, tentativa, doacao.RegistroStatus, erro = ComporMensagemErro(erro, doacao.RegistroErro) }));
            }
        }

        return false;
    }

    private void PrepararArgumentosBanco(Doacao doacao)
    {
        // Youtube Gerente de Banco de Dados: preserva nomes e tipos do contrato existente.
        CPH.SetArgument("doacaoProcessamentoId", doacao.ProcessamentoId);
        CPH.SetArgument("doacaoUserId", doacao.UsuarioId);
        CPH.SetArgument("doacaoUserName", doacao.Usuario);
        CPH.SetArgument("doacaoTipoAcao", doacao.TriggerName);
        CPH.SetArgument("doacaoValorOriginal", (double)doacao.ValorOriginal);
        CPH.SetArgument("doacaoMoedaOrigem", doacao.CurrencyCode ?? "BRL");
        CPH.SetArgument("doacaoValorBRL", (double)doacao.ValorBRL);
        CPH.SetArgument("doacaoPontosMeta", doacao.PontosMeta);
        CPH.SetArgument("doacaoMoedaGanha", doacao.MoedaGanha);
        CPH.SetArgument("doacaoMultiplicador", doacao.Multiplicador);
        CPH.SetArgument("doacaoBroadcastUserId", doacao.BroadcastUserId);
        CPH.SetArgument("doacaoBroadcastUserName", doacao.BroadcastUserName);
        CPH.SetArgument("doacaoTier", doacao.Tier);
        CPH.SetArgument("doacaoBroadcastId", doacao.BroadcastId);
        CPH.SetArgument("doacaoMessageId", doacao.MessageId);
        CPH.SetArgument("doacaoTimestamp", doacao.Timestamp);
        CPH.SetArgument("doacaoRegistroId", 0L);
        CPH.SetArgument("doacaoRegistroStatus", "incerto");
        CPH.SetArgument("doacaoRegistroErro", "");
    }

    private bool DoacaoDuplicada(Doacao doacao)
    {
        CPH.SetArgument("doacaoDupUserId", doacao.UsuarioId);
        CPH.SetArgument("doacaoDupBroadcastUserId", doacao.BroadcastUserId);
        CPH.SetArgument("doacaoDupTipoAcao", doacao.TriggerName);
        CPH.SetArgument("doacaoDupTier", doacao.Tier ?? "");
        CPH.SetArgument("doacaoDuplicada", false);
        CPH.SetArgument("doacaoDuplicidadeErro", "");

        bool consultou = ExecutarMetodo("Youtube Gerente de Banco de Dados", "VerificarDoacaoDuplicada", out string erro);
        if (!consultou || !CPH.TryGetArg("doacaoDuplicada", out bool duplicado))
        {
            RegistrarFalha(doacao, "duplicidade", "consulta_falhou_continuando", ComporMensagemErro(erro, LerTexto("doacaoDuplicidadeErro")));
            return false;
        }

        if (duplicado)
        {
            LogInfo($"Evento duplicado ignorado: {JsonConvert.SerializeObject(doacao)}");
            doacao.ProcessamentoSucesso = true;
            CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(new Contexto { Doacao = doacao }));
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

    private string ComporMensagemErro(string chamada, string detalhe)
    {
        return string.IsNullOrEmpty(detalhe) ? (string.IsNullOrEmpty(chamada) ? "A etapa não confirmou o resultado esperado." : chamada) : chamada + " " + detalhe;
    }

    private void RegistrarFalha(Doacao doacao, string etapa, string status, string erro)
    {
        // JSON em uma linha: preserva valores e nomes sem ambiguidade para correção manual; nunca executa SQL.
        var dados = new Dictionary<string, object>
        {
            { "Doacao", doacao }
        };
        dados["pastaStreamerBot"] = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true);
        dados["etapa"] = etapa;
        dados["status"] = status;
        dados["erro"] = erro;
        dados["orientacao"] = "Sem repetição automática. Confira o banco/timer antes da recuperação manual se o resultado for incerto. MoedasCalculadas e MoedaGanha representam a recompensa calculada, não comprovam o crédito. Consulte CreditoStatus e confira o saldo e os registros do banco antes de qualquer ajuste manual. Não repita um crédito confirmado ou incerto sem verificar se ele já ocorreu. Use origem=doacao, usuário/canal e a quantidade original ao recuperar moedas, sem novo sorteio.";
        LogError($"RECUPERACAO_MANUAL {JsonConvert.SerializeObject(dados)}");
    }

    private void EnviarMensagem(string mensagem, string broadcastId, bool somenteNaTransmissao)
    {
        // Direciona a resposta quando solicitado e há uma transmissão identificada.
        try
        {
            if (somenteNaTransmissao && !string.IsNullOrWhiteSpace(broadcastId))
            {
                CPH.SendYouTubeMessage(mensagem, true, true, broadcastId);
            }
            else
            {
                CPH.SendYouTubeMessage(mensagem, true, true);
            }
        }
        catch (Exception ex)
        {
            LogError($"Falha ao enviar mensagem ao chat: {ex.Message}");
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

    private bool ReceberResultadoRecompensa(Doacao doacao)
    {
        try
        {
            Contexto contexto = LerContexto();
            Doacao resultado = contexto?.Doacao;
            if (resultado == null || resultado.ProcessamentoId != doacao.ProcessamentoId)
            {
                throw new InvalidOperationException("Resposta da recompensa ausente ou de outro processamento.");
            }

            doacao.MoedasCalculadas = resultado.MoedasCalculadas;
            // Por decisão de negócio, o registro usa a recompensa calculada; o status mantém a confirmação real.
            doacao.MoedaGanha = resultado.MoedasCalculadas;
            doacao.Multiplicador = resultado.Multiplicador;
            doacao.CreditoStatus = resultado.CreditoStatus;
            doacao.CreditoErro = resultado.CreditoErro;
            doacao.DestinatarioId = resultado.DestinatarioId;
            doacao.CreditoOperacaoId = resultado.CreditoOperacaoId;
            doacao.CreditoBancoInicioId = resultado.CreditoBancoInicioId;
            doacao.CreditoBancoRespostaId = resultado.CreditoBancoRespostaId;
            doacao.CreditoBancoConcluido = resultado.CreditoBancoConcluido;
            doacao.CreditoResultado = resultado.CreditoResultado;
            return true;
        }
        catch (Exception ex)
        {
            doacao.CreditoStatus = "incerto";
            doacao.MoedaGanha = doacao.MoedasCalculadas;
            doacao.CreditoErro = "Falha ao receber resultado da recompensa: " + ex.Message;
            LogError(doacao.CreditoErro);
            return false;
        }
    }

    private bool ReceberResultadoEtapa(Doacao doacao, string etapa)
    {
        try
        {
            Doacao resultado = LerContexto()?.Doacao;
            if (resultado == null || resultado.ProcessamentoId != doacao.ProcessamentoId)
            {
                throw new InvalidOperationException("Resposta ausente ou de outro processamento.");
            }

            if (etapa == "timer")
            {
                doacao.TimerStatus = resultado.TimerStatus;
                doacao.TimerSegundos = resultado.TimerSegundos;
                doacao.TimerErro = resultado.TimerErro;
            }
            else
            {
                doacao.MinecraftAceito = resultado.MinecraftAceito;
                doacao.MinecraftStatus = resultado.MinecraftStatus;
                doacao.MinecraftMensagem = resultado.MinecraftMensagem;
            }

            return true;
        }
        catch (Exception ex)
        {
            if (etapa == "timer")
            {
                doacao.TimerStatus = "incerto";
                doacao.TimerErro = ex.Message;
            }
            else
            {
                doacao.MinecraftAceito = false;
                doacao.MinecraftStatus = "INCERTO";
                doacao.MinecraftMensagem = ex.Message;
            }

            return false;
        }
    }

    private void LerResultadoRegistro(Doacao doacao)
    {
        CPH.TryGetArg("doacaoRegistroId", out long registroId);
        doacao.RegistroId = registroId;
        doacao.RegistroStatus = LerTexto("doacaoRegistroStatus");
        doacao.RegistroErro = LerTexto("doacaoRegistroErro");
    }

    private string LerTexto(string argumento)
    {
        CPH.TryGetArg(argumento, out string valor);
        return valor ?? "";
    }

    private Doacao LerEvento()
    {
        // Identifica a trigger, lê seus campos e prepara a identificação comum da doação.
        CPH.TryGetArg("triggerName", out string triggerName);
        if (string.IsNullOrWhiteSpace(triggerName))
        {
            LogError("Tipo de evento não informado.");
            return null;
        }

        Doacao doacao;
        switch (triggerName)
        {
            case "Tip":
                doacao = LerTip();
                break;
            case "Super Chat":
            case "Super Sticker":
                doacao = LerSuperChatSticker();
                break;
            case "New Sponsor":
            case "Member Milestone":
                doacao = LerMembro();
                break;
            case "Membership Gift":
                doacao = LerPresentesDeMembro();
                break;
            case "Jewels Gifted":
                doacao = LerJoias();
                break;
            default:
                LogError($"Tipo de evento não suportado: {triggerName}.");
                return null;
        }
        if (doacao == null)
        {
            return null;
        }

        var broadcaster = CPH.YouTubeGetBroadcaster();

        doacao.BroadcastUserId = broadcaster?.UserId ?? "";
        doacao.BroadcastUserName = string.IsNullOrEmpty(broadcaster?.UserName) ? "YOUTUBE" : broadcaster?.UserName;
        doacao.ProcessamentoId = Guid.NewGuid().ToString("D");
        doacao.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        doacao.TriggerName = triggerName;
        return doacao;
    }

    private Doacao LerTip()
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

        string broadcastId = "";
        try
        {
            broadcastId = CPH.YouTubeGetMonitoredBroadcasts()?.FirstOrDefault(b => b.Title?.Contains("🔴") == true)?.Id ?? "";
        }
        catch (Exception ex)
        {
            LogWarn("Não foi possível identificar a transmissão da Tip: " + ex.Message);
        }

        Doacao doacao = new Doacao
        {
            ValorOriginal = valor,
            UsuarioId = "",
            Usuario = tipUsername ?? "",
            CurrencyCode = tipCurrency?.Trim().ToUpperInvariant(),
            MessageId = messageId ?? "",
            Message = tipMessage ?? "",
            BroadcastId = broadcastId,
        };
        return doacao;
    }

    private Doacao LerSuperChatSticker()
    {
        Doacao doacao = LerDoacaoYoutube();

        CPH.TryGetArg("microAmount", out long microAmount);
        CPH.TryGetArg("currencyCode", out string currencyCode);

        doacao.ValorOriginal = microAmount / (decimal)MicroUnidadesPorUnidadeMonetaria;
        doacao.CurrencyCode = currencyCode?.Trim().ToUpperInvariant();
        return doacao;
    }

    private Doacao LerMembro()
    {
        Doacao doacao = LerDoacaoYoutube();

        CPH.TryGetArg("months", out int months);
        CPH.TryGetArg("levelName", out string levelName);

        doacao.Months = months > 0 ? months : 1;
        doacao.Tier = levelName;
        doacao.ValorOriginal = ObterValorTier(levelName);
        doacao.CurrencyCode = "BRL";
        return doacao;
    }

    private Doacao LerPresentesDeMembro()
    {
        Doacao doacao = LerDoacaoYoutube();

        CPH.TryGetArg("count", out int count);
        CPH.TryGetArg("tier", out string tier);

        doacao.QuantidadeGifts = count > 0 ? count : 1;
        doacao.Tier = tier;
        doacao.ValorOriginal = ObterValorTier(tier) * doacao.QuantidadeGifts;
        doacao.CurrencyCode = "BRL";
        return doacao;
    }

    private Doacao LerJoias()
    {
        Doacao doacao = LerDoacaoYoutube();

        CPH.TryGetArg("gift.jewelsAmount", out double jewelsAmount);
        if (!TentarConverterValorMonetario(jewelsAmount, "gift.jewelsAmount", out decimal quantidadeJoias))
        {
            return null;
        }

        doacao.JewelsAmount = jewelsAmount;
        doacao.ValorOriginal = quantidadeJoias / JoiasPorDolar; // 2 Joias = 0,01 Dólar.
        doacao.CurrencyCode = "USD";
        return doacao;
    }

    private Doacao LerDoacaoYoutube()
    {
        // Identificação compartilhada pelos eventos nativos do YouTube.
        CPH.TryGetArg("userId", out string usuarioId);
        CPH.TryGetArg("user", out string usuario);
        CPH.TryGetArg("messageId", out string messageId);
        CPH.TryGetArg("broadcast.id", out string broadcastId);

        Doacao doacao = new Doacao
        {
            UsuarioId = usuarioId,
            Usuario = usuario,
            MessageId = messageId,
            BroadcastId = broadcastId,
        };
        return doacao;
    }

    private Contexto LerContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrWhiteSpace(contextoJson))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class Contexto
    {
        public Doacao Doacao { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
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
        public bool CreditoBancoConcluido { get; set; }
        public bool MinecraftAceito { get; set; }

        // Gerente de Doações: valores e características da doação.
        public int Months { get; set; }
        public int QuantidadeGifts { get; set; } = 1;
        public int PontosMeta { get; set; }
        // Recompensar Doações: moedas calculadas e resultado do crédito.
        public int MoedasCalculadas { get; set; }
        public int MoedaGanha { get; set; }
        public int Multiplicador { get; set; }
        // Gerente de Timer: tempo adicionado e resultado do processamento.
        public int TimerSegundos { get; set; }

        // Gerente de Banco de Dados: resultado da gravação da doação.
        public long RegistroId { get; set; }

        public decimal ValorOriginal { get; set; }
        public decimal ValorBRL { get; set; }

        public double JewelsAmount { get; set; }
        public double PontosMetaExatos { get; set; }

        public string CreditoOperacaoId { get; set; }
        public string CreditoBancoInicioId { get; set; }
        public string CreditoBancoRespostaId { get; set; }
        public string CreditoResultado { get; set; }
        public string MinecraftStatus { get; set; } = "NAO_ENVIADO";
        public string MinecraftMensagem { get; set; } = "";
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
        public string CurrencyCode { get; set; }
        public string Tier { get; set; }
        public string CreditoStatus { get; set; } = "incerto";
        public string CreditoErro { get; set; } = "";
        public string DestinatarioId { get; set; } = "";
        public string RegistroStatus { get; set; } = "incerto";
        public string RegistroErro { get; set; } = "";
        public string TimerStatus { get; set; } = "incerto";
        public string TimerErro { get; set; } = "";
    }
}