using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Atualização 260903.1700
public class CPHInline
{
    public bool Execute()
    {
        try
        {
            Evento evento = new Evento(CPH);
            Ambiente ambiente = new Ambiente(CPH);

            var contexto = new Contexto
            {
                Evento = evento,
                Ambiente = ambiente
            };

            // Serializa para JSON uma única vez
            string contextoJson = JsonConvert.SerializeObject(contexto);
            CPH.SetArgument("contextoJson", contextoJson);

            CPH.SetArgument("chatLogIsSubscribed", evento.IsSub);
            CPH.SetArgument("chatLogIsSponsor", evento.IsSpo);
            CPH.SetArgument("chatLogIsModerator", evento.IsMod);
            CPH.SetArgument("chatLogUserId", evento.UserId);
            CPH.SetArgument("chatLogUserName", evento.UserName);
            CPH.SetArgument("chatLogUserPreviousActive", evento.UserPreviousActive);
            CPH.SetArgument("chatLogMessageId", evento.MessageId);
            CPH.SetArgument("chatLogMessage", evento.MessageText);
            CPH.SetArgument("chatLogPublishedAt", evento.PublishedAt);
            CPH.SetArgument("chatLogBroadcastUserId", evento.BroadcastUserId);
            CPH.SetArgument("chatLogBroadcastUserName", evento.BroadcastUserName);

            bool salvou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SalvarChatLog");
            if (!salvou)
            {
                CPH.LogError(">>> [GERENTE_DE_CHAT] ERRO: falha ao salvar mensagem no banco de dados.");
                return false;
            }

            CPH.LogDebug(">>> [GERENTE_DE_CHAT] DADO SALVO COM SUCESSO!");

            bool creditou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "RecompensarAtividadeChat");
            if (!creditou)
            {
                CPH.LogError(">>> [GERENTE_DE_CHAT] ERRO: falha ao creditar moedas para o usuário.");
            }
            else
            {
                CPH.LogDebug(">>> [GERENTE_DE_CHAT] MOEDAS CREDITADAS COM SUCESSO!");
            }

            // Comandos de Chat
            if (!string.IsNullOrEmpty(evento.MessageText) && evento.MessageText.StartsWith("!"))
            {
                EncaminharComando(evento.MessageText, evento.UserName);
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DE_CHAT] ERRO CRÍTICO: {ex.Message}");
            return false;
        }
    }

    private void EncaminharComando(string message, string userName)
    {
        // Extrai o comando base (a primeira palavra da mensagem)
        string comando = message.Split(' ')[0].ToLower();
        switch (comando)
        {
            case "!saldo":
                CPH.ExecuteMethod("Youtube Gerente de Moedas", "SaldoMoedasUsuario");
                break;
            case "!adicionar":
                CPH.SetArgument("origem", "chat_adicionar");
                CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
                break;
            case "!transferir":
                CPH.ExecuteMethod("Youtube Gerente de Moedas", "TransferirMoedasUsuario");
                break;
            case "!topmoedas":
                CPH.ExecuteMethod("Youtube Gerente de Moedas", "TopMoedas");
                break;
            case "!importar":
                CPH.ExecuteMethod("Youtube Importar Moedas SE", "ImportarMoedasStreamElements");
                break;
            case "!iniciarpalpite":
                CPH.ExecuteMethod("Youtube Gerente de Palpite", "IniciarPalpite");
                break;
            case "!palpite":
                CPH.ExecuteMethod("Youtube Gerente de Palpite", "ApostarPalpite");
                break;
            case "!resultadopalpite":
                CPH.ExecuteMethod("Youtube Gerente de Palpite", "ResolverPalpite");
                break;
            case "!cancelarpalpite":
                CPH.ExecuteMethod("Youtube Gerente de Palpite", "CancelarPalpite");
                break;
            case "!meta":
                CPH.RunAction("Youtube Consultar Meta", false);
                break;
            case "!novoaudio":
                CPH.ExecuteMethod("Youtube Gerente de Áudio", "CadastrarAudio");
                break;
            case "!audios":
                CPH.ExecuteMethod("Youtube Gerente de Áudio", "ListarAudios");
                break;
            default:
                bool audioTocado = CPH.ExecuteMethod("Youtube Gerente de Áudio", "ReproduzirAudio");
                if (audioTocado)
                    break;

                // Desafio de Palavra Surpresa
                var moedasSurpresaPalavra = CPH.GetGlobalVar<string>("moedasSurpresaPalavra", true);
                var actionComparaPalavra = CPH.GetActions().FirstOrDefault(a => a.Name.Equals("Youtube Compara Palavra", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(moedasSurpresaPalavra) && actionComparaPalavra != null && actionComparaPalavra.Enabled)
                {
                    CPH.ExecuteMethod("Youtube Compara Palavra", "CompararPalavra");
                }
                else
                {
                    // Mensagem desativada para não poluir o chat com mensagens de comando desconhecido
                    // CPH.SendYouTubeMessage($"@{userName} - Comando desconhecido: {comando}");
                    CPH.LogDebug($">>> [GERENTE_DE_CHAT] @{userName} - Comando desconhecido: {comando}");
                }

                break;
        }
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
        public Ambiente Ambiente { get; set; }
    }

    public class Evento
    {
        public bool IsSub { get; set; }
        public bool IsSpo { get; set; }
        public bool IsMod { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string UserPreviousActive { get; set; }
        public string MessageId { get; set; }
        public string MessageText { get; set; }
        public string PublishedAt { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }

        // Construtor vazio necessário para deserialização
        public Evento()
        {
        }

        public Evento(IInlineInvokeProxy CPH)
        {
            CPH.TryGetArg("isSubscribed", out bool isSub);
            CPH.TryGetArg("userIsSponsor", out bool isSpo);
            CPH.TryGetArg("isModerator", out bool isMod);
            CPH.TryGetArg("userId", out string userId);
            CPH.TryGetArg("userName", out string userName);
            CPH.TryGetArg("userPreviousActive", out DateTime userPreviousActive);
            CPH.TryGetArg("messageId", out string messageId);
            CPH.TryGetArg("message", out string messageText);
            CPH.TryGetArg("broadcastUserId", out string bUserId);
            CPH.TryGetArg("broadcastUserName", out string bUserName);

            IsSub = isSub;
            IsSpo = isSpo;
            IsMod = isMod;
            UserId = userId;
            UserName = userName;
            UserPreviousActive = userPreviousActive != DateTime.MinValue ? userPreviousActive.ToString("yyyy-MM-dd HH:mm:ss") : "Primeira Mensagem";
            MessageId = messageId;
            MessageText = messageText;
            PublishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            BroadcastUserId = bUserId;
            BroadcastUserName = string.IsNullOrEmpty(bUserName) ? "YOUTUBE" : bUserName;
        }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }
        public string PastaStream { get; set; }
        public string PastaAudios { get; set; }
        public string PastaSE { get; set; }
        public string CaminhoConfigSE { get; set; }
        public string CaminhoBanco { get; set; }

        // Construtor vazio necessário para deserialização
        public Ambiente()
        {
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
            PastaSE = CPH.GetGlobalVar<string>("caminhoPastaStreamElements", true) ?? "";

            PastaStream = Path.Combine(PastaRaiz, "Data", "YoutubeStream");
            PastaAudios = Path.Combine(PastaRaiz, "Data", "Audios");
            CaminhoConfigSE = Path.Combine(PastaSE, "ConfigSE.txt");
            CaminhoBanco = Path.Combine(PastaStream, "YoutubeStream.db");
        }
    }
}