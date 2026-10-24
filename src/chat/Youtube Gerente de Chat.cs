using System;
using System.IO;
using Newtonsoft.Json;

// Atualização 261022.0630
// Padrões aplicados: 5
// Revisão concluída: 1
public class CPHInline
{
    private const string Prefixo = "[GERENTE_DE_CHAT] ";

    public bool Execute()
    {
        try
        {
            Chat chat = LerChat();

            Ambiente ambiente = new Ambiente(CPH);
            Contexto contexto = new Contexto
            {
                Chat = chat,
                Ambiente = ambiente
            };
            var broadcaster = CPH.YouTubeGetBroadcaster();

            chat.BroadcastUserId = broadcaster?.UserId ?? "";
            chat.BroadcastUserName = string.IsNullOrEmpty(broadcaster?.UserName) ? "YOUTUBE" : broadcaster?.UserName;

            // Serializa para JSON uma única vez
            string contextoJson = JsonConvert.SerializeObject(contexto);
            CPH.SetArgument("contextoJson", contextoJson);

            // Youtube Gerente de Banco de Dados: registra a mensagem e o usuário.
            PrepararArgumentosChatLog(chat);

            bool salvou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SalvarChatLog");
            if (!salvou)
            {
                LogError("ERRO: falha ao salvar mensagem no banco de dados.");
            }

            // Youtube Gerente de Moedas: recompensa a atividade no chat.
            bool creditou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "RecompensarAtividadeChat");
            if (!creditou)
            {
                LogError("ERRO: falha ao creditar moedas para o usuário.");
            }

            // Comandos de Chat
            if (!string.IsNullOrEmpty(chat.MessageText) && chat.MessageText.StartsWith("!"))
            {
                EncaminharComando(chat.MessageText, chat.UserName);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"ERRO CRÍTICO: {ex.Message}");
            return false;
        }
    }

    private void EncaminharComando(string message, string userName)
    {
        // Extrai o comando base (a primeira palavra da mensagem)
        string comando = message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        switch (comando)
        {
            case "!meta":
                CPH.ExecuteMethod("Youtube Consultar Meta", "ConsultarMeta");
                break;
            case "!novoaudio":
                CPH.ExecuteMethod("Youtube Gerente de Áudio", "CadastrarAudio");
                break;
            case "!audios":
                CPH.ExecuteMethod("Youtube Gerente de Áudio", "ListarAudios");
                break;
            case "!presenca":
                CPH.ExecuteMethod("Youtube Gerente de Estatísticas", "ConsultarPresenca");
                break;
            case "!presencames":
                CPH.ExecuteMethod("Youtube Gerente de Estatísticas", "ConsultarPresencaMes");
                break;
            case "!minecraft":
                CPH.ExecuteMethod("Youtube Gerente de Minecraft", "ProcessarComando");
                break;
            case "!saldo":
                CPH.ExecuteMethod("Youtube Gerente de Moedas", "SaldoMoedasUsuario");
                break;
            case "!adicionar":
                CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
                break;
            case "!transferir":
                CPH.ExecuteMethod("Youtube Gerente de Moedas", "TransferirMoedasUsuario");
                break;
            case "!topmoedas":
                CPH.ExecuteMethod("Youtube Gerente de Moedas", "TopMoedas");
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
            case "!perfil":
                CPH.ExecuteMethod("Youtube Gerente de Perfil", "ProcessarComando");
                break;
            case "!plataforma":
                CPH.ExecuteMethod("Youtube Gerente de Plataforma", "ProcessarComando");
                break;
            case "!timer":
                CPH.ExecuteMethod("Youtube Gerente de Timer", "ProcessarComando");
                break;
            case "!importar":
                CPH.ExecuteMethod("Youtube Importar Moedas SE", "ImportarMoedasStreamElements");
                break;
            default:
                bool audioTocado = CPH.ExecuteMethod("Youtube Gerente de Áudio", "ReproduzirAudio");
                if (audioTocado)
                {
                    break;
                }

                // Desafio de Palavra Surpresa
                string moedasSurpresaPalavra = CPH.GetGlobalVar<string>("moedasSurpresaPalavra", false);
                if (!string.IsNullOrEmpty(moedasSurpresaPalavra))
                {
                    CPH.ExecuteMethod("Youtube Compara Palavra", "CompararPalavra");
                }
                else
                {
                    LogInfo($"@{userName} - Comando desconhecido: {comando}");
                }

                break;
        }
    }

    private void PrepararArgumentosChatLog(Chat chat)
    {
        // Prepara os argumentos do registro sem alterar o contexto enviado aos outros gerentes.
        CPH.SetArgument("chatLogIsSubscribed", chat.IsSub);
        CPH.SetArgument("chatLogIsSponsor", chat.IsSpo);
        CPH.SetArgument("chatLogIsModerator", chat.IsMod);
        CPH.SetArgument("chatLogUserId", chat.UserId);
        CPH.SetArgument("chatLogUserName", chat.UserName);
        CPH.SetArgument("chatLogMessageId", chat.MessageId);
        CPH.SetArgument("chatLogMessage", chat.MessageText);
        CPH.SetArgument("chatLogPublishedAt", chat.PublishedAt);
        CPH.SetArgument("chatLogUserPreviousActive", chat.UserPreviousActive);
        CPH.SetArgument("chatLogBroadcastUserId", chat.BroadcastUserId);
        CPH.SetArgument("chatLogBroadcastUserName", chat.BroadcastUserName);
        CPH.SetArgument("chatLogBroadcastId", chat.BroadcastId);
    }

    private void LogError(string mensagem)
    {
        CPH.LogError(Prefixo + mensagem);
    }

    private void LogInfo(string mensagem)
    {
        CPH.LogInfo(Prefixo + mensagem);
    }

    private Chat LerChat()
    {
        Chat chat = new Chat();

        CPH.TryGetArg("isSubscribed", out bool isSub);
        CPH.TryGetArg("userIsSponsor", out bool isSpo);
        CPH.TryGetArg("isModerator", out bool isMod);
        CPH.TryGetArg("userId", out string userId);
        CPH.TryGetArg("userName", out string userName);
        CPH.TryGetArg("messageId", out string messageId);
        CPH.TryGetArg("message", out string messageText);
        CPH.TryGetArg("userPreviousActive", out DateTime userPreviousActive);
        CPH.TryGetArg("broadcast.id", out string broadcastId);

        chat.IsSub = isSub;
        chat.IsSpo = isSpo;
        chat.IsMod = isMod;
        chat.UserId = userId;
        chat.UserName = userName;
        chat.MessageId = messageId;
        chat.MessageText = messageText;
        chat.PublishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        chat.UserPreviousActive = userPreviousActive != DateTime.MinValue ? userPreviousActive.ToString("yyyy-MM-dd HH:mm:ss") : "Primeira Mensagem";
        chat.BroadcastId = broadcastId;

        return chat;
    }

    public class Contexto
    {
        [JsonProperty("Evento")]
        public Chat Chat { get; set; }

        public Ambiente Ambiente { get; set; }
    }

    public class Chat
    {
        public bool IsSub { get; set; }
        public bool IsSpo { get; set; }
        public bool IsMod { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageId { get; set; }
        public string MessageText { get; set; }
        public string PublishedAt { get; set; }
        public string UserPreviousActive { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; } = "";
        public string PastaSE { get; set; } = "";

        public string PastaStream => Path.Combine(PastaRaiz, "Data", "YoutubeStream");
        public string PastaAudios => Path.Combine(PastaRaiz, "Data", "Audios");
        public string CaminhoBanco => Path.Combine(PastaStream, "YoutubeStream.db");
        public string CaminhoConfigSE => Path.Combine(PastaSE, "ConfigSE.txt");

        public Ambiente()
        {
            // Construtor vazio necessário para desserialização do contexto.
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
            PastaSE = CPH.GetGlobalVar<string>("caminhoPastaStreamElements", true) ?? "";
        }
    }
}