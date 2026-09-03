using System;
using Newtonsoft.Json;

// Atualização 260903.1655
public class CPHInline
{
    private static readonly object moedasSurpresaLock = new object();

    public bool CompararPalavra()
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [COMPARA_PALAVRA] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            var evento = contexto.Evento;

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
                        CPH.LogError($">>> [COMPARA_PALAVRA] ERRO CRÍTICO: Falha ao adicionar moedas para @{evento.UserName}.");
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
            CPH.LogError(">>> [COMPARA_PALAVRA] ERRO: " + ex.Message);
            return false;
        }
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
    }

    private Contexto ObterContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrEmpty(contextoJson))
            return null;

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class Evento
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
        public bool IsSub { get; set; }
        public bool IsSpo { get; set; }
    }
}