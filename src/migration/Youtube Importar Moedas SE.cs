using System;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 260822.1255
public class CPHInline
{
    public bool ImportarMoedasStreamElements()
    {
        Evento evento = null;
        Ambiente ambiente = null;

        bool debitoSucesso = false;
        string twitchUser = null;
        int moedasAImportar = 0;

        try
        {
            CPH.TryGetArg("contextoJson", out string contextoJson);
            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);

            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                CPH.LogError(">>> [IMPORT_MOEDAS_SE] ERRO: contexto ausente ou inválido.");
                return false;
            }

            evento = contexto.Evento;
            ambiente = contexto.Ambiente;

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length != 3)
            {
                CPH.SendYouTubeMessage($"⚠ Uso correto: !importar [nome_na_twitch] [quantidade ou all]");
                return false;
            }

            twitchUser = partes[1].Trim();
            string qtdInput = partes[2].Trim().ToLower();

            if (string.IsNullOrEmpty(ambiente.PastaSE))
            {
                CPH.LogError(">>> [IMPORT_MOEDAS_SE] ERRO: Variável global 'caminhoPastaStreamElements' não definida!");
                CPH.SendYouTubeMessage("❌ Erro de configuração: pasta do StreamElements não localizada.");
                return false;
            }

            if (!File.Exists(ambiente.CaminhoConfigSE))
            {
                CPH.LogError($">>> [IMPORT_MOEDAS_SE] ERRO: Arquivo {ambiente.CaminhoConfigSE} não localizado!");
                CPH.SendYouTubeMessage("❌ Erro interno: arquivo de credenciais ausente.");
                return false;
            }

            var variaveisConfigSE = CarregarVariaveisDoArquivo(ambiente.CaminhoConfigSE);
            var usuarioEmissao = CPH.GetGlobalVar<string>("usuarioEmissao", true);

            if (string.IsNullOrEmpty(usuarioEmissao))
            {
                CPH.LogError(">>> [IMPORT_MOEDAS_SE] ERRO: Variável global 'usuarioEmissao' não definida!");
                CPH.SendYouTubeMessage("❌ Erro de configuração: usuário emissor não localizado.");
                return false;
            }

            string keyPrefix = usuarioEmissao.ToLower();

            string jwtToken = variaveisConfigSE.ContainsKey($"jwt_token_{keyPrefix}") ? variaveisConfigSE[$"jwt_token_{keyPrefix}"] : null;
            string idCanal = variaveisConfigSE.ContainsKey($"channel_id_{keyPrefix}") ? variaveisConfigSE[$"channel_id_{keyPrefix}"] : null;
            string moedaSE = variaveisConfigSE.ContainsKey($"moeda_se_{keyPrefix}") ? variaveisConfigSE[$"moeda_se_{keyPrefix}"] : "pontos";

            if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(idCanal))
            {
                CPH.LogError($">>> [IMPORT_MOEDAS_SE] ERRO: Token ou Channel ID ausentes para o canal {usuarioEmissao}!");
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

            debitoSucesso = api.DebitarPontosUsuario(twitchUser, moedasAImportar);

            if (!debitoSucesso)
            {
                CPH.SendYouTubeMessage($"❌ Erro de comunicação ao tentar debitar pontos do StreamElements.");
                return false;
            }

            CPH.SetArgument("origem", "importacao");
            CPH.SetArgument("targetUserId", evento.UserId);
            CPH.SetArgument("targetUserName", evento.UserName);
            CPH.SetArgument("coinsToAdd", moedasAImportar);
            CPH.SetArgument("broadcastUserId", evento.BroadcastUserId);
            CPH.SetArgument("broadcastUserName", evento.BroadcastUserName);

            bool creditou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
            if (!creditou)
            {
                CPH.LogError($">>> [IMPORT_MOEDAS_SE] ATENÇÃO — AJUSTE MANUAL NECESSÁRIO: {moedasAImportar} pontos já foram debitados de '{twitchUser}' na StreamElements mas NÃO foram creditados no YouTube (destinatário: {evento.UserName} / userId: {evento.UserId}).");
                CPH.SendYouTubeMessage("❌ Falha técnica ao tentar concluir a importação.");
                return false;
            }

            CPH.SetArgument("consultarChave", evento.UserId);
            CPH.SetArgument("consultarPorId", true);
            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SaldoMoedasUsuario");

            CPH.TryGetArg("consultarMoedas", out int saldoAtual);
            CPH.TryGetArg("consultarRank", out int rankUsuario);

            CPH.SendYouTubeMessage($"✅ Importação concluída! {moedasAImportar:N0} {moedaSE} foram retirados de '{twitchUser}' (Twitch) e adicionados à conta de @{evento.UserName} no YouTube! Novo Saldo: {saldoAtual:N0} Moedas (#{rankUsuario}). 🎉");
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [IMPORT_MOEDAS_SE] ERRO CRÍTICO: " + ex.Message);

            if (debitoSucesso)
            {
                CPH.LogError($">>> [IMPORT_MOEDAS_SE] ATENÇÃO — AJUSTE MANUAL NECESSÁRIO: {moedasAImportar} pontos já foram debitados de '{twitchUser}' na StreamElements mas NÃO foram creditados no YouTube (destinatário: {evento?.UserName} / userId: {evento?.UserId}).");
            }

            CPH.SendYouTubeMessage("❌ Falha técnica ao tentar concluir a importação.");
            return false;
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
            try
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
                if (!getResponse.IsSuccessStatusCode) return (0, 0);

                var getBody = getResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var json = JToken.Parse(getBody);

                int usuarioPoints = (int)json["points"];
                int usuarioRank = (int)json["rank"];

                return (usuarioPoints, usuarioRank);
            }
            catch (Exception)
            {
                return (0, 0);
            }
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

                var putResponse = client.SendAsync(putRequest).GetAwaiter().GetResult();
                return putResponse.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
        public Ambiente Ambiente { get; set; }
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
        public string PastaSE { get; set; }

        public string CaminhoConfigSE { get; set; }
    }
}