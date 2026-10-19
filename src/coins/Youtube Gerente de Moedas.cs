using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// Atualização 261016.0735
public class CPHInline
{
    private const int MoedasPorMensagem = 10;
    private const int CooldownAtividadeMinutos = 10;
    private const int LimiteTopModerador = 100;
    private const int LimiteTopUsuario = 10;
    private const int LimiteMensagem = 200;
    private const int IntervaloMensagensMilissegundos = 300;

    private const string Prefixo = "[GERENTE_MOEDAS] ";

    public bool SaldoMoedasUsuario()
    {
        Evento evento = null;

        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            evento = contexto.Evento;

            string[] partesComando = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);

            string mensagem = partesComando.Length > 1 ? partesComando[1].Replace("@", "").Trim() : "";

            bool consultaPropria = string.IsNullOrEmpty(mensagem);

            string nomeConsultado = consultaPropria ? evento.UserName : mensagem;

            CPH.SetArgument("consultarChave", consultaPropria ? evento.UserId : nomeConsultado);
            CPH.SetArgument("consultarPorId", consultaPropria);

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SaldoMoedasUsuario");
            if (!executou)
            {
                EnviarMensagem("❌ Falha técnica ao consultar moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("consultarEncontrado", out bool encontrado);
            if (!encontrado)
            {
                EnviarMensagem($"ℹ @{nomeConsultado} ainda não possui moedas do canal registradas.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            CPH.TryGetArg("consultarNomeExibido", out string nomeExibido);
            CPH.TryGetArg("consultarMoedas", out int moedas);
            CPH.TryGetArg("consultarRank", out int rank);

            string posicaoTexto = rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => "✨"
            };

            EnviarMensagem($"{posicaoTexto}(#{rank}) - @{nomeExibido}: {moedas:N0} Moedas do Canal", evento?.BroadcastId, somenteNaTransmissao: true);
            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao consultar moedas: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao consultar moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }
    }

    public bool TopMoedas()
    {
        Evento evento = null;

        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            evento = contexto.Evento;

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int limiteMaximo = evento.IsMod ? LimiteTopModerador : LimiteTopUsuario;

            if (partes.Length != 2 || !int.TryParse(partes[1], out int quantidade) || quantidade <= 0)
            {
                EnviarMensagem($"⚠ Uso correto: !topmoedas [quantidade] (máximo {limiteMaximo})", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            if (quantidade > limiteMaximo)
            {
                EnviarMensagem($"⚠ @{evento.UserName}, o máximo permitido é {limiteMaximo}.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.SetArgument("topMoedasQuantidade", quantidade);

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ConsultarTopMoedas");
            if (!executou)
            {
                EnviarMensagem("❌ Falha técnica ao consultar o top de moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("topMoedasResultadoJson", out string resultadoJson);

            List<TopMoedaItem> itens = string.IsNullOrEmpty(resultadoJson) ? null : JsonConvert.DeserializeObject<List<TopMoedaItem>>(resultadoJson);
            if (itens == null || itens.Count == 0)
            {
                EnviarMensagem("ℹ Ainda não há moedas do canal registradas.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            EnviarTopMoedas(itens, evento.BroadcastId);
            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao consultar top de moedas do canal: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao consultar o top de moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }
    }

    private void EnviarTopMoedas(List<TopMoedaItem> itens, string broadcastId)
    {
        const string prefixo = "Top Moedas do Canal: ";

        var mensagens = new List<string>();

        string mensagemAtual = prefixo;

        foreach (var item in itens)
        {
            string medalha = item.Rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => ""
            };

            string trecho = $"{medalha}(#{item.Rank}) - @{item.NomeExibido}: {item.Moedas:N0} |";
            string candidata = mensagemAtual == prefixo ? prefixo + trecho : mensagemAtual + " " + trecho;
            if (candidata.Length > LimiteMensagem)
            {
                mensagens.Add(mensagemAtual);
                mensagemAtual = prefixo + trecho;
            }
            else
            {
                mensagemAtual = candidata;
            }
        }

        if (mensagemAtual != prefixo)
        {
            mensagens.Add(mensagemAtual);
        }

        foreach (var mensagem in mensagens)
        {
            EnviarMensagem(mensagem, broadcastId, somenteNaTransmissao: true);
            CPH.Wait(IntervaloMensagensMilissegundos);
        }
    }

    public bool AdicionarMoedasUsuario()
    {
        bool bancoChamado = false;

        string origem = "";
        string operacaoId = Guid.NewGuid().ToString("D");

        Evento evento = null;

        try
        {
            CPH.SetArgument("adicionarOperacaoId", operacaoId);
            CPH.SetArgument("adicionarBancoInicioId", "");
            CPH.SetArgument("adicionarBancoRespostaId", "");
            CPH.SetArgument("adicionarBancoConcluido", false);
            CPH.SetArgument("adicionarCreditoStatus", "falhou");
            CPH.SetArgument("adicionarErro", "");
            CPH.SetArgument("adicionarResultado", "");
            CPH.SetArgument("adicionarDestinatarioId", "");
            CPH.SetArgument("adicionarDestinatarioNomeExibido", "");

            CPH.TryGetArg("origem", out origem);

            origem = string.IsNullOrEmpty(origem) ? "chat_adicionar" : origem;
            if (origem == "chat_atividade" || origem == "chat_adicionar")
            {
                Contexto contexto = ObterContexto();
                if (contexto?.Evento == null)
                {
                    LogError("ERRO: não foi possível ler o contexto do evento.");
                    return false;
                }
                evento = contexto.Evento;
            }

            int coinsToAdd = 0;

            string targetUserId = "";
            string targetUserName = "";
            string senderUserName = "";

            if (origem == "chat_atividade" || origem == "doacao" || origem == "moedas_surpresa" || origem == "importacao")
            {
                CPH.TryGetArg("targetUserId", out targetUserId);
                CPH.TryGetArg("targetUserName", out targetUserName);
                CPH.TryGetArg("coinsToAdd", out coinsToAdd);
                CPH.TryGetArg("broadcastUserId", out string broadcastUserId);
                CPH.TryGetArg("broadcastUserName", out string broadcastUserName);

                if ((string.IsNullOrEmpty(targetUserId) && string.IsNullOrEmpty(targetUserName)) || coinsToAdd <= 0)
                {
                    LogError("ERRO: Dados de doação inválidos.");
                    CPH.SetArgument("adicionarErro", "Usuário ou quantidade de moedas do canal inválidos.");
                    CPH.SetArgument("adicionarResultado", "ParametrosInvalidos");
                    return false;
                }

                CPH.SetArgument("adicionarUserId", targetUserId);
                CPH.SetArgument("adicionarUserName", targetUserName);
                CPH.SetArgument("adicionarQuantidade", coinsToAdd);
                CPH.SetArgument("adicionarBroadcastUserId", broadcastUserId);
                CPH.SetArgument("adicionarBroadcastUserName", broadcastUserName);
            }
            else if (origem == "chat_adicionar")
            {
                senderUserName = evento.UserName;

                if (!evento.IsMod)
                {
                    EnviarMensagem($"❌ @{evento.UserName}, apenas moderadores podem usar !adicionar.", evento?.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length != 3)
                {
                    EnviarMensagem("⚠ Uso correto: !adicionar [@usuario] [quantidade]", evento?.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                targetUserName = partes[1].TrimStart('@').Trim();
                if (string.IsNullOrEmpty(targetUserName))
                {
                    EnviarMensagem($"⚠ @{evento.UserName}, informe o usuário que vai receber as moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                if (!int.TryParse(partes[2], out coinsToAdd) || coinsToAdd <= 0)
                {
                    EnviarMensagem($"⚠ @{evento.UserName}, a quantidade precisa ser um número inteiro maior que zero.", evento?.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                CPH.SetArgument("adicionarBroadcastUserId", evento.BroadcastUserId);
                CPH.SetArgument("adicionarBroadcastUserName", evento.BroadcastUserName);
            }

            CPH.TryGetArg("cooldownMinutos", out int cooldownMinutos);

            CPH.SetArgument("adicionarOrigem", origem);
            CPH.SetArgument("adicionarUserId", targetUserId);
            CPH.SetArgument("adicionarUserName", targetUserName);
            CPH.SetArgument("adicionarQuantidade", coinsToAdd);
            CPH.SetArgument("adicionarCooldownMinutos", cooldownMinutos);
            CPH.SetArgument("adicionarCreditoStatus", "incerto");

            bancoChamado = true;

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "AdicionarMoedasUsuario");

            ValidarRespostaCredito(operacaoId);

            CPH.TryGetArg("adicionarCreditoStatus", out string creditoStatus);

            if (!executou)
            {
                LogError("ERRO: falha ao adicionar moedas do canal por comando.");
                if (origem != "doacao" && origem != "importacao" && origem != "moedas_surpresa")
                {
                    EnviarMensagem("❌ Falha técnica ao adicionar moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                }
                return (origem == "doacao" || origem == "importacao" || origem == "moedas_surpresa") && creditoStatus == "creditado";
            }

            CPH.TryGetArg("adicionarResultado", out string resultado);

            if (origem == "doacao" || origem == "importacao" || origem == "moedas_surpresa")
            {
                return creditoStatus == "creditado";
            }
            if (origem == "chat_adicionar")
            {
                switch (resultado)
                {
                    case "Sucesso":
                        CPH.TryGetArg("adicionarDestinatarioNomeExibido", out string nomeDestinoExibido);
                        string nomeFinal = string.IsNullOrEmpty(nomeDestinoExibido) ? targetUserName : nomeDestinoExibido;
                        EnviarMensagem($"✅ @{senderUserName} adicionou {coinsToAdd:N0} moedas do canal para @{nomeFinal}!", evento?.BroadcastId, somenteNaTransmissao: true);
                        break;
                    case "ParametrosInvalidos":
                        EnviarMensagem($"❌ @{senderUserName}, parâmetros inválidos para adicionar moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                        break;
                    case "BancoNaoEncontrado":
                        EnviarMensagem("❌ Falha técnica ao adicionar moedas do canal (Banco de dados não encontrado).", evento?.BroadcastId, somenteNaTransmissao: true);
                        break;
                    default:
                        EnviarMensagem("❌ Falha técnica ao adicionar moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                        break;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao adicionar moedas do canal: " + ex);
            if (bancoChamado)
            {
                ValidarRespostaCredito(operacaoId);
            }
            CPH.TryGetArg("adicionarCreditoStatus", out string creditoStatus);
            if (!bancoChamado)
            {
                creditoStatus = "falhou";
            }
            else if (creditoStatus != "creditado" && creditoStatus != "falhou")
            {
                creditoStatus = "incerto";
            }
            CPH.SetArgument("adicionarCreditoStatus", creditoStatus);
            CPH.SetArgument("adicionarErro", ex.ToString());

            if (origem != "doacao" && origem != "importacao" && origem != "moedas_surpresa")
            {
                EnviarMensagem("❌ Falha técnica ao adicionar moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
            }
            return (origem == "doacao" || origem == "importacao" || origem == "moedas_surpresa") && creditoStatus == "creditado";
        }
    }

    private void ValidarRespostaCredito(string operacaoId)
    {
        CPH.TryGetArg("adicionarBancoInicioId", out string inicioId);
        CPH.TryGetArg("adicionarBancoRespostaId", out string respostaId);
        CPH.TryGetArg("adicionarBancoConcluido", out bool concluido);
        CPH.TryGetArg("adicionarResultado", out string resultado);
        CPH.TryGetArg("adicionarCreditoStatus", out string status);
        CPH.TryGetArg("adicionarErro", out string erro);
        CPH.TryGetArg("adicionarUserId", out string usuarioId);
        CPH.TryGetArg("adicionarUserName", out string usuario);
        CPH.TryGetArg("adicionarQuantidade", out int quantidade);
        CPH.TryGetArg("adicionarOrigem", out string origem);
        CPH.TryGetArg("adicionarBroadcastUserId", out string canalId);
        CPH.TryGetArg("adicionarBroadcastUserName", out string canal);

        bool respostaValida = inicioId == operacaoId && respostaId == operacaoId && concluido && !string.IsNullOrEmpty(resultado) && (status == "creditado" || status == "falhou" || status == "incerto");
        if (!respostaValida)
        {
            CPH.SetArgument("adicionarCreditoStatus", "incerto");
            CPH.SetArgument("adicionarResultado", "RespostaIncompleta");
            CPH.SetArgument("adicionarErro", $"Resposta de crédito incompleta ou de outra operação. Operação={operacaoId}; início={inicioId}; resposta={respostaId}; concluído={concluido}; status={status}; resultado={resultado}; erro={erro}");
        }

        if (!respostaValida || (status != "creditado" && resultado != "EmCooldown") || !string.IsNullOrEmpty(erro))
        {
            LogError($"DIAGNOSTICO_CREDITO {JsonConvert.SerializeObject(new { operacaoId, inicioId, respostaId, concluido, respostaValida, origem, usuarioId, usuario, quantidade, canalId, canal, statusRecebido = status, resultadoRecebido = resultado, erro })}");
        }
    }

    public bool RecompensarAtividadeChat()
    {
        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            Evento evento = contexto.Evento;

            int moedasPorMensagem = MoedasPorMensagem;
            int cooldownMinutos = CooldownAtividadeMinutos;
            int multiplicador = 1;

            if (evento.IsSpo)
            {
                multiplicador++;
            }
            if (evento.IsSub)
            {
                multiplicador++;
            }
            moedasPorMensagem *= multiplicador;

            CPH.SetArgument("origem", "chat_atividade");
            CPH.SetArgument("targetUserId", evento.UserId);
            CPH.SetArgument("targetUserName", evento.UserName);
            CPH.SetArgument("coinsToAdd", moedasPorMensagem);
            CPH.SetArgument("cooldownMinutos", cooldownMinutos);
            CPH.SetArgument("broadcastUserId", evento.BroadcastUserId);
            CPH.SetArgument("broadcastUserName", evento.BroadcastUserName);

            bool creditou = AdicionarMoedasUsuario();
            if (!creditou)
            {
                LogError($"ERRO: falha ao creditar moedas do canal de atividade de chat para @{evento.UserName}.");
            }

            return creditou;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao creditar moedas do canal de chat: " + ex.Message);
            return false;
        }
    }

    public bool TransferirMoedasUsuario()
    {
        Evento evento = null;

        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            evento = contexto.Evento;

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 3)
            {
                EnviarMensagem("⚠ Uso correto: !transferir [@usuario] [quantidade]", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string destinatarioNome = partes[1].TrimStart('@').Trim();
            if (string.IsNullOrEmpty(destinatarioNome))
            {
                EnviarMensagem($"⚠ @{evento.UserName}, informe o usuário que vai receber as moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string quantidadeStr = partes[2];
            if (!int.TryParse(quantidadeStr, out int quantidade) || quantidade <= 0)
            {
                EnviarMensagem($"⚠ @{evento.UserName}, a quantidade precisa ser um número inteiro maior que zero.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.SetArgument("transferirRemetenteUserId", evento.UserId);
            CPH.SetArgument("transferirDestinatarioNome", destinatarioNome);
            CPH.SetArgument("transferirQuantidade", quantidade);

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "TransferirMoedasUsuario");
            if (!executou)
            {
                EnviarMensagem("❌ Falha técnica ao transferir moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("transferirResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    CPH.TryGetArg("transferirDestinatarioNomeExibido", out string nomeDestinoExibido);
                    string nomeFinal = string.IsNullOrEmpty(nomeDestinoExibido) ? destinatarioNome : nomeDestinoExibido;
                    EnviarMensagem($"✅ @{evento.UserName} transferiu {quantidade:N0} moedas do canal para @{nomeFinal}!", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "DestinatarioNaoEncontrado":
                    EnviarMensagem($"❌ @{evento.UserName}, o usuário '{destinatarioNome}' não foi encontrado.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "SaldoInsuficiente":
                    CPH.TryGetArg("transferirSaldoRemetente", out int saldoRemetente);
                    EnviarMensagem($"❌ @{evento.UserName}, saldo insuficiente! Você possui apenas {saldoRemetente:N0} moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "AutoTransferencia":
                    EnviarMensagem($"⚠ @{evento.UserName}, você não pode transferir moedas do canal para si mesmo.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                default:
                    EnviarMensagem("❌ Falha técnica ao transferir moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao transferir moedas do canal: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao transferir moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
            return false;
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

    private Contexto ObterContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrEmpty(contextoJson))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
    }

    public class TopMoedaItem
    {
        public int Rank { get; set; }
        public int Moedas { get; set; }

        public string NomeExibido { get; set; }
    }

    public class Evento
    {
        public bool IsSub { get; set; }
        public bool IsSpo { get; set; }
        public bool IsMod { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }
}