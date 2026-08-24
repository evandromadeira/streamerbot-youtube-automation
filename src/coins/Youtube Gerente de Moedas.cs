using System;
using Newtonsoft.Json;

// Atualização 260824.0945
public class CPHInline
{
    public bool SaldoMoedasUsuario()
    {
        try
        {
            CPH.TryGetArg("contextoJson", out string contextoJson);

            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_MOEDAS] ERRO: contexto ausente ou inválido.");
                return false;
            }

            var evento = contexto.Evento;
            string[] partesComando = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string mensagem = partesComando.Length > 1 ? partesComando[1].Replace("@", "").Trim() : "";
            bool consultaPropria = string.IsNullOrEmpty(mensagem);
            string nomeConsultado = consultaPropria ? evento.UserName : mensagem;

            CPH.SetArgument("consultarChave", consultaPropria ? evento.UserId : nomeConsultado);
            CPH.SetArgument("consultarPorId", consultaPropria);

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SaldoMoedasUsuario");
            if (!executou)
            {
                CPH.SendYouTubeMessage("❌ Falha técnica ao consultar moedas.");
                return false;
            }

            CPH.TryGetArg("consultarEncontrado", out bool encontrado);
            if (!encontrado)
            {
                CPH.SendYouTubeMessage($"ℹ @{nomeConsultado} ainda não possui moedas registradas.");
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

            CPH.SendYouTubeMessage($"{posicaoTexto}(#{rank}) - @{nomeExibido}: {moedas:N0} Moedas");
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_MOEDAS] ERRO CRÍTICO ao consultar moedas: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao consultar moedas.");
            return false;
        }
    }

    public bool AdicionarMoedasUsuario()
    {
        try
        {
            CPH.TryGetArg("origem", out string origem);

            origem = string.IsNullOrEmpty(origem) ? "chat_adicionar" : origem;

            string targetUserId = "";
            string targetUserName = "";
            string senderUserName = "";

            int coinsToAdd = 0;

            if (origem == "chat_atividade" || origem == "doacao" || origem == "moedas_surpresa" || origem == "importacao")
            {
                CPH.TryGetArg("targetUserId", out targetUserId);
                CPH.TryGetArg("targetUserName", out targetUserName);
                CPH.TryGetArg("coinsToAdd", out coinsToAdd);
                CPH.TryGetArg("broadcastUserId", out string broadcastUserId);
                CPH.TryGetArg("broadcastUserName", out string broadcastUserName);

                if ((string.IsNullOrEmpty(targetUserId) && string.IsNullOrEmpty(targetUserName)) || coinsToAdd <= 0)
                {
                    CPH.LogError(">>> [GERENTE_MOEDAS] ERRO: Dados de doação inválidos.");
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
                CPH.TryGetArg("contextoJson", out string contextoJson);

                var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);
                if (contexto?.Evento == null)
                {
                    CPH.LogError(">>> [GERENTE_MOEDAS] ERRO: contexto ausente ou inválido.");
                    return false;
                }

                var evento = contexto.Evento;
                senderUserName = evento.UserName;

                if (!evento.IsMod)
                {
                    CPH.SendYouTubeMessage($"❌ @{evento.UserName}, apenas moderadores podem usar !adicionar.");
                    return false;
                }

                string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length != 3)
                {
                    CPH.SendYouTubeMessage("⚠ Uso correto: !adicionar [@usuario] [quantidade]");
                    return false;
                }

                targetUserName = partes[1].TrimStart('@').Trim();
                if (string.IsNullOrEmpty(targetUserName))
                {
                    CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, informe o usuário que vai receber as moedas.");
                    return false;
                }

                if (!int.TryParse(partes[2], out coinsToAdd) || coinsToAdd <= 0)
                {
                    CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, a quantidade precisa ser um número inteiro maior que zero.");
                    return false;
                }

                CPH.SetArgument("adicionarBroadcastUserId", evento.BroadcastUserId);
                CPH.SetArgument("adicionarBroadcastUserName", evento.BroadcastUserName);
            }

            CPH.SetArgument("adicionarUserId", targetUserId);
            CPH.SetArgument("adicionarUserName", targetUserName);
            CPH.SetArgument("adicionarQuantidade", coinsToAdd);
            CPH.SetArgument("adicionarOrigem", origem);

            CPH.TryGetArg("cooldownMinutos", out int cooldownMinutos);

            CPH.SetArgument("adicionarCooldownMinutos", cooldownMinutos);

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "AdicionarMoedasUsuario");
            if (!executou)
            {
                CPH.LogError(">>> [GERENTE_MOEDAS] ERRO: falha ao adicionar moedas por comando.");
                CPH.SendYouTubeMessage("❌ Falha técnica ao adicionar moedas.");
                return false;
            }

            CPH.TryGetArg("adicionarResultado", out string resultado);

            if (origem == "chat_adicionar")
            {
                switch (resultado)
                {
                    case "Sucesso":
                        CPH.TryGetArg("adicionarDestinatarioNomeExibido", out string nomeDestinoExibido);
                        string nomeFinal = string.IsNullOrEmpty(nomeDestinoExibido) ? targetUserName : nomeDestinoExibido;
                        CPH.SendYouTubeMessage($"✅ @{senderUserName} adicionou {coinsToAdd:N0} Moedas para @{nomeFinal}!");
                        break;
                    case "ParametrosInvalidos":
                        CPH.SendYouTubeMessage($"❌ @{senderUserName}, parâmetros inválidos para adicionar moedas.");
                        break;
                    case "BancoNaoEncontrado":
                        CPH.SendYouTubeMessage("❌ Falha técnica ao adicionar moedas (Banco de dados não encontrado).");
                        break;
                    default:
                        CPH.SendYouTubeMessage("❌ Falha técnica ao adicionar moedas.");
                        break;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_MOEDAS] ERRO CRÍTICO ao adicionar moedas: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao adicionar moedas.");
            return false;
        }
    }

    public bool TransferirMoedasUsuario()
    {
        try
        {
            CPH.TryGetArg("contextoJson", out string contextoJson);

            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_MOEDAS] ERRO: contexto ausente ou inválido.");
                return false;
            }

            var evento = contexto.Evento;
            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 3)
            {
                CPH.SendYouTubeMessage("⚠ Uso correto: !transferir [@usuario] [quantidade]");
                return false;
            }

            string destinatarioNome = partes[1].TrimStart('@').Trim();
            if (string.IsNullOrEmpty(destinatarioNome))
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, informe o usuário que vai receber as moedas.");
                return false;
            }

            string quantidadeStr = partes[2];
            if (!int.TryParse(quantidadeStr, out int quantidade) || quantidade <= 0)
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, a quantidade precisa ser um número inteiro maior que zero.");
                return false;
            }

            CPH.SetArgument("transferirRemetenteUserId", evento.UserId);
            CPH.SetArgument("transferirDestinatarioNome", destinatarioNome);
            CPH.SetArgument("transferirQuantidade", quantidade);

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "TransferirMoedasUsuario");
            if (!executou)
            {
                CPH.SendYouTubeMessage("❌ Falha técnica ao transferir moedas.");
                return false;
            }

            CPH.TryGetArg("transferirResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    CPH.TryGetArg("transferirDestinatarioNomeExibido", out string nomeDestinoExibido);
                    string nomeFinal = string.IsNullOrEmpty(nomeDestinoExibido) ? destinatarioNome : nomeDestinoExibido;
                    CPH.SendYouTubeMessage($"✅ @{evento.UserName} transferiu {quantidade:N0} moedas para @{nomeFinal}!");
                    break;
                case "DestinatarioNaoEncontrado":
                    CPH.SendYouTubeMessage($"❌ @{evento.UserName}, o usuário '{destinatarioNome}' não foi encontrado.");
                    break;
                case "SaldoInsuficiente":
                    CPH.TryGetArg("transferirSaldoRemetente", out int saldoRemetente);
                    CPH.SendYouTubeMessage($"❌ @{evento.UserName}, saldo insuficiente! Você possui apenas {saldoRemetente:N0} moedas.");
                    break;
                case "AutoTransferencia":
                    CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, você não pode transferir moedas para si mesmo.");
                    break;
                default:
                    CPH.SendYouTubeMessage("❌ Falha técnica ao transferir moedas.");
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_MOEDAS] ERRO CRÍTICO ao transferir moedas: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao transferir moedas.");
            return false;
        }
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
    }

    public class Evento
    {
        public bool IsMod { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }
}