using System;
using Newtonsoft.Json;

// Versão 260715.1950

public class CPHInline
{
    private static readonly object pontosSurpresaLock = new object();

    public bool Execute()
    {
        try
        {
            var evento = new Evento(CPH);

            var palavraSurpresa = CPH.GetGlobalVar<string>("pontosSurpresaPalavra", true);

            if (string.IsNullOrEmpty(palavraSurpresa)) return true;

            // Compara a palavra digitada de forma segura (case-insensitive)
            if (string.Equals(palavraSurpresa, evento.MessageText?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Travamos para garantir que só uma execução processe por vez.
                lock (pontosSurpresaLock)
                {
                    // Evita que o mesmo espectador ganhe mais de uma vez na mesma rodada
                    var x = CPH.GetGlobalVar<int>("pontosSurpresaGanhadoresCount", true);
                    string listaGanhadores = CPH.GetGlobalVar<string>("pontosSurpresaListaGanhadores", true) ?? "";
                    string buscaId = $"|{evento.UserId}|";

                    if (listaGanhadores.Contains(buscaId))
                    {
                        return true; // Ignora se ele já ganhou nesta rodada
                    }

                    // Adiciona o ID do usuário à lista de ganhadores temporária
                    listaGanhadores += buscaId;
                    CPH.SetGlobalVar("pontosSurpresaListaGanhadores", listaGanhadores, true);

                    // Calcula o prêmio base com base na posição do ganhador (x)
                    int pontosBase = (int)(1000 / Math.Pow(2, x));

                    // Incrementa multiplicador com base nas informações do usuário
                    int multiplicador = 1;
                    if (evento.IsSub) multiplicador++;
                    if (evento.IsSpo) multiplicador++;

                    int pontosFinais = pontosBase * multiplicador;

                    // Envia o payload de pontos para a Action de adicionar pontos
                    var payload = new
                    {
                        UserId = evento.UserId ?? evento.UserName,
                        UserName = evento.UserName,
                        Timestamp = DateTime.Now,
                        Origem = "Pontos Surpresa",
                        Pontos = pontosFinais,
                        BroadcastUserId = evento.BroadcastUserId ?? "",
                        BroadcastUserName = evento.BroadcastUserName
                    };

                    string json = JsonConvert.SerializeObject(payload);
                    CPH.SetArgument("pontosPayload", json);
                    CPH.RunAction("Youtube Adicionar Pontos", true);

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

                    string mensagemSucesso = $"{posicaoTexto}: @{evento.UserName} digitou rápido e ganhou {pontosFinais:N0} pontos!{detalheCargo}";
                    CPH.SendYouTubeMessage(mensagemSucesso, false);

                    // Incrementa o contador de ganhadores no banco de memória do bot
                    CPH.SetGlobalVar("pontosSurpresaGanhadoresCount", ++x, true);

                    // Se já bateu os 3 ganhadores, desativa imediatamente
                    if (x >= 3)
                    {
                        CPH.DisableAction("Youtube Compara Palavra");

                        CPH.UnsetGlobalVar("pontosSurpresaPalavra", true);
                        CPH.UnsetGlobalVar("pontosSurpresaGanhadoresCount", true);
                        CPH.UnsetGlobalVar("pontosSurpresaListaGanhadores", true);
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