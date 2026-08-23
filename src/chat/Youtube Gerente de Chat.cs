using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Atualização 260822.1600
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

            // Log de chat - Salvar mensagem na tabela YoutubeChatLog
            CPH.RunAction("Youtube Salvar Mensagem", true);

            // Comandos de Chat
            if (!string.IsNullOrEmpty(evento.MessageText) && evento.MessageText.StartsWith("!"))
            {
                EncaminharComando(evento.MessageText, evento.UserName);
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE DE CHAT] ERRO CRÍTICO: {ex.Message}");
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
            case "!importar":
                CPH.ExecuteMethod("Youtube Importar Moedas SE", "ImportarMoedasStreamElements");
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
                    CPH.RunAction("Youtube Compara Palavra", false);
                }
                else
                {
                    CPH.SendYouTubeMessage($"@{userName} - Comando desconhecido: {comando}");
                    CPH.LogDebug($">>> [GERENTE DE CHAT] @{userName} - Comando desconhecido: {comando}");
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
        public string MessageText { get; set; }
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
            CPH.TryGetArg("user", out string userName);
            CPH.TryGetArg("message", out string messageText);
            CPH.TryGetArg("broadcastUserId", out string bUserId);
            CPH.TryGetArg("broadcastUserName", out string bUserName);

            IsSub = isSub;
            IsSpo = isSpo;
            IsMod = isMod;
            UserId = userId;
            UserName = userName;
            MessageText = messageText;
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