using System;

// Atualização 260823.0905
public class CPHInline
{
    public bool Execute()
    {
        Evento evento = null;
        
        try
        {
            evento = new Evento(CPH);
            
            CPH.LogInfo($"userId: {evento.UserId}");
            CPH.LogInfo($"userName: {evento.UserName}");
            CPH.LogInfo($"messageId: {evento.MessageId}");
            CPH.LogInfo($"message: {evento.MessageText}");
            CPH.LogInfo($"broadcastUserId: {evento.BroadcastUserId}");
            CPH.LogInfo($"broadcastUserName: {evento.BroadcastUserName}");
            CPH.LogInfo($"isSubscribed: {evento.IsSub}");
            CPH.LogInfo($"userIsSponsor: {evento.IsSpo}");
            CPH.LogInfo($"isModerator: {evento.IsMod}");
            CPH.LogInfo($"userPreviousActive: {evento.UserPreviousActive}");
            CPH.LogInfo($"publishedAt: {evento.PublishedAt}");

            CPH.SetArgument("chatLogUserId", evento.UserId);
            CPH.SetArgument("chatLogUserName", evento.UserName);
            CPH.SetArgument("chatLogMessageId", evento.MessageId);
            CPH.SetArgument("chatLogMessage", evento.MessageText);
            CPH.SetArgument("chatLogBroadcastUserId", evento.BroadcastUserId);
            CPH.SetArgument("chatLogBroadcastUserName", evento.BroadcastUserName);
            CPH.SetArgument("chatLogIsSubscribed", evento.IsSub);
            CPH.SetArgument("chatLogIsSponsor", evento.IsSpo);
            CPH.SetArgument("chatLogIsModerator", evento.IsMod);
            CPH.SetArgument("chatLogUserPreviousActive", evento.UserPreviousActive);
            CPH.SetArgument("chatLogPublishedAt", evento.PublishedAt);

            bool salvou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SalvarChatLog");
            if (!salvou)
            {
                CPH.LogError(">>> [CHAT_LOG] ERRO: falha ao salvar mensagem no banco de dados.");
                return false;
            }

            CPH.LogDebug(">>> [CHAT_LOG] DADO SALVO COM SUCESSO!");


            CreditarMoedasChat(evento);

            return true;
        }
        catch (Exception ex)
        {
			CPH.LogError(">>> [CHAT_LOG] ERRO CRÍTICO: " + ex.Message);
            return false;
        }
    }

    private void CreditarMoedasChat(Evento evento)
    {
        int moedasPorMensagem = 10;
        int cooldownMinutos = 10;
        int multiplicador = 1;

        if (evento.IsSpo) multiplicador++;
        if (evento.IsSub) multiplicador++;

        moedasPorMensagem *= multiplicador;

        CPH.SetArgument("origem", "chat_atividade");
        CPH.SetArgument("targetUserId", evento.UserId);
        CPH.SetArgument("targetUserName", evento.UserName);
        CPH.SetArgument("coinsToAdd", moedasPorMensagem);
        CPH.SetArgument("cooldownMinutos", cooldownMinutos);
        CPH.SetArgument("broadcastUserId", evento.BroadcastUserId);
        CPH.SetArgument("broadcastUserName", evento.BroadcastUserName);

        bool creditou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
        if (!creditou)
        {
            CPH.LogError($">>> [CHAT_LOG] ERRO: falha ao creditar moedas de atividade de chat para @{evento.UserName}.");
        }
    }

    public class Evento
    {
        public string UserId { get; }
        public string UserName { get; }
        public string MessageId { get; }
        public string MessageText { get; }
        public string BroadcastUserId { get; }
        public string BroadcastUserName { get; }

        public bool IsSub { get; }
        public bool IsSpo { get; }
        public bool IsMod { get; }

        public string UserPreviousActive { get; }
        public string PublishedAt { get; }

        public Evento(IInlineInvokeProxy CPH)
        {
            CPH.TryGetArg("userId", out string userId);
            CPH.TryGetArg("userName", out string userName);
            CPH.TryGetArg("messageId", out string messageId);
            CPH.TryGetArg("message", out string messageText);
            CPH.TryGetArg("broadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("broadcastUserName", out string broadcastUserName);

            CPH.TryGetArg("isSubscribed", out bool isSub);
            CPH.TryGetArg("userIsSponsor", out bool isSpo);
            CPH.TryGetArg("isModerator", out bool isMod);

            CPH.TryGetArg("userPreviousActive", out DateTime userPreviousActive);

            UserId = userId;
            UserName = userName;
            MessageId = messageId;
            MessageText = messageText;
            BroadcastUserId = broadcastUserId;
            BroadcastUserName = string.IsNullOrEmpty(broadcastUserName) ? "YOUTUBE" : broadcastUserName;

            IsSub = isSub;
            IsSpo = isSpo;
            IsMod = isMod;

            UserPreviousActive = userPreviousActive != DateTime.MinValue ? userPreviousActive.ToString("yyyy-MM-dd HH:mm:ss") : "Primeira Mensagem";
            PublishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}