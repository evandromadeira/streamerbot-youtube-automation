using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261022.0630
// Padrões aplicados: 5
// Revisão concluída: 1
// Sem triggers. Chamado por Youtube Gerente de Doações via ExecuteMethod.
public class CPHInline
{
    private const int MultiplicadorMinimo = 50;
    private const int MultiplicadorMaximo = 1000;
    private const int MoedasPorReal = 20;
    private const int LimiteMensagem = 200;

    private const string Prefixo = "[RECOMPENSAR_DOACOES] ";

    public bool RecompensarDoacao()
    {
        bool chamouCredito = false;

        Contexto contexto = null;
        Doacao doacao = null;

        try
        {
            contexto = LerContexto();
            if (contexto?.Doacao == null)
            {
                throw new ArgumentException("Não foi possível ler o contexto da doação.");
            }

            doacao = contexto.Doacao;
            if (string.IsNullOrWhiteSpace(doacao.TriggerName) || string.IsNullOrWhiteSpace(doacao.Usuario))
            {
                throw new ArgumentException("Dados da doação inválidos.");
            }

            doacao.MoedasCalculadas = 0;
            doacao.MoedaGanha = 0;
            doacao.Multiplicador = 0;
            doacao.CreditoStatus = "falhou";
            doacao.CreditoErro = "";
            doacao.DestinatarioId = "";
            if (doacao.ValorBRL <= 0m)
            {
                throw new ArgumentException("Valor em BRL inválido para calcular a recompensa.");
            }

            doacao.Multiplicador = new Random().Next(MultiplicadorMinimo, MultiplicadorMaximo + 1);
            double calculo = Math.Round(doacao.Multiplicador * (double)doacao.ValorBRL * MoedasPorReal);
            if (calculo < 0 || calculo > int.MaxValue)
            {
                throw new ArgumentException("Quantidade calculada fora do intervalo de moedas suportado: " + calculo);
            }

            doacao.MoedasCalculadas = (int)calculo;
            if (doacao.MoedasCalculadas == 0)
            {
                throw new ArgumentException("O valor da doação resultou em zero moedas após o arredondamento.");
            }

            doacao.CreditoStatus = "incerto";

            // Youtube Gerente de Moedas: solicita o crédito e recebe a confirmação pelo contexto.
            CPH.SetArgument("contextoJson", JsonConvert.SerializeObject(contexto));

            chamouCredito = true;
            bool executou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasDoacao");

            Contexto resposta = LerContexto();
            if (resposta?.Doacao == null || resposta.Doacao.ProcessamentoId != doacao.ProcessamentoId)
            {
                throw new InvalidOperationException("Resposta do crédito ausente ou de outro processamento.");
            }

            contexto = resposta;
            doacao = resposta.Doacao;

            bool creditou = doacao.CreditoStatus == "creditado";

            string erro = doacao.CreditoErro ?? "";
            if (string.IsNullOrEmpty(erro) && (!creditou || !executou))
            {
                erro = $"Crédito: retorno={executou}, status={doacao.CreditoStatus}.";
            }

            doacao.CreditoErro = erro;
            // Uma falha posterior ao COMMIT não desfaz o crédito confirmado, mas precisa aparecer no log.
            return creditou && executou && string.IsNullOrEmpty(erro);
        }
        catch (Exception ex)
        {
            if (doacao != null)
            {
                // Uma exceção na chamada pode acontecer depois de publicar o crédito confirmado.
                bool confirmado = false;
                if (chamouCredito)
                {
                    try
                    {
                        Contexto resposta = LerContexto();
                        if (resposta?.Doacao != null && resposta.Doacao.ProcessamentoId == doacao.ProcessamentoId)
                        {
                            contexto = resposta;
                            doacao = resposta.Doacao;
                            confirmado = doacao.CreditoStatus == "creditado";
                        }
                    }
                    catch (Exception leituraEx)
                    {
                        LogError("Não foi possível conferir o retorno do crédito: " + leituraEx.Message);
                    }
                }

                doacao.CreditoStatus = confirmado ? "creditado" : chamouCredito ? "incerto" : "falhou";
                doacao.MoedaGanha = confirmado ? doacao.MoedasCalculadas : 0;
                doacao.CreditoErro = ex.ToString();
            }

            LogError("Erro ao recompensar doação: " + ex.Message);
            return false;
        }
        finally
        {
            CPH.SetArgument("contextoJson", doacao == null ? "" : JsonConvert.SerializeObject(contexto));
        }
    }

    public bool EnviarAgradecimento()
    {
        // Usa os resultados atualizados pelo Gerente de Doações, sem repetir o crédito.
        try
        {
            Contexto contexto = LerContexto();
            if (contexto?.Doacao == null)
            {
                throw new ArgumentException("Não foi possível ler o contexto da doação.");
            }

            Doacao doacao = contexto.Doacao;
            if (string.IsNullOrWhiteSpace(doacao.TriggerName) || string.IsNullOrWhiteSpace(doacao.Usuario))
            {
                throw new ArgumentException("Dados da doação inválidos.");
            }

            bool membro = doacao.TriggerName == "New Sponsor" || doacao.TriggerName == "Member Milestone" || doacao.TriggerName == "Membership Gift";
            // O agradecimento usa os valores calculados, independentemente da confirmação técnica.
            bool temRecompensa = doacao.MoedasCalculadas > 0;

            var (nomeEvento, artigo) = doacao.TriggerName switch
            {
                "Super Chat" => ("Super Chat", "pelo"),
                "Super Sticker" => ("Super Sticker", "pelo"),
                "Jewels Gifted" => ("Joias", "pelas"),
                "New Sponsor" => ("Novo Membro", "pelo"),
                "Member Milestone" => ("Renovação de Assinatura", "pela"),
                "Membership Gift" => ("Presente de Assinatura", "pelo"),
                "Tip" => ("Contribuição", "pela"),
                _ => ("Contribuição", "pela")
            };

            string detalheTier = membro && !string.IsNullOrEmpty(doacao.Tier) ? $" ({doacao.Tier}" + (doacao.QuantidadeGifts > 1 ? $" x{doacao.QuantidadeGifts})" : ")") : "";
            string agradecimento = doacao.TriggerName == "Jewels Gifted" ? $"Obrigado {artigo} {doacao.JewelsAmount:N0} {nomeEvento}" : $"Obrigado {artigo} {nomeEvento}{detalheTier} de {ObterSimboloMoeda(doacao.CurrencyCode)} {doacao.ValorOriginal:F2}";
            string mensagem = $"{agradecimento}, @{doacao.Usuario}!";
            mensagem += $" Você contribuiu com {doacao.PontosMeta:N0} Pontos para as metas";
            if (temRecompensa)
            {
                mensagem += $" e ganhou {doacao.MoedasCalculadas:N0} Moedas! ({doacao.Multiplicador:N0} Multiplicador x {doacao.ValorBRL:0.00#} x {MoedasPorReal})";
            }
            else
            {
                mensagem += "!";
            }

            if (mensagem.Length > LimiteMensagem)
            {
                mensagem = mensagem.Substring(0, LimiteMensagem - 3) + "...";
            }

            EnviarMensagem(mensagem, doacao.BroadcastId, somenteNaTransmissao: true);
            if (doacao.TriggerName == "Tip" && !string.IsNullOrWhiteSpace(doacao.Message))
            {
                string mensagemDoador = $"💬 @{doacao.Usuario}: {doacao.Message.Trim()}";
                if (mensagemDoador.Length > LimiteMensagem)
                {
                    mensagemDoador = mensagemDoador.Substring(0, LimiteMensagem - 3) + "...";
                }

                EnviarMensagem(mensagemDoador, doacao.BroadcastId, somenteNaTransmissao: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("Erro ao enviar agradecimento: " + ex.Message);
            return false;
        }
    }

    private string ObterSimboloMoeda(string moeda)
    {
        var simbolos = new Dictionary<string, string>
        {
            { "BRL", "R$" },
            { "USD", "U$" },
            { "GBP", "£" },
            { "EUR", "€" }
        };
        return moeda != null && simbolos.TryGetValue(moeda, out string simbolo) ? simbolo : moeda;
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

    private Contexto LerContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);
        if (string.IsNullOrWhiteSpace(contextoJson))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    public class Contexto
    {
        public Doacao Doacao { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Doacao
    {
        public int MoedasCalculadas { get; set; }
        public int QuantidadeGifts { get; set; }
        public int PontosMeta { get; set; }
        public int MoedaGanha { get; set; }
        public int Multiplicador { get; set; }

        public decimal ValorOriginal { get; set; }
        public decimal ValorBRL { get; set; }

        public double JewelsAmount { get; set; }

        public string ProcessamentoId { get; set; }
        public string CreditoErro { get; set; }
        public string DestinatarioId { get; set; }
        public string TriggerName { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string CurrencyCode { get; set; }
        public string Message { get; set; }
        public string Tier { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
        public string CreditoStatus { get; set; }
        public string RegistroStatus { get; set; }

        // Preserva os dados dos demais gerentes ao devolver o JSON.
        [JsonExtensionData]
        public IDictionary<string, JToken> DadosComplementares { get; set; }
    }
}