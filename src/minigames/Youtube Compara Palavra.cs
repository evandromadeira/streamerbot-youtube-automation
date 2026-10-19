using System;
using Newtonsoft.Json;

// Atualização 261015.1120
public class CPHInline
{
    private const int PremioBase = 1000;
    private const int LimiteGanhadores = 3;
    private const int LimiteMensagem = 200;

    private const string Prefixo = "[COMPARA_PALAVRA] ";

    private static readonly object moedasSurpresaLock = new object();

    public bool CompararPalavra()
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

            string palavraSurpresa = CPH.GetGlobalVar<string>("moedasSurpresaPalavra", false);
            if (string.IsNullOrEmpty(palavraSurpresa))
            {
                return true;
            }

            // Compara a palavra digitada de forma segura (case-insensitive)
            if (string.Equals(palavraSurpresa, evento.MessageText?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Travamos para garantir que só uma execução processe por vez.
                lock (moedasSurpresaLock)
                {
                    // Confere a rodada novamente depois de obter o bloqueio.
                    if (CPH.GetGlobalVar<string>("moedasSurpresaPalavra", false) != palavraSurpresa)
                    {
                        return true;
                    }

                    // Evita que o mesmo espectador ganhe mais de uma vez na mesma rodada
                    int x = CPH.GetGlobalVar<int>("moedasSurpresaGanhadoresCount", false);

                    string listaGanhadores = CPH.GetGlobalVar<string>("moedasSurpresaListaGanhadores", false) ?? "";
                    string buscaId = $"|{(string.IsNullOrEmpty(evento.UserId) ? evento.UserName : evento.UserId)}|";

                    if (x >= LimiteGanhadores)
                    {
                        return true;
                    }

                    if (listaGanhadores.Contains(buscaId))
                    {
                        return true; // Ignora se ele já ganhou nesta rodada
                    }

                    // Calcula o prêmio base com base na posição do ganhador (x)
                    int moedasBase = (int)(PremioBase / Math.Pow(2, x));

                    // Incrementa multiplicador com base nas informações do usuário
                    int multiplicador = 1;
                    if (evento.IsSub)
                    {
                        multiplicador++;
                    }
                    if (evento.IsSpo)
                    {
                        multiplicador++;
                    }

                    int moedasFinais = moedasBase * multiplicador;

                    CPH.SetArgument("origem", "moedas_surpresa");
                    CPH.SetArgument("targetUserId", evento.UserId ?? evento.UserName);
                    CPH.SetArgument("targetUserName", evento.UserName);
                    CPH.SetArgument("coinsToAdd", moedasFinais);
                    CPH.SetArgument("broadcastUserId", evento.BroadcastUserId);
                    CPH.SetArgument("broadcastUserName", evento.BroadcastUserName);

                    string operacaoId = Guid.NewGuid().ToString("D");

                    CPH.SetArgument("adicionarCreditoStatus", "incerto");
                    CPH.SetArgument("adicionarErro", "");
                    CPH.SetArgument("adicionarDestinatarioId", "");
                    // Reserva antes da chamada. Uma exceção não pode permitir crédito duplicado.
                    CPH.SetGlobalVar("moedasSurpresaListaGanhadores", listaGanhadores + buscaId, false);

                    bool executou = false;

                    string erro = "";

                    try
                    {
                        executou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
                    }
                    catch (Exception ex)
                    {
                        erro = ex.Message;
                    }
                    CPH.TryGetArg("adicionarCreditoStatus", out string creditoStatus);
                    CPH.TryGetArg("adicionarErro", out string erroCredito);
                    CPH.TryGetArg("adicionarDestinatarioId", out string destinatarioId);

                    bool confirmado = creditoStatus == "creditado";
                    bool falhou = creditoStatus == "falhou";
                    if (!confirmado && !falhou)
                    {
                        creditoStatus = "incerto";
                    }

                    if (falhou)
                    {
                        // Não houve crédito: libera o participante e mantém a posição disponível.
                        CPH.SetGlobalVar("moedasSurpresaListaGanhadores", listaGanhadores, false);
                    }
                    else
                    {
                        // Confirmado ou incerto: reserva a posição antes de qualquer mensagem.
                        CPH.SetGlobalVar("moedasSurpresaGanhadoresCount", x + 1, false);
                        if (x + 1 >= LimiteGanhadores)
                        {
                            CPH.UnsetGlobalVar("moedasSurpresaPalavra", false);
                            CPH.UnsetGlobalVar("moedasSurpresaGanhadoresCount", false);
                            CPH.UnsetGlobalVar("moedasSurpresaListaGanhadores", false);
                        }
                    }

                    if (!confirmado || !executou || !string.IsNullOrEmpty(erroCredito))
                    {
                        LogError("RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { operacaoId, timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), origem = "moedas_surpresa", pastaStreamerBot = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true), evento.UserId, evento.UserName, evento.BroadcastUserId, evento.BroadcastUserName, destinatarioId, palavraSurpresa, posicao = x + 1, quantidade = moedasFinais, creditoStatus, erro, erroCredito, orientacao = falhou ? "Crédito não realizado; nova tentativa permitida nesta rodada. Confira tentativas posteriores antes de qualquer ajuste manual." : "Posição reservada. Confira o banco antes de qualquer ajuste; não repetir crédito confirmado ou incerto." }));
                    }
                    if (!confirmado)
                    {
                        Avisar(falhou ? $"❌ @{evento.UserName}, não foi possível creditar o prêmio. Tente a palavra novamente enquanto a rodada estiver aberta." : $"⚠ @{evento.UserName}, crédito do prêmio incerto. Posição reservada; moderação, confira o log e o banco antes de adicionar moedas.", evento.BroadcastId, somenteNaTransmissao: true);
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

                    Avisar(mensagemSucesso, evento.BroadcastId, somenteNaTransmissao: true);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO: " + ex.Message);
            return false;
        }
    }

    private void Avisar(string mensagem, string broadcastId, bool somenteNaTransmissao)
    {
        try
        {
            if (mensagem.Length > LimiteMensagem)
            {
                mensagem = mensagem.Substring(0, LimiteMensagem - 3) + "...";
            }

            EnviarMensagem(mensagem, broadcastId, somenteNaTransmissao);
        }
        catch (Exception ex)
        {
            LogError("Falha ao enviar aviso ao chat: " + ex.Message);
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

    public class Evento
    {
        public bool IsSub { get; set; }
        public bool IsSpo { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }
}