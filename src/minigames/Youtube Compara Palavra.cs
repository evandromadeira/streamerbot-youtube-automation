using System;
using Newtonsoft.Json;

// Atualização 260820.2015
public class CPHInline
{
    private static readonly object moedasSurpresaLock = new object();

    public bool Execute()
    {
        try
        {
            var evento = new Evento(CPH);

            var palavraSurpresa = CPH.GetGlobalVar<string>("moedasSurpresaPalavra", true);

            if (string.IsNullOrEmpty(palavraSurpresa)) return true;

            // Compara a palavra digitada de forma segura (case-insensitive)
            if (string.Equals(palavraSurpresa, evento.MessageText?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Travamos para garantir que só uma execução processe por vez.
                lock (moedasSurpresaLock)
                {
                    // Evita que o mesmo espectador ganhe mais de uma vez na mesma rodada
                    var x = CPH.GetGlobalVar<int>("moedasSurpresaGanhadoresCount", true);
                    string listaGanhadores = CPH.GetGlobalVar<string>("moedasSurpresaListaGanhadores", true) ?? "";
                    string buscaId = $"|{evento.UserId}|";

                    if (listaGanhadores.Contains(buscaId))
                    {
                        return true; // Ignora se ele já ganhou nesta rodada
                    }

                    // Adiciona o ID do usuário à lista de ganhadores temporária
                    listaGanhadores += buscaId;
                    CPH.SetGlobalVar("moedasSurpresaListaGanhadores", listaGanhadores, true);

                    // Calcula o prêmio base com base na posição do ganhador (x)
                    int moedasBase = (int)(1000 / Math.Pow(2, x));

                    // Incrementa multiplicador com base nas informações do usuário
                    int multiplicador = 1;
                    if (evento.IsSub) multiplicador++;
                    if (evento.IsSpo) multiplicador++;

                    int moedasFinais = moedasBase * multiplicador;

                    CPH.SetArgument("origem", "moedas_surpresa");
                    CPH.SetArgument("targetUserId", evento.UserId ?? evento.UserName);
                    CPH.SetArgument("targetUserName", evento.UserName);
                    CPH.SetArgument("coinsToAdd", moedasFinais);
                    CPH.SetArgument("broadcastUserId", evento.BroadcastUserId);
                    CPH.SetArgument("broadcastUserName", evento.BroadcastUserName);

                    bool executou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
                    if (!executou)
                    {
                        CPH.LogError($">>> [YT COMPARA PALAVRA] ERRO CRÍTICO: Falha ao adicionar moedas para @{evento.UserName}.");
                        return false;
                    }

                    // Envia a mensagem comemorativa no chat destacando a posição e o bônus
                    string posicaoTexto = x switch
                    {
                        0 => "🥇 1º Lugar",
                        1 => "🥈 2º Lugar",
                        _ => "🥉 3º Lugar"
                    };

                    string detalheCargo = multiplicador switch
                    {
                        3 => " (Inscrito & Membro - 3x!)",
                        2 => evento.IsSpo ? " (Membro - 2x!)" : " (Inscrito - 2x!)",
                        _ => ""
                    };

                    string mensagemSucesso = $"{posicaoTexto}: @{evento.UserName} digitou rápido e ganhou {moedasFinais:N0} Moedas!{detalheCargo}";
                    CPH.SendYouTubeMessage(mensagemSucesso, false);

                    // Incrementa o contador de ganhadores no banco de memória do bot
                    CPH.SetGlobalVar("moedasSurpresaGanhadoresCount", ++x, true);

                    // Se já bateu os 3 ganhadores, desativa imediatamente
                    if (x >= 3)
                    {
                        CPH.DisableAction("Youtube Compara Palavra");

                        CPH.UnsetGlobalVar("moedasSurpresaPalavra", true);
                        CPH.UnsetGlobalVar("moedasSurpresaGanhadoresCount", true);
                        CPH.UnsetGlobalVar("moedasSurpresaListaGanhadores", true);
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [YT COMPARA PALAVRA] ERRO: " + ex.Message);
            return false;
        }
    }

    public class Evento
    {
        public string UserId { get; }
        public string UserName { get; }
        public string MessageText { get; }
        public string BroadcastUserId { get; }
        public string BroadcastUserName { get; }

        public bool IsSub { get; }
        public bool IsSpo { get; }

        public Evento(IInlineInvokeProxy CPH)
        {
            CPH.TryGetArg("isSubscribed", out bool isSub);
            CPH.TryGetArg("userIsSponsor", out bool isSpo);
            CPH.TryGetArg("user", out string userName);
            CPH.TryGetArg("userId", out string userId);
            CPH.TryGetArg("message", out string messageText);
            CPH.TryGetArg("broadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("broadcastUserName", out string broadcastUserName);

            IsSub = isSub;
            IsSpo = isSpo;
            UserName = userName;
            UserId = userId;
            MessageText = messageText;
            BroadcastUserId = broadcastUserId;
            BroadcastUserName = string.IsNullOrEmpty(broadcastUserName) ? "YOUTUBE" : broadcastUserName;
        }
    }
}