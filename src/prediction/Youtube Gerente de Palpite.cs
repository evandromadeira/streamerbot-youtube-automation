using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

// Atualização 260903.1615
public class CPHInline
{
    public bool IniciarPalpite()
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_DE_PALPITE] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            var evento = contexto.Evento;

            if (!evento.IsMod)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - apenas moderadores podem iniciar um palpite.");
                return true;
            }

            string mensagem = evento.MessageText ?? "";
            int primeiroEspaco = mensagem.IndexOf(' ');
            string resto = primeiroEspaco >= 0 ? mensagem.Substring(primeiroEspaco + 1).Trim() : "";

            var matchTempo = Regex.Match(resto, @"^(\d+)([msh])(\s+|$)", RegexOptions.IgnoreCase);
            if (!matchTempo.Success)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - sintaxe: !iniciarpalpite [tempo, ex: 3m] [descrição] | [opção1] ; [opção2]");
                return true;
            }

            int valorTempo = int.Parse(matchTempo.Groups[1].Value);
            string unidadeTempo = matchTempo.Groups[2].Value.ToLower();
            int durationSeconds = unidadeTempo == "h" ? valorTempo * 3600 : unidadeTempo == "m" ? valorTempo * 60 : valorTempo;
            string restoAposTempo = resto.Substring(matchTempo.Length).Trim();

            int posPipe = restoAposTempo.IndexOf('|');
            if (posPipe < 0)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - faltou o separador '|' entre a descrição e as opções.");
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
                CPH.SendYouTubeMessage($"@{evento.UserName} - precisa de uma descrição e pelo menos 2 opções separadas por ';'.");
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

            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "CriarPalpite");
            CPH.TryGetArg("criarPalpiteResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    string listaOpcoes = string.Join(" | ", options.Select((o, i) => $"{(char)('a' + i)}) {o}"));
                    CPH.SendYouTubeMessage($"🎲 PALPITE ABERTO: {description} — {listaOpcoes} — aposte com !palpite [letra] [valor], você tem {FormatarDuracao(durationSeconds)}!");
                    break;
                case "RodadaJaAberta":
                    CPH.SendYouTubeMessage($"@{evento.UserName} - já existe um palpite em andamento. Aguarde ele encerrar.");
                    break;
                default:
                    CPH.LogError(">>> [GERENTE_DE_PALPITE] ERRO: falha ao criar palpite no banco de dados.");
                    CPH.SendYouTubeMessage($"@{evento.UserName} - erro ao criar o palpite, tenta de novo.");
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DE_PALPITE] ERRO CRÍTICO em IniciarPalpite: {ex.Message}");
            return false;
        }
    }

    public bool ResolverPalpite()
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_DE_PALPITE] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            var evento = contexto.Evento;

            if (!evento.IsMod)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - apenas moderadores podem declarar o resultado de um palpite.");
                return true;
            }

            string mensagem = evento.MessageText ?? "";
            var partes = mensagem.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length != 2 || partes[1].Length != 1 || partes[1][0] < 'a' || partes[1][0] > 'z')
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - sintaxe: !resultadopalpite [letra]");
                return true;
            }

            string opcaoVencedora = partes[1].ToLower();

            CPH.SetArgument("resolverPalpiteOpcaoVencedora", opcaoVencedora);
            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ResolverPalpite");
            CPH.TryGetArg("resolverPalpiteResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    CPH.TryGetArg("resolverPalpiteDescription", out string description);
                    CPH.TryGetArg("resolverPalpiteTotalPago", out int totalPago);
                    CPH.TryGetArg("resolverPalpiteQtdVencedores", out int qtdVencedores);
                    CPH.SendYouTubeMessage($"🏆 Palpite \"{description}\" encerrado! Opção vencedora: {opcaoVencedora}) — {qtdVencedores} vencedor(es) dividiram {totalPago:N0} moeda(s)!");
                    break;
                case "SemGanhadores":
                    CPH.SendYouTubeMessage("😬 Ninguém apostou na opção vencedora — palpite cancelado, moedas devolvidas a todos.");
                    break;
                case "SemRodadaAberta":
                    CPH.SendYouTubeMessage($"@{evento.UserName} - não há nenhum palpite aberto no momento.");
                    break;
                case "OpcaoInvalida":
                    CPH.SendYouTubeMessage($"@{evento.UserName} - opção '{opcaoVencedora}' não existe nesse palpite.");
                    break;
                default:
                    CPH.LogError(">>> [GERENTE_DE_PALPITE] ERRO: falha ao resolver palpite.");
                    CPH.SendYouTubeMessage($"@{evento.UserName} - erro ao declarar o resultado, tenta de novo.");
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DE_PALPITE] ERRO CRÍTICO em ResolverPalpite: {ex.Message}");
            return false;
        }
    }

    public bool CancelarPalpite()
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_DE_PALPITE] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            var evento = contexto.Evento;

            if (!evento.IsMod)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - apenas moderadores podem cancelar um palpite.");
                return true;
            }

            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "CancelarPalpite");
            CPH.TryGetArg("cancelarPalpiteResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    CPH.SendYouTubeMessage("🚫 Palpite cancelado, moedas apostadas devolvidas a todos os participantes.");
                    break;
                case "SemRodadaAberta":
                    CPH.SendYouTubeMessage($"@{evento.UserName} - não há nenhum palpite aberto no momento.");
                    break;
                default:
                    CPH.LogError(">>> [GERENTE_DE_PALPITE] ERRO: falha ao cancelar palpite.");
                    CPH.SendYouTubeMessage($"@{evento.UserName} - erro ao cancelar o palpite, tenta de novo.");
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DE_PALPITE] ERRO CRÍTICO em Cancelar: {ex.Message}");
            return false;
        }
    }

    public bool ApostarPalpite()
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_DE_PALPITE] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            var evento = contexto.Evento;

            string mensagem = evento.MessageText ?? "";
            var partes = mensagem.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length != 3)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - sintaxe: !palpite [opção] [valor], ex: !palpite a 50");
                return true;
            }

            string opcao = partes[1].ToLower();
            if (opcao.Length != 1 || opcao[0] < 'a' || opcao[0] > 'z')
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - opção inválida, use a letra da opção (a, b, c...).");
                return true;
            }

            if (!int.TryParse(partes[2], out int valor) || valor <= 0)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - valor inválido, use um número inteiro positivo de moedas.");
                return true;
            }

            CPH.SetArgument("apostarPalpiteUserId", evento.UserId);
            CPH.SetArgument("apostarPalpiteUserName", evento.UserName);
            CPH.SetArgument("apostarPalpiteOption", opcao);
            CPH.SetArgument("apostarPalpiteValor", valor);
            CPH.SetArgument("apostarPalpiteBroadcastUserId", evento.BroadcastUserId);

            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ApostarPalpite");
            CPH.TryGetArg("apostarPalpiteResultado", out string resultado);

            switch (resultado)
            {
                case "Sucesso":
                    CPH.TryGetArg("apostarPalpiteTotalUsuario", out int totalUsuario);
                    CPH.SendYouTubeMessage($"@{evento.UserName} apostou {valor:N0} Moedas na opção '{opcao}'. (Total: {totalUsuario:N0})");
                    break;
                case "SemRodadaAberta":
                    CPH.SendYouTubeMessage($"@{evento.UserName} - não há nenhum palpite aberto no momento.");
                    break;
                case "RodadaEncerrada":
                    CPH.SendYouTubeMessage($"@{evento.UserName} - o tempo desse palpite já acabou, aguarde o mod declarar o resultado.");
                    break;
                case "OpcaoInvalida":
                    CPH.SendYouTubeMessage($"@{evento.UserName} - opção '{opcao}' não existe nesse palpite.");
                    break;
                case "SaldoInsuficiente":
                    CPH.TryGetArg("apostarPalpiteSaldoAtual", out int saldoAtual);
                    CPH.SendYouTubeMessage($"@{evento.UserName} - saldo insuficiente (você tem {saldoAtual:N0} moeda(s)).");
                    break;
                case "OpcaoDiferente":
                    CPH.TryGetArg("apostarPalpiteOpcaoAtual", out string opcaoAtual);
                    CPH.SendYouTubeMessage($"@{evento.UserName} - você já apostou na opção '{opcaoAtual}' nessa rodada, não dá pra trocar.");
                    break;
                default:
                    CPH.LogError(">>> [GERENTE_DE_PALPITE] ERRO: falha ao registrar aposta.");
                    CPH.SendYouTubeMessage($"@{evento.UserName} - erro ao registrar sua aposta, tenta de novo.");
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DE_PALPITE] ERRO CRÍTICO em ApostarPalpite: {ex.Message}");
            return false;
        }
    }

    public bool VerificarEncerramentoPalpite()
    {
        try
        {
            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "VerificarEncerramentoPalpite");
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

                CPH.SendYouTubeMessage($"⏰ Tempo esgotado! Palpite \"{description}\" encerrado! {resumo}");
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DE_PALPITE] ERRO CRÍTICO em VerificarEncerramentoPalpite: {ex.Message}");
            return false;
        }
    }

    private string FormatarDuracao(int segundos)
    {
        if (segundos % 60 == 0)
            return $"{segundos / 60} minuto(s)";
        return $"{segundos} segundo(s)";
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
    }
}