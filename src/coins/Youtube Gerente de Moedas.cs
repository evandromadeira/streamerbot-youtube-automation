using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261022.0630
// Padrões aplicados: 5
// Revisão concluída: 1
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
        Chat chat = null;
        try
        {
            Contexto contexto = LerContexto();
            if (contexto?.Chat == null)
            {
                LogError("ERRO: não foi possível ler o contexto do chat.");
                return false;
            }

            chat = contexto.Chat;
            string[] partesComando = (chat.MessageText ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);

            string mensagem = partesComando.Length > 1 ? partesComando[1].Replace("@", "").Trim() : "";

            bool consultaPropria = string.IsNullOrEmpty(mensagem);

            string nomeConsultado = consultaPropria ? chat.UserName : mensagem;
            // Youtube Gerente de Banco de Dados: consulta o saldo e a posição do usuário.
            PrepararArgumentosSaldo(consultaPropria ? chat.UserId : nomeConsultado, consultaPropria);

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SaldoMoedasUsuario");
            if (!executou)
            {
                EnviarMensagem("❌ Falha técnica ao consultar moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("consultarEncontrado", out bool encontrado);
            if (!encontrado)
            {
                EnviarMensagem($"ℹ @{nomeConsultado} ainda não possui moedas do canal registradas.", chat?.BroadcastId, somenteNaTransmissao: true);
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
            EnviarMensagem($"{posicaoTexto}(#{rank}) - @{nomeExibido}: {moedas:N0} Moedas do Canal", chat?.BroadcastId, somenteNaTransmissao: true);
            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao consultar moedas: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao consultar moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }
    }

    public bool TopMoedas()
    {
        Chat chat = null;
        try
        {
            Contexto contexto = LerContexto();
            if (contexto?.Chat == null)
            {
                LogError("ERRO: não foi possível ler o contexto do chat.");
                return false;
            }

            chat = contexto.Chat;
            string[] partes = (chat.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int limiteMaximo = chat.IsMod ? LimiteTopModerador : LimiteTopUsuario;
            if (partes.Length != 2 || !int.TryParse(partes[1], out int quantidade) || quantidade <= 0)
            {
                EnviarMensagem($"⚠ Uso correto: !topmoedas [quantidade] (máximo {limiteMaximo})", chat?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            if (quantidade > limiteMaximo)
            {
                EnviarMensagem($"⚠ @{chat.UserName}, o máximo permitido é {limiteMaximo}.", chat?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            // Youtube Gerente de Banco de Dados: consulta os maiores saldos do canal.
            CPH.SetArgument("topMoedasQuantidade", quantidade);
            CPH.SetArgument("topMoedasResultadoJson", "[]");

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ConsultarTopMoedas");
            if (!executou)
            {
                EnviarMensagem("❌ Falha técnica ao consultar o top de moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("topMoedasResultadoJson", out string resultadoJson);
            List<TopMoedaItem> itens = string.IsNullOrEmpty(resultadoJson) ? null : JsonConvert.DeserializeObject<List<TopMoedaItem>>(resultadoJson);
            if (itens == null || itens.Count == 0)
            {
                EnviarMensagem("ℹ Ainda não há moedas do canal registradas.", chat?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            EnviarTopMoedas(itens, chat.BroadcastId);
            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao consultar top de moedas do canal: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao consultar o top de moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }
    }

    public bool AdicionarMoedasDoacao()
    {
        // Converte somente na fronteira com o Banco, que mantém argumentos individuais.
        Contexto contexto = null;

        Doacao doacao = null;
        try
        {
            contexto = LerContexto();
            doacao = contexto?.Doacao;
            if (doacao == null || string.IsNullOrWhiteSpace(doacao.ProcessamentoId))
            {
                throw new ArgumentException("Contexto da doação ausente ou inválido.");
            }

            doacao.CreditoStatus = "incerto";
            doacao.CreditoErro = "";
            doacao.MoedaGanha = 0;
            doacao.DestinatarioId = "";
            bool executou = AdicionarMoedasUsuarioBase(doacao);
            return executou;
        }
        catch (Exception ex)
        {
            if (doacao != null)
            {
                doacao.CreditoErro = ex.ToString();
            }

            LogError("Erro ao processar crédito da doação: " + ex.Message);
            return false;
        }
        finally
        {
            CPH.SetArgument("contextoJson", contexto == null ? "" : JsonConvert.SerializeObject(contexto));
        }
    }

    public bool AdicionarMoedasCredito()
    {
        // Recebe o pedido pelo contexto e devolve o resultado confirmado pelo Banco.
        Contexto contexto = null;

        Moedas moedas = null;
        try
        {
            contexto = LerContexto();
            if (contexto == null)
            {
                throw new ArgumentException("Contexto do pedido ausente.");
            }

            contexto.Moedas = null;
            if (contexto.ImportarMoedas != null && contexto.ComparaPalavra != null)
            {
                throw new ArgumentException("O contexto contém mais de uma origem de crédito.");
            }

            ImportarMoedas importarMoedas = contexto.ImportarMoedas;

            ComparaPalavra comparaPalavra = contexto.ComparaPalavra;
            if (importarMoedas != null)
            {
                moedas = new Moedas
                {
                    OperacaoId = importarMoedas.OperacaoId,
                    Origem = "importacao",
                    UsuarioId = importarMoedas.UsuarioId,
                    Usuario = importarMoedas.Usuario,
                    Quantidade = importarMoedas.Quantidade,
                    BroadcastUserId = importarMoedas.BroadcastUserId,
                    BroadcastUserName = importarMoedas.BroadcastUserName
                };
            }
            else if (comparaPalavra != null)
            {
                moedas = new Moedas
                {
                    OperacaoId = comparaPalavra.OperacaoId,
                    Origem = "moedas_surpresa",
                    UsuarioId = comparaPalavra.UsuarioId,
                    Usuario = comparaPalavra.Usuario,
                    Quantidade = comparaPalavra.Quantidade,
                    BroadcastUserId = comparaPalavra.BroadcastUserId,
                    BroadcastUserName = comparaPalavra.BroadcastUserName
                };
            }

            contexto.Moedas = moedas;
            if (moedas == null || string.IsNullOrWhiteSpace(moedas.OperacaoId))
            {
                throw new ArgumentException("Pedido de crédito ausente ou inválido.");
            }

            moedas.Concluido = false;
            moedas.Status = "incerto";
            moedas.Erro = "";
            moedas.Resultado = "";
            moedas.DestinatarioId = "";
            bool executou = AdicionarMoedasUsuarioBase(null, moedas: moedas);
            return executou;
        }
        catch (Exception ex)
        {
            if (moedas != null)
            {
                moedas.Erro = ex.ToString();
            }

            LogError("Erro ao processar pedido de crédito: " + ex.Message);
            return false;
        }
        finally
        {
            CPH.SetArgument("contextoJson", contexto == null ? "" : JsonConvert.SerializeObject(contexto));
        }
    }

    public bool AdicionarMoedasUsuario()
    {
        return AdicionarMoedasUsuarioBase(null);
    }

    public bool RecompensarAtividadeChat()
    {
        try
        {
            Contexto contexto = LerContexto();
            if (contexto?.Chat == null)
            {
                LogError("ERRO: não foi possível ler o contexto do chat.");
                return false;
            }

            Chat chat = contexto.Chat;

            int moedasPorMensagem = MoedasPorMensagem;
            int cooldownMinutos = CooldownAtividadeMinutos;
            int multiplicador = 1;
            if (chat.IsSpo)
            {
                multiplicador++;
            }

            if (chat.IsSub)
            {
                multiplicador++;
            }

            moedasPorMensagem *= multiplicador;
            bool creditou = AdicionarMoedasUsuarioBase(null, chat, moedasPorMensagem, cooldownMinutos);
            if (!creditou)
            {
                LogError($"ERRO: falha ao creditar moedas do canal de atividade de chat para @{chat.UserName}.");
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
        Chat chat = null;
        try
        {
            Contexto contexto = LerContexto();
            if (contexto?.Chat == null)
            {
                LogError("ERRO: não foi possível ler o contexto do chat.");
                return false;
            }

            chat = contexto.Chat;
            string[] partes = (chat.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 3)
            {
                EnviarMensagem("⚠ Uso correto: !transferir [@usuario] [quantidade]", chat?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string destinatarioNome = partes[1].TrimStart('@').Trim();
            if (string.IsNullOrEmpty(destinatarioNome))
            {
                EnviarMensagem($"⚠ @{chat.UserName}, informe o usuário que vai receber as moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string quantidadeStr = partes[2];
            if (!int.TryParse(quantidadeStr, out int quantidade) || quantidade <= 0)
            {
                EnviarMensagem($"⚠ @{chat.UserName}, a quantidade precisa ser um número inteiro maior que zero.", chat?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            // Youtube Gerente de Banco de Dados: transfere as moedas após validar o comando.
            PrepararArgumentosTransferencia(chat.UserId, destinatarioNome, quantidade);

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "TransferirMoedasUsuario");
            if (!executou)
            {
                EnviarMensagem("❌ Falha técnica ao transferir moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("transferirResultado", out string resultado);
            switch (resultado)
            {
                case "Sucesso":
                    CPH.TryGetArg("transferirDestinatarioNomeExibido", out string nomeDestinoExibido);
                    string nomeFinal = string.IsNullOrEmpty(nomeDestinoExibido) ? destinatarioNome : nomeDestinoExibido;
                    EnviarMensagem($"✅ @{chat.UserName} transferiu {quantidade:N0} moedas do canal para @{nomeFinal}!", chat?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "DestinatarioNaoEncontrado":
                    EnviarMensagem($"❌ @{chat.UserName}, o usuário '{destinatarioNome}' não foi encontrado.", chat?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "SaldoInsuficiente":
                    CPH.TryGetArg("transferirSaldoRemetente", out int saldoRemetente);
                    EnviarMensagem($"❌ @{chat.UserName}, saldo insuficiente! Você possui apenas {saldoRemetente:N0} moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "AutoTransferencia":
                    EnviarMensagem($"⚠ @{chat.UserName}, você não pode transferir moedas do canal para si mesmo.", chat?.BroadcastId, somenteNaTransmissao: true);
                    break;
                default:
                    EnviarMensagem("❌ Falha técnica ao transferir moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao transferir moedas do canal: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao transferir moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }
    }

    private bool AdicionarMoedasUsuarioBase(Doacao doacao, Chat chatAtividade = null, int moedasAtividade = 0, int cooldownAtividade = 0, Moedas moedas = null)
    {
        bool bancoChamado = false;

        string origem = "";
        string operacaoId = moedas?.OperacaoId ?? Guid.NewGuid().ToString("D");

        Chat chat = chatAtividade;
        try
        {
            InicializarResultadoCredito(operacaoId);
            if (doacao != null)
            {
                origem = "doacao";
            }
            else if (chatAtividade != null)
            {
                origem = "chat_atividade";
            }
            else
            {
                origem = moedas?.Origem ?? "chat_adicionar";
            }

            origem = string.IsNullOrEmpty(origem) ? "chat_adicionar" : origem;
            if (chat == null && (origem == "chat_atividade" || origem == "chat_adicionar"))
            {
                Contexto contexto = LerContexto();
                if (contexto?.Chat == null)
                {
                    LogError("ERRO: não foi possível ler o contexto do chat.");
                    return false;
                }

                chat = contexto.Chat;
            }

            int coinsToAdd = 0;

            string targetUserId = "";
            string targetUserName = "";
            string senderUserName = "";
            if (origem == "chat_atividade" || origem == "doacao" || origem == "moedas_surpresa" || origem == "importacao")
            {
                string broadcastUserId;
                string broadcastUserName;
                if (doacao != null)
                {
                    targetUserId = doacao.UsuarioId;
                    targetUserName = doacao.Usuario;
                    coinsToAdd = doacao.MoedasCalculadas;
                    broadcastUserId = doacao.BroadcastUserId;
                    broadcastUserName = doacao.BroadcastUserName;
                }
                else if (chatAtividade != null)
                {
                    targetUserId = chatAtividade.UserId;
                    targetUserName = chatAtividade.UserName;
                    coinsToAdd = moedasAtividade;
                    broadcastUserId = chatAtividade.BroadcastUserId;
                    broadcastUserName = chatAtividade.BroadcastUserName;
                }
                else
                {
                    targetUserId = moedas.UsuarioId;
                    targetUserName = moedas.Usuario;
                    coinsToAdd = moedas.Quantidade;
                    broadcastUserId = moedas.BroadcastUserId;
                    broadcastUserName = moedas.BroadcastUserName;
                }

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
                senderUserName = chat.UserName;
                if (!chat.IsMod)
                {
                    EnviarMensagem($"❌ @{chat.UserName}, apenas moderadores podem usar !adicionar.", chat?.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                string[] partes = (chat.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length != 3)
                {
                    EnviarMensagem("⚠ Uso correto: !adicionar [@usuario] [quantidade]", chat?.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                targetUserName = partes[1].TrimStart('@').Trim();
                if (string.IsNullOrEmpty(targetUserName))
                {
                    EnviarMensagem($"⚠ @{chat.UserName}, informe o usuário que vai receber as moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                if (!int.TryParse(partes[2], out coinsToAdd) || coinsToAdd <= 0)
                {
                    EnviarMensagem($"⚠ @{chat.UserName}, a quantidade precisa ser um número inteiro maior que zero.", chat?.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                CPH.SetArgument("adicionarBroadcastUserId", chat.BroadcastUserId);
                CPH.SetArgument("adicionarBroadcastUserName", chat.BroadcastUserName);
            }

            int cooldownMinutos = chatAtividade != null ? cooldownAtividade : 0;
            // Youtube Gerente de Banco de Dados: aplica o crédito e devolve sua confirmação.
            PrepararArgumentosCredito(origem, targetUserId, targetUserName, coinsToAdd, cooldownMinutos);

            bancoChamado = true;
            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "AdicionarMoedasUsuario");
            ValidarRespostaCredito(operacaoId);
            CPH.TryGetArg("adicionarCreditoStatus", out string creditoStatus);
            if (!executou)
            {
                LogError("ERRO: falha ao adicionar moedas do canal por comando.");
                if (origem != "doacao" && origem != "importacao" && origem != "moedas_surpresa")
                {
                    EnviarMensagem("❌ Falha técnica ao adicionar moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
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
                        EnviarMensagem($"✅ @{senderUserName} adicionou {coinsToAdd:N0} moedas do canal para @{nomeFinal}!", chat?.BroadcastId, somenteNaTransmissao: true);
                        break;
                    case "ParametrosInvalidos":
                        EnviarMensagem($"❌ @{senderUserName}, parâmetros inválidos para adicionar moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
                        break;
                    case "BancoNaoEncontrado":
                        EnviarMensagem("❌ Falha técnica ao adicionar moedas do canal (Banco de dados não encontrado).", chat?.BroadcastId, somenteNaTransmissao: true);
                        break;
                    default:
                        EnviarMensagem("❌ Falha técnica ao adicionar moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
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
                EnviarMensagem("❌ Falha técnica ao adicionar moedas do canal.", chat?.BroadcastId, somenteNaTransmissao: true);
            }

            return (origem == "doacao" || origem == "importacao" || origem == "moedas_surpresa") && creditoStatus == "creditado";
        }
        finally
        {
            // Transforma a resposta do Banco nos objetos devolvidos aos chamadores.
            if (doacao != null)
            {
                CPH.TryGetArg("adicionarCreditoStatus", out string status);
                CPH.TryGetArg("adicionarResultado", out string resultado);
                CPH.TryGetArg("adicionarErro", out string erro);
                CPH.TryGetArg("adicionarDestinatarioId", out string destinatarioId);
                CPH.TryGetArg("adicionarOperacaoId", out string operacaoRetornoId);
                CPH.TryGetArg("adicionarBancoInicioId", out string inicioId);
                CPH.TryGetArg("adicionarBancoRespostaId", out string respostaId);
                CPH.TryGetArg("adicionarBancoConcluido", out bool concluido);
                doacao.CreditoStatus = !string.IsNullOrEmpty(resultado) && status == "creditado" ? "creditado" : status == "falhou" ? "falhou" : "incerto";
                doacao.CreditoErro = erro ?? "";
                doacao.CreditoResultado = resultado ?? "";
                doacao.CreditoOperacaoId = operacaoRetornoId;
                doacao.CreditoBancoInicioId = inicioId;
                doacao.CreditoBancoRespostaId = respostaId;
                doacao.CreditoBancoConcluido = concluido;
                doacao.DestinatarioId = destinatarioId ?? "";
                doacao.MoedaGanha = doacao.CreditoStatus == "creditado" ? doacao.MoedasCalculadas : 0;
            }
            else if (moedas != null)
            {
                CPH.TryGetArg("adicionarCreditoStatus", out string status);
                CPH.TryGetArg("adicionarErro", out string erro);
                CPH.TryGetArg("adicionarResultado", out string resultado);
                CPH.TryGetArg("adicionarDestinatarioId", out string destinatarioId);
                moedas.Status = status == "creditado" || status == "falhou" ? status : "incerto";
                moedas.Erro = erro ?? "";
                moedas.Resultado = resultado ?? "";
                moedas.DestinatarioId = destinatarioId ?? "";
                moedas.Concluido = true;
            }
        }
    }

    private void PrepararArgumentosSaldo(string chave, bool consultarPorId)
    {
        CPH.SetArgument("consultarChave", chave);
        CPH.SetArgument("consultarPorId", consultarPorId);
        CPH.SetArgument("consultarEncontrado", false);
        CPH.SetArgument("consultarNomeExibido", "");
        CPH.SetArgument("consultarMoedas", 0);
        CPH.SetArgument("consultarRank", 0);
    }

    private void PrepararArgumentosTransferencia(string remetenteUserId, string destinatarioNome, int quantidade)
    {
        CPH.SetArgument("transferirRemetenteUserId", remetenteUserId);
        CPH.SetArgument("transferirDestinatarioNome", destinatarioNome);
        CPH.SetArgument("transferirQuantidade", quantidade);
        CPH.SetArgument("transferirResultado", "");
        CPH.SetArgument("transferirDestinatarioNomeExibido", "");
        CPH.SetArgument("transferirSaldoRemetente", 0);
    }

    private void InicializarResultadoCredito(string operacaoId)
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
    }

    private void PrepararArgumentosCredito(string origem, string targetUserId, string targetUserName, int coinsToAdd, int cooldownMinutos)
    {
        CPH.SetArgument("adicionarOrigem", origem);
        CPH.SetArgument("adicionarUserId", targetUserId);
        CPH.SetArgument("adicionarUserName", targetUserName);
        CPH.SetArgument("adicionarQuantidade", coinsToAdd);
        CPH.SetArgument("adicionarCooldownMinutos", cooldownMinutos);
        CPH.SetArgument("adicionarCreditoStatus", "incerto");
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

    private Contexto LerContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrEmpty(contextoJson))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class TopMoedaItem
    {
        public int Rank { get; set; }
        public int Moedas { get; set; }

        public string NomeExibido { get; set; }
    }

    public class Contexto
    {
        public ComparaPalavra ComparaPalavra { get; set; }

        public ImportarMoedas ImportarMoedas { get; set; }

        public Moedas Moedas { get; set; }

        public Doacao Doacao { get; set; }

        [JsonProperty("Evento")]
        public Chat Chat { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class ComparaPalavra
    {
        public int Quantidade { get; set; }

        public string OperacaoId { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class ImportarMoedas
    {
        public int Quantidade { get; set; }

        public string OperacaoId { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Moedas
    {
        public bool Concluido { get; set; }

        public int Quantidade { get; set; }

        public string OperacaoId { get; set; }
        public string Origem { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
        public string Status { get; set; } = "incerto";
        public string Erro { get; set; } = "";
        public string Resultado { get; set; } = "";
        public string DestinatarioId { get; set; } = "";
    }

    public class Doacao
    {
        public bool CreditoBancoConcluido { get; set; }

        public int MoedasCalculadas { get; set; }
        public int MoedaGanha { get; set; }

        public string ProcessamentoId { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string CreditoStatus { get; set; }
        public string CreditoErro { get; set; }
        public string CreditoResultado { get; set; }
        public string CreditoOperacaoId { get; set; }
        public string CreditoBancoInicioId { get; set; }
        public string CreditoBancoRespostaId { get; set; }
        public string DestinatarioId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }

        // Preserva os resultados das outras etapas ao devolver o contexto.
        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Chat
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

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }
}