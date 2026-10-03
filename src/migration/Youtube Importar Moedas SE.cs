using System;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261003.0945
public class CPHInline
{
    public bool ImportarMoedasStreamElements()
    {
        string operacaoId = Guid.NewGuid().ToString("D");
        string debitoStatus = "nao_iniciado";
        string creditoStatus = "falhou";
        string idCanal = null;
        string twitchUser = null;
        int moedasAImportar = 0;

        Evento evento = null;

        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                CPH.LogError(">>> [IMPORTAR_MOEDAS_SE] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }
            evento = contexto.Evento;
            Ambiente ambiente = contexto.Ambiente;

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length != 3)
            {
                CPH.SendYouTubeMessage($"⚠ Uso correto: !importar [nome_na_twitch] [quantidade ou all]");
                return false;
            }

            twitchUser = partes[1].Trim();
            string qtdInput = partes[2].Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(ambiente.PastaSE))
            {
                CPH.LogError(">>> [IMPORTAR_MOEDAS_SE] ERRO: Variável global 'caminhoPastaStreamElements' não definida!");
                CPH.SendYouTubeMessage("❌ Erro de configuração: pasta do StreamElements não localizada.");
                return false;
            }

            if (!File.Exists(ambiente.CaminhoConfigSE))
            {
                CPH.LogError($">>> [IMPORTAR_MOEDAS_SE] ERRO: Arquivo {ambiente.CaminhoConfigSE} não localizado!");
                CPH.SendYouTubeMessage("❌ Erro interno: arquivo de credenciais ausente.");
                return false;
            }

            var variaveisConfigSE = CarregarVariaveisDoArquivo(ambiente.CaminhoConfigSE);
            var usuarioEmissao = CPH.GetGlobalVar<string>("usuarioEmissao", true);

            if (string.IsNullOrEmpty(usuarioEmissao))
            {
                CPH.LogError(">>> [IMPORTAR_MOEDAS_SE] ERRO: Variável global 'usuarioEmissao' não definida!");
                CPH.SendYouTubeMessage("❌ Erro de configuração: usuário emissor não localizado.");
                return false;
            }

            string keyPrefix = usuarioEmissao.ToLowerInvariant();

            string jwtToken = variaveisConfigSE.ContainsKey($"jwt_token_{keyPrefix}") ? variaveisConfigSE[$"jwt_token_{keyPrefix}"] : null;
            idCanal = variaveisConfigSE.ContainsKey($"channel_id_{keyPrefix}") ? variaveisConfigSE[$"channel_id_{keyPrefix}"] : null;
            string moedaSE = variaveisConfigSE.ContainsKey($"moeda_se_{keyPrefix}") ? variaveisConfigSE[$"moeda_se_{keyPrefix}"] : "pontos";

            if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(idCanal))
            {
                CPH.LogError($">>> [IMPORTAR_MOEDAS_SE] ERRO: Token ou Channel ID ausentes para o canal {usuarioEmissao}!");
                CPH.SendYouTubeMessage("❌ Falha de autenticação com o StreamElements.");
                return false;
            }

            var api = new ApiStreamElements(jwtToken, idCanal);

            (int saldoSE, _) = api.ConsultarSaldoERankUsuario(twitchUser);

            if (saldoSE <= 0)
            {
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, o usuário Twitch '{twitchUser}' não possui {moedaSE} para importar.");
                return false;
            }

            if (qtdInput == "all")
            {
                moedasAImportar = saldoSE;
            }
            else if (int.TryParse(qtdInput, out int qtdInformada))
            {
                if (qtdInformada <= 0)
                {
                    CPH.SendYouTubeMessage($"⚠ A quantidade informada precisa ser maior que zero!");
                    return false;
                }
                if (qtdInformada > saldoSE)
                {
                    CPH.SendYouTubeMessage($"❌ Saldo insuficiente na Twitch! '{twitchUser}' possui apenas {saldoSE:N0} {moedaSE}.");
                    return false;
                }
                moedasAImportar = qtdInformada;
            }
            else
            {
                CPH.SendYouTubeMessage($"⚠ Entrada inválida! Use um valor numérico ou 'all'.");
                return false;
            }

            // Sem resposta conclusiva, o débito pode ter sido aplicado no SE.
            debitoStatus = "incerto";
            bool debitou = api.DebitarPontosUsuario(twitchUser, moedasAImportar);
            debitoStatus = api.DebitoStatus;
            if (!debitou) throw new InvalidOperationException(api.UltimoErro);
            CPH.SetArgument("origem", "importacao");
            CPH.SetArgument("targetUserId", evento.UserId);
            CPH.SetArgument("targetUserName", evento.UserName);
            CPH.SetArgument("coinsToAdd", moedasAImportar);
            CPH.SetArgument("broadcastUserId", evento.BroadcastUserId);
            CPH.SetArgument("broadcastUserName", evento.BroadcastUserName);

            CPH.SetArgument("adicionarCreditoStatus", "incerto");
            CPH.SetArgument("adicionarErro", "");
            CPH.SetArgument("adicionarDestinatarioId", "");
            creditoStatus = "incerto";
            bool executou;
            try
            {
                executou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
            }
            finally
            {
                CPH.TryGetArg("adicionarCreditoStatus", out string status);
                creditoStatus = status == "creditado" || status == "falhou" ? status : "incerto";
            }
            CPH.TryGetArg("adicionarErro", out string erroCredito);
            if (creditoStatus != "creditado" || !executou || !string.IsNullOrEmpty(erroCredito))
                throw new InvalidOperationException(string.IsNullOrEmpty(erroCredito) ? "O crédito não terminou normalmente." : erroCredito);

            // O saldo é informativo: sua consulta não desfaz um crédito confirmado.
            CPH.SetArgument("consultarChave", evento.UserId);
            CPH.SetArgument("consultarPorId", true);
            CPH.SetArgument("consultarEncontrado", false);
            bool consultou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SaldoMoedasUsuario");
            CPH.TryGetArg("consultarEncontrado", out bool encontrado);
            CPH.TryGetArg("consultarMoedas", out int saldoAtual);
            CPH.TryGetArg("consultarRank", out int rankUsuario);
            string detalheSaldo = consultou && encontrado ? $" Saldo: {saldoAtual:N0} (#{rankUsuario})." : " Saldo indisponível para consulta.";
            Avisar($"✅ @{evento.UserName}, importação de {moedasAImportar:N0} moedas concluída!" + detalheSaldo);
            return true;
        }
        catch (Exception ex)
        {
            CPH.TryGetArg("adicionarDestinatarioId", out string destinatarioId);
            string comandoManual = debitoStatus == "debitado" && creditoStatus == "falhou" ? $"!adicionar @{evento?.UserName} {moedasAImportar}" : null;
            CPH.LogError(">>> [IMPORTAR_MOEDAS_SE] RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { operacaoId, timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), origem = "importacao", pastaStreamerBot = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true), twitchUser, idCanalSE = idCanal, userId = evento?.UserId, userName = evento?.UserName, broadcastUserId = evento?.BroadcastUserId, broadcastUserName = evento?.BroadcastUserName, destinatarioId, quantidade = moedasAImportar, debitoStatus, creditoStatus, erro = ex.Message, comandoManual, orientacao = "Conferir usuário/ID e histórico antes de ajustar. Nunca repetir uma operação incerta. Crédito confirmado não deve ser adicionado novamente." }));

            if (creditoStatus == "creditado")
                Avisar($"⚠ @{evento?.UserName}, as {moedasAImportar:N0} moedas foram creditadas, mas houve falha posterior. Não repita a importação.");
            else if (debitoStatus == "debitado" && creditoStatus == "falhou")
                Avisar($"⚠ @{evento?.UserName}, o SE debitou {moedasAImportar:N0} pontos, mas o crédito falhou. Moderação: adicionar manualmente; detalhes no log. Não repita a importação.");
            else if (debitoStatus == "incerto" || creditoStatus == "incerto")
                Avisar($"⚠ @{evento?.UserName}, não foi possível confirmar a importação. Moderação: conferir SE e banco pelo log antes de adicionar moedas. Não repita a operação.");
            else
                Avisar($"❌ @{evento?.UserName}, não foi possível concluir a consulta ou o débito no StreamElements. Tente mais tarde.");
            return creditoStatus == "creditado";
        }
    }

    private void Avisar(string mensagem)
    {
        try
        {
            if (mensagem.Length > 200) mensagem = mensagem.Substring(0, 197) + "...";
            CPH.SendYouTubeMessage(mensagem);
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [IMPORTAR_MOEDAS_SE] Falha ao enviar aviso ao chat: " + ex.Message);
        }
    }

    private Dictionary<string, string> CarregarVariaveisDoArquivo(string arqConfigSE)
    {
        var variaveisArquivo = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(arqConfigSE))
        {
            if (line.Contains("="))
            {
                var partes = line.Split(new[] { '=' }, 2);
                if (partes.Length == 2)
                    variaveisArquivo[partes[0].Trim()] = partes[1].Trim();
            }
        }
        return variaveisArquivo;
    }

    public class ApiStreamElements
    {
        public string DebitoStatus { get; private set; } = "nao_iniciado";
        public string UltimoErro { get; private set; } = "";
        private readonly string jwtToken;
        private readonly string idCanal;
        private readonly HttpClient client = new HttpClient();

        public ApiStreamElements(string jwtToken, string idCanal)
        {
            this.jwtToken = jwtToken;
            this.idCanal = idCanal;
        }

        public (int saldo, int rank) ConsultarSaldoERankUsuario(string usuario)
        {
            string url = $"https://api.streamelements.com/kappa/v2/points/{this.idCanal}/{usuario}";
            var getRequest = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url),
                Headers =
                {
                    { "Accept", "application/json; charset=utf-8" },
                    { "Authorization", $"Bearer {this.jwtToken}" }
                },
            };

            var getResponse = client.SendAsync(getRequest).GetAwaiter().GetResult();
            if (!getResponse.IsSuccessStatusCode) throw new InvalidOperationException("Consulta SE: HTTP " + (int)getResponse.StatusCode);

            var getBody = getResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JToken.Parse(getBody);

            int usuarioPoints = (int)json["points"];
            int usuarioRank = (int)json["rank"];

            return (usuarioPoints, usuarioRank);
        }

        public bool DebitarPontosUsuario(string usuario, int pontos)
        {
            try
            {
                string url = $"https://api.streamelements.com/kappa/v2/points/{this.idCanal}/{usuario}/-{pontos}";
                var putRequest = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    RequestUri = new Uri(url),
                    Headers =
                    {
                        { "Accept", "application/json; charset=utf-8" },
                        { "Authorization", $"Bearer {this.jwtToken}" }
                    },
                };

                DebitoStatus = "incerto";
                var putResponse = client.SendAsync(putRequest).GetAwaiter().GetResult();
                if (putResponse.IsSuccessStatusCode)
                {
                    DebitoStatus = "debitado";
                    return true;
                }
                // Uma resposta de erro não comprova, por si só, que o servidor não debitou.
                UltimoErro = "Débito SE não confirmado: HTTP " + (int)putResponse.StatusCode;
                return false;
            }
            catch (Exception ex)
            {
                UltimoErro = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
        public Ambiente Ambiente { get; set; }
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
    }

    public class Ambiente
    {
        public string PastaSE { get; set; } = "";

        public string CaminhoConfigSE => Path.Combine(PastaSE, "ConfigSE.txt");

        // Construtor vazio necessário para desserialização do contexto.
        public Ambiente()
        {
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaSE = CPH.GetGlobalVar<string>("caminhoPastaStreamElements", true) ?? "";
        }
    }
}