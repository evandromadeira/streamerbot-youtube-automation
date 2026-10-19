using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

// Atualização 261016.0800
public class CPHInline
{
    private const string Prefixo = "[GERENTE_PALPITE] ";

    public bool IniciarPalpite()
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

            if (!evento.IsMod)
            {
                EnviarMensagem($"@{evento.UserName} - apenas moderadores podem iniciar um palpite.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            string mensagem = evento.MessageText ?? "";

            int primeiroEspaco = mensagem.IndexOf(' ');

            string resto = primeiroEspaco >= 0 ? mensagem.Substring(primeiroEspaco + 1).Trim() : "";

            var matchTempo = Regex.Match(resto, @"^(\d+)([msh])(\s+|$)", RegexOptions.IgnoreCase);
            if (!matchTempo.Success)
            {
                EnviarMensagem($"@{evento.UserName} - sintaxe: !iniciarpalpite [tempo, ex: 3m] [descrição] | [opção1] ; [opção2]", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            int valorTempo = int.Parse(matchTempo.Groups[1].Value);

            string unidadeTempo = matchTempo.Groups[2].Value.ToLowerInvariant();

            int durationSeconds = unidadeTempo == "h" ? valorTempo * 3600 : unidadeTempo == "m" ? valorTempo * 60 : valorTempo;

            string restoAposTempo = resto.Substring(matchTempo.Length).Trim();

            int posPipe = restoAposTempo.IndexOf('|');
            if (posPipe < 0)
            {
                EnviarMensagem($"@{evento.UserName} - faltou o separador '|' entre a descrição e as opções.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            string description = restoAposTempo.Substring(0, posPipe).Trim();
            string optionsRaw = restoAposTempo.Substring(posPipe + 1);

            var options = optionsRaw.Split(';')
                .Select(o => o.Trim())
                .Where(o => !string.IsNullOrEmpty(o))
                .ToList();

            if (string.IsNullOrEmpty(description) || options.Count < 2)
            {
                EnviarMensagem($"@{evento.UserName} - precisa de uma descrição e pelo menos 2 opções separadas por ';'.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            string agora = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string endsAt = DateTime.Now.AddSeconds(durationSeconds).ToString("yyyy-MM-dd HH:mm:ss");

            CPH.SetArgument("novoPalpiteDescription", description);
            CPH.SetArgument("novoPalpiteOptions", string.Join(";", options));
            CPH.SetArgument("novoPalpiteDurationSeconds", durationSeconds);
            CPH.SetArgument("novoPalpiteCreatedAt", agora);
            CPH.SetArgument("novoPalpiteEndsAt", endsAt);
            CPH.SetArgument("novoPalpiteCreatedByUserId", evento.UserId);
            CPH.SetArgument("novoPalpiteCreatedByUserName", evento.UserName);
            CPH.SetArgument("novoPalpiteBroadcastUserId", evento.BroadcastUserId);
            CPH.SetArgument("novoPalpiteBroadcastUserName", evento.BroadcastUserName);

            if (!CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "CriarPalpite"))
            {
                LogError("ERRO: falha ao executar CriarPalpite no banco de dados.");
                EnviarMensagem($"@{evento.UserName} - falha técnica ao criar o palpite.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("criarPalpiteResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    string listaOpcoes = string.Join(" | ", options.Select((o, i) => $"{(char)('a' + i)}) {o}"));
                    EnviarMensagem($"🎲 PALPITE ABERTO: {description} — {listaOpcoes} — participe com !palpite [letra] [valor], você tem {FormatarDuracao(durationSeconds)}!", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "RodadaJaAberta":
                    EnviarMensagem($"@{evento.UserName} - já existe um palpite em andamento. Aguarde ele encerrar.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                default:
                    LogError("ERRO: falha ao criar palpite no banco de dados.");
                    EnviarMensagem($"@{evento.UserName} - erro ao criar o palpite, tenta de novo.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"ERRO CRÍTICO em IniciarPalpite: {ex.Message}");
            return false;
        }
    }

    public bool ResolverPalpite()
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

            if (!evento.IsMod)
            {
                EnviarMensagem($"@{evento.UserName} - apenas moderadores podem declarar o resultado de um palpite.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            string mensagem = evento.MessageText ?? "";

            var partes = mensagem.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            string opcaoVencedora = partes.Length == 2 ? partes[1].ToLowerInvariant() : "";
            if (opcaoVencedora.Length != 1 || opcaoVencedora[0] < 'a' || opcaoVencedora[0] > 'z')
            {
                EnviarMensagem($"@{evento.UserName} - sintaxe: !resultadopalpite [letra]", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            CPH.SetArgument("resolverPalpiteOpcaoVencedora", opcaoVencedora);

            if (!CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ResolverPalpite"))
            {
                LogError("ERRO: falha ao executar ResolverPalpite no banco de dados.");
                EnviarMensagem($"@{evento.UserName} - falha técnica ao declarar o resultado do palpite.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("resolverPalpiteResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    CPH.TryGetArg("resolverPalpiteDescription", out string description);
                    CPH.TryGetArg("resolverPalpiteTotalPago", out int totalPago);
                    CPH.TryGetArg("resolverPalpiteQtdVencedores", out int qtdVencedores);

                    EnviarMensagem($"🏆 Palpite \"{description}\" encerrado! Opção vencedora: {opcaoVencedora}) — {qtdVencedores} vencedor(es) dividiram {totalPago:N0} moeda(s) do canal!", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "SemGanhadores":
                    EnviarMensagem("😬 Ninguém apostou na opção vencedora — palpite cancelado, moedas do canal devolvidas a todos.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "SemRodadaAberta":
                    EnviarMensagem($"@{evento.UserName} - não há nenhum palpite aberto no momento.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "OpcaoInvalida":
                    EnviarMensagem($"@{evento.UserName} - opção '{opcaoVencedora}' não existe nesse palpite.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                default:
                    LogError("ERRO: falha ao resolver palpite.");
                    EnviarMensagem($"@{evento.UserName} - erro ao declarar o resultado, tenta de novo.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"ERRO CRÍTICO em ResolverPalpite: {ex.Message}");
            return false;
        }
    }

    public bool CancelarPalpite()
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

            if (!evento.IsMod)
            {
                EnviarMensagem($"@{evento.UserName} - apenas moderadores podem cancelar um palpite.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            if (!CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "CancelarPalpite"))
            {
                LogError("ERRO: falha ao executar CancelarPalpite no banco de dados.");
                EnviarMensagem($"@{evento.UserName} - falha técnica ao cancelar o palpite.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("cancelarPalpiteResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    EnviarMensagem("🚫 Palpite cancelado, moedas do canal apostadas devolvidas a todos os participantes.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "SemRodadaAberta":
                    EnviarMensagem($"@{evento.UserName} - não há nenhum palpite aberto no momento.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                default:
                    LogError("ERRO: falha ao cancelar palpite.");
                    EnviarMensagem($"@{evento.UserName} - erro ao cancelar o palpite, tenta de novo.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"ERRO CRÍTICO em Cancelar: {ex.Message}");
            return false;
        }
    }

    public bool ApostarPalpite()
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

            string mensagem = evento.MessageText ?? "";

            var partes = mensagem.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 3)
            {
                EnviarMensagem($"@{evento.UserName} - sintaxe: !palpite [opção] [valor], ex: !palpite a 50", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            string opcao = partes[1].ToLowerInvariant();
            if (opcao.Length != 1 || opcao[0] < 'a' || opcao[0] > 'z')
            {
                EnviarMensagem($"@{evento.UserName} - opção inválida, use a letra da opção (a, b, c...).", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            if (!int.TryParse(partes[2], out int valor) || valor <= 0)
            {
                EnviarMensagem($"@{evento.UserName} - valor inválido, use um número inteiro positivo de moedas do canal.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            CPH.SetArgument("apostarPalpiteUserId", evento.UserId);
            CPH.SetArgument("apostarPalpiteUserName", evento.UserName);
            CPH.SetArgument("apostarPalpiteOption", opcao);
            CPH.SetArgument("apostarPalpiteValor", valor);
            CPH.SetArgument("apostarPalpiteBroadcastUserId", evento.BroadcastUserId);

            if (!CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ApostarPalpite"))
            {
                LogError("ERRO: falha ao executar ApostarPalpite no banco de dados.");
                EnviarMensagem($"@{evento.UserName} - falha técnica ao registrar a aposta.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("apostarPalpiteResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    CPH.TryGetArg("apostarPalpiteTotalUsuario", out int totalUsuario);
                    EnviarMensagem($"@{evento.UserName} apostou {valor:N0} moedas do canal na opção '{opcao}'. (Total: {totalUsuario:N0})", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "SemRodadaAberta":
                    EnviarMensagem($"@{evento.UserName} - não há nenhum palpite aberto no momento.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "RodadaEncerrada":
                    EnviarMensagem($"@{evento.UserName} - o tempo desse palpite já acabou, aguarde um moderador declarar o resultado.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "OpcaoInvalida":
                    EnviarMensagem($"@{evento.UserName} - opção '{opcao}' não existe nesse palpite.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "SaldoInsuficiente":
                    CPH.TryGetArg("apostarPalpiteSaldoAtual", out int saldoAtual);
                    EnviarMensagem($"@{evento.UserName} - saldo insuficiente (você tem {saldoAtual:N0} moeda(s) do canal).", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                case "OpcaoDiferente":
                    CPH.TryGetArg("apostarPalpiteOpcaoAtual", out string opcaoAtual);
                    EnviarMensagem($"@{evento.UserName} - você já apostou na opção '{opcaoAtual}' nessa rodada, não dá pra trocar.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
                default:
                    LogError("ERRO: falha ao registrar aposta.");
                    EnviarMensagem($"@{evento.UserName} - erro ao registrar sua aposta, tenta de novo.", evento?.BroadcastId, somenteNaTransmissao: true);
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"ERRO CRÍTICO em ApostarPalpite: {ex.Message}");
            return false;
        }
    }

    public bool VerificarEncerramentoPalpite()
    {
        try
        {
            if (!CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "VerificarEncerramentoPalpite"))
            {
                LogError("ERRO: falha ao executar VerificarEncerramentoPalpite no banco de dados.");
                return false;
            }

            CPH.TryGetArg("palpiteEncerradoEncontrado", out bool encontrado);
            if (encontrado)
            {
                CPH.TryGetArg("palpiteEncerradoDescription", out string description);
                CPH.TryGetArg("palpiteEncerradoOptions", out string optionsRaw);
                CPH.TryGetArg("palpiteEncerradoTotaisJson", out string totaisJson);

                var options = (optionsRaw ?? "").Split(';');
                var totais = JsonConvert.DeserializeObject<Dictionary<string, int>>(totaisJson ?? "{}") ?? new Dictionary<string, int>();

                int poteTotal = totais.Values.Sum();

                string resumo = string.Join(" | ", options.Select((opcao, indice) =>
                {
                    string letra = ((char)('a' + indice)).ToString();

                    int total = totais.ContainsKey(letra) ? totais[letra] : 0;

                    string multiplicador = total > 0 ? $" ({(double)poteTotal / total:0.00}x)" : " (sem apostas)";
                    return $"{letra}) {opcao}: {total:N0}{multiplicador}";
                }));

                string broadcastId = "";
                try
                {
                    broadcastId = CPH.YouTubeGetMonitoredBroadcasts()?.FirstOrDefault(b => b.Title?.Contains("🔴") == true)?.Id ?? "";
                }
                catch (Exception consultaEx)
                {
                    LogWarn("Não foi possível identificar a transmissão: " + consultaEx.Message);
                }

                EnviarMensagem($"⏰ Tempo esgotado! Palpite \"{description}\" encerrado! {resumo}", broadcastId, somenteNaTransmissao: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"ERRO CRÍTICO em VerificarEncerramentoPalpite: {ex.Message}");
            return false;
        }
    }

    private string FormatarDuracao(int segundos)
    {
        if (segundos % 60 == 0)
        {
            return $"{segundos / 60} minuto(s)";
        }

        return $"{segundos} segundo(s)";
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

    private void LogWarn(string mensagem)
    {
        CPH.LogWarn(Prefixo + mensagem);
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
        public bool IsMod { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }
}