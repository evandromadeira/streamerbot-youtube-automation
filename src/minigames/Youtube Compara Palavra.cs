using System;
using Newtonsoft.Json;

// Atualização 261003.1010
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
            Evento evento = contexto.Evento;

            var palavraSurpresa = CPH.GetGlobalVar<string>("moedasSurpresaPalavra", true);

            if (string.IsNullOrEmpty(palavraSurpresa)) return true;

            // Compara a palavra digitada de forma segura (case-insensitive)
            if (string.Equals(palavraSurpresa, evento.MessageText?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Travamos para garantir que só uma execução processe por vez.
                lock (moedasSurpresaLock)
                {
                    // Confere a rodada novamente depois de obter o bloqueio.
                    if (CPH.GetGlobalVar<string>("moedasSurpresaPalavra", true) != palavraSurpresa) return true;

                    // Evita que o mesmo espectador ganhe mais de uma vez na mesma rodada
                    var x = CPH.GetGlobalVar<int>("moedasSurpresaGanhadoresCount", true);
                    string listaGanhadores = CPH.GetGlobalVar<string>("moedasSurpresaListaGanhadores", true) ?? "";
                    string buscaId = $"|{(string.IsNullOrEmpty(evento.UserId) ? evento.UserName : evento.UserId)}|";
                    if (x >= 3) return true;

                    if (listaGanhadores.Contains(buscaId))
                    {
                        return true; // Ignora se ele já ganhou nesta rodada
                    }

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

                    string operacaoId = Guid.NewGuid().ToString("D");
                    CPH.SetArgument("adicionarCreditoStatus", "incerto");
                    CPH.SetArgument("adicionarErro", "");
                    CPH.SetArgument("adicionarDestinatarioId", "");
                    // Reserva antes da chamada. Uma exceção não pode permitir crédito duplicado.
                    CPH.SetGlobalVar("moedasSurpresaListaGanhadores", listaGanhadores + buscaId, true);
                    string erro = "";
                    bool executou = false;
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
                    if (!confirmado && !falhou) creditoStatus = "incerto";

                    if (falhou)
                    {
                        // Não houve crédito: libera o participante e mantém a posição disponível.
                        CPH.SetGlobalVar("moedasSurpresaListaGanhadores", listaGanhadores, true);
                    }
                    else
                    {
                        // Confirmado ou incerto: reserva a posição antes de qualquer mensagem.
                        CPH.SetGlobalVar("moedasSurpresaGanhadoresCount", x + 1, true);
                        if (x + 1 >= 3)
                        {
                            CPH.UnsetGlobalVar("moedasSurpresaPalavra", true);
                            CPH.UnsetGlobalVar("moedasSurpresaGanhadoresCount", true);
                            CPH.UnsetGlobalVar("moedasSurpresaListaGanhadores", true);
                        }
                    }

                    if (!confirmado || !executou || !string.IsNullOrEmpty(erroCredito))
                    {
                        CPH.LogError(">>> [COMPARA_PALAVRA] RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { operacaoId, timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), origem = "moedas_surpresa", pastaStreamerBot = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true), evento.UserId, evento.UserName, evento.BroadcastUserId, evento.BroadcastUserName, destinatarioId, palavraSurpresa, posicao = x + 1, quantidade = moedasFinais, creditoStatus, erro, erroCredito, orientacao = falhou ? "Crédito não realizado; nova tentativa permitida nesta rodada. Confira tentativas posteriores antes de qualquer ajuste manual." : "Posição reservada. Confira o banco antes de qualquer ajuste; não repetir crédito confirmado ou incerto." }));
                    }
                    if (!confirmado)
                    {
                        Avisar(falhou ? $"❌ @{evento.UserName}, não foi possível creditar o prêmio. Tente a palavra novamente enquanto a rodada estiver aberta." : $"⚠ @{evento.UserName}, crédito do prêmio incerto. Posição reservada; moderação, confira o log e o banco antes de adicionar moedas.");
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
                    Avisar(mensagemSucesso);
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

    private void Avisar(string mensagem)
    {
        try
        {
            if (mensagem.Length > 200) mensagem = mensagem.Substring(0, 197) + "...";
            CPH.SendYouTubeMessage(mensagem, false);
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [COMPARA_PALAVRA] Falha ao enviar aviso ao chat: " + ex.Message);
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