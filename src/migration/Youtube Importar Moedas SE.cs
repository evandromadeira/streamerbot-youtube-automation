using System;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261022.0630
// Padrões aplicados: 5
// Revisão concluída: 1
public class CPHInline
{
    private const int LimiteMensagem = 200;

    private const string Prefixo = "[IMPORTAR_MOEDAS_SE] ";

    public bool ImportarMoedasStreamElements()
    {
        int moedasAImportar = 0;

        string operacaoId = Guid.NewGuid().ToString("D");
        string debitoStatus = "nao_iniciado";
        string creditoStatus = "falhou";
        string idCanal = null;
        string twitchUser = null;

        Evento evento = null;

        Moedas moedas = null;
        try
        {
            Contexto contexto = LerContexto();
            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }

            evento = contexto.Evento;
            Ambiente ambiente = contexto.Ambiente;

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 3)
            {
                EnviarMensagem($"⚠ Uso correto: !importar [nome_na_twitch] [quantidade ou all]", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            twitchUser = partes[1].Trim();
            string qtdInput = partes[2].Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(ambiente.PastaSE))
            {
                LogError("ERRO: Variável global 'caminhoPastaStreamElements' não definida!");
                EnviarMensagem("❌ Erro de configuração: pasta do StreamElements não localizada.", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            if (!File.Exists(ambiente.CaminhoConfigSE))
            {
                LogError($"ERRO: Arquivo {ambiente.CaminhoConfigSE} não localizado!");
                EnviarMensagem("❌ Erro interno: arquivo de credenciais ausente.", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            var variaveisConfigSE = CarregarVariaveisDoArquivo(ambiente.CaminhoConfigSE);

            string keyPrefix = evento.BroadcastUserName.ToLowerInvariant();
            string jwtToken = variaveisConfigSE.ContainsKey($"jwt_token_{keyPrefix}") ? variaveisConfigSE[$"jwt_token_{keyPrefix}"] : null;
            idCanal = variaveisConfigSE.ContainsKey($"channel_id_{keyPrefix}") ? variaveisConfigSE[$"channel_id_{keyPrefix}"] : null;
            string moedaSE = variaveisConfigSE.ContainsKey($"moeda_se_{keyPrefix}") ? variaveisConfigSE[$"moeda_se_{keyPrefix}"] : "pontos";
            if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(idCanal))
            {
                LogError($"ERRO: Token ou Channel ID ausentes para o canal {evento.BroadcastUserName}!");
                EnviarMensagem("❌ Falha de autenticação com o StreamElements.", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            ApiStreamElements api = new ApiStreamElements(jwtToken, idCanal);
            (int saldoSE, _) = api.ConsultarSaldoERankUsuario(twitchUser);
            if (saldoSE <= 0)
            {
                EnviarMensagem($"❌ @{evento.UserName}, o usuário Twitch '{twitchUser}' não possui {moedaSE} para importar.", evento.BroadcastId, somenteNaTransmissao: true);
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
                    EnviarMensagem($"⚠ A quantidade informada precisa ser maior que zero!", evento.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                if (qtdInformada > saldoSE)
                {
                    EnviarMensagem($"❌ Saldo insuficiente na Twitch! '{twitchUser}' possui apenas {saldoSE:N0} {moedaSE}.", evento.BroadcastId, somenteNaTransmissao: true);
                    return false;
                }

                moedasAImportar = qtdInformada;
            }
            else
            {
                EnviarMensagem($"⚠ Entrada inválida! Use um valor numérico ou 'all'.", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            // Sem resposta conclusiva, o débito pode ter sido aplicado no SE.
            debitoStatus = "incerto";
            bool debitou = api.DebitarPontosUsuario(twitchUser, moedasAImportar);
            debitoStatus = api.DebitoStatus;
            if (!debitou)
            {
                throw new InvalidOperationException(api.UltimoErro);
            }

            // Youtube Gerente de Moedas: credita as moedas após o débito confirmado no SE.
            ImportarMoedas importarMoedas = new ImportarMoedas
            {
                OperacaoId = operacaoId,
                UsuarioId = evento.UserId,
                Usuario = evento.UserName,
                Quantidade = moedasAImportar,
                BroadcastUserId = evento.BroadcastUserId,
                BroadcastUserName = evento.BroadcastUserName
            };
            contexto.ImportarMoedas = importarMoedas;
            contexto.Moedas = null;
            CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(contexto));

            creditoStatus = "incerto";
            bool executou;
            try
            {
                executou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasCredito");
            }
            finally
            {
                moedas = LerResultadoCredito(operacaoId);
                creditoStatus = moedas.Status;
            }

            string erroCredito = moedas.Erro;
            if (creditoStatus != "creditado" || !executou || !string.IsNullOrEmpty(erroCredito))
            {
                throw new InvalidOperationException(string.IsNullOrEmpty(erroCredito) ? "O crédito não terminou normalmente." : erroCredito);
            }

            // Youtube Gerente de Banco de Dados: consulta o saldo sem desfazer o crédito confirmado.
            PrepararArgumentosSaldo(evento.UserId);

            bool consultou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SaldoMoedasUsuario");
            CPH.TryGetArg("consultarEncontrado", out bool encontrado);
            CPH.TryGetArg("consultarMoedas", out int saldoAtual);
            CPH.TryGetArg("consultarRank", out int rankUsuario);
            string detalheSaldo = consultou && encontrado ? $" Saldo: {saldoAtual:N0} (#{rankUsuario})." : " Saldo indisponível para consulta.";
            Avisar($"✅ @{evento.UserName}, importação de {moedasAImportar:N0} moedas concluída!" + detalheSaldo, evento?.BroadcastId);
            return true;
        }
        catch (Exception ex)
        {
            string destinatarioId = moedas?.DestinatarioId;
            string comandoManual = debitoStatus == "debitado" && creditoStatus == "falhou" ? $"!adicionar @{evento?.UserName} {moedasAImportar}" : null;
            LogError("RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { operacaoId, timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), origem = "importacao", pastaStreamerBot = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true), twitchUser, idCanalSE = idCanal, userId = evento?.UserId, userName = evento?.UserName, broadcastUserId = evento?.BroadcastUserId, broadcastUserName = evento?.BroadcastUserName, destinatarioId, quantidade = moedasAImportar, debitoStatus, creditoStatus, erro = ex.Message, comandoManual, orientacao = "Conferir usuário/ID e histórico antes de ajustar. Nunca repetir uma operação incerta. Crédito confirmado não deve ser adicionado novamente." }));
            if (creditoStatus == "creditado")
            {
                Avisar($"⚠ @{evento?.UserName}, as {moedasAImportar:N0} moedas foram creditadas, mas houve falha posterior. Não repita a importação.", evento?.BroadcastId);
            }
            else if (debitoStatus == "debitado" && creditoStatus == "falhou")
            {
                Avisar($"⚠ @{evento?.UserName}, o SE debitou {moedasAImportar:N0} pontos, mas o crédito falhou. Moderação: adicionar manualmente; detalhes no log. Não repita a importação.", evento?.BroadcastId);
            }
            else if (debitoStatus == "incerto" || creditoStatus == "incerto")
            {
                Avisar($"⚠ @{evento?.UserName}, não foi possível confirmar a importação. Moderação: conferir SE e banco pelo log antes de adicionar moedas. Não repita a operação.", evento?.BroadcastId);
            }
            else
            {
                Avisar($"❌ @{evento?.UserName}, não foi possível concluir a consulta ou o débito no StreamElements. Tente mais tarde.", evento?.BroadcastId);
            }

            return creditoStatus == "creditado";
        }
    }

    private void PrepararArgumentosSaldo(string usuarioId)
    {
        CPH.SetArgument("consultarChave", usuarioId);
        CPH.SetArgument("consultarPorId", true);
        CPH.SetArgument("consultarEncontrado", false);
        CPH.SetArgument("consultarMoedas", 0);
        CPH.SetArgument("consultarRank", 0);
    }

    private void Avisar(string mensagem, string broadcastId)
    {
        try
        {
            if (mensagem.Length > LimiteMensagem)
            {
                mensagem = mensagem.Substring(0, LimiteMensagem - 3) + "...";
            }

            EnviarMensagem(mensagem, broadcastId, somenteNaTransmissao: true);
        }
        catch (Exception ex)
        {
            LogError("Falha ao enviar aviso ao chat: " + ex.Message);
        }
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
                    {
                        "Accept",
                        "application/json; charset=utf-8"
                    },
                    {
                        "Authorization",
                        $"Bearer {this.jwtToken}"
                    }
                },
            };
            var getResponse = client.SendAsync(getRequest).GetAwaiter().GetResult();
            if (!getResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Consulta SE: HTTP " + (int)getResponse.StatusCode);
            }

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
                        {
                            "Accept",
                            "application/json; charset=utf-8"
                        },
                        {
                            "Authorization",
                            $"Bearer {this.jwtToken}"
                        }
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

    private Dictionary<string, string> CarregarVariaveisDoArquivo(string arqConfigSE)
    {
        var variaveisArquivo = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(arqConfigSE))
        {
            if (line.Contains("="))
            {
                var partes = line.Split(new[] { '=' }, 2);
                if (partes.Length == 2)
                {
                    variaveisArquivo[partes[0].Trim()] = partes[1].Trim();
                }
            }
        }

        return variaveisArquivo;
    }

    private Moedas LerResultadoCredito(string operacaoId)
    {
        // Só aceita a resposta concluída do pedido atual; ausência de confirmação não permite repetir o crédito.
        try
        {
            Moedas moedas = LerContexto()?.Moedas;
            if (moedas != null && moedas.OperacaoId == operacaoId && moedas.Concluido)
            {
                moedas.Status = moedas.Status == "creditado" || moedas.Status == "falhou" ? moedas.Status : "incerto";
                return moedas;
            }
        }
        catch (Exception ex)
        {
            LogError("Erro ao ler o resultado do crédito: " + ex.Message);
        }

        return new Moedas
        {
            OperacaoId = operacaoId,
            Erro = "Resposta de crédito ausente, incompleta ou de outra operação."
        };
    }

    private Contexto LerContexto()
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
        public ImportarMoedas ImportarMoedas { get; set; }

        public Moedas Moedas { get; set; }

        public Evento Evento { get; set; }

        public Ambiente Ambiente { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class ImportarMoedas
    {
        public int Quantidade { get; set; }

        public string OperacaoId { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Moedas
    {
        public bool Concluido { get; set; }

        public string OperacaoId { get; set; }
        public string Status { get; set; } = "incerto";
        public string Erro { get; set; } = "";
        public string DestinatarioId { get; set; } = "";

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Evento
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Ambiente
    {
        public string PastaSE { get; set; } = "";
        public string CaminhoConfigSE => Path.Combine(PastaSE, "ConfigSE.txt");

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }

        public Ambiente()
        {
            // Construtor vazio necessário para desserialização do contexto.
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaSE = CPH.GetGlobalVar<string>("caminhoPastaStreamElements", true) ?? "";
        }
    }
}