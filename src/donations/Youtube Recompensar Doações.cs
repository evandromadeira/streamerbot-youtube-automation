using System;
using System.Collections.Generic;

// Atualização 261002.0945
// Sem triggers. Chamado por Youtube Gerente de Doações via ExecuteMethod.
// Configure o Name de Execute C# Code como Youtube Recompensar Doações.
public class CPHInline
{
    public bool RecompensarDoacao()
    {
        bool chamouCredito = false;
        CPH.SetArgument("doacaoMoedasCalculadas", 0);
        CPH.SetArgument("doacaoMoedaGanha", 0);
        CPH.SetArgument("doacaoMultiplicador", 0);
        CPH.SetArgument("doacaoCreditoStatus", "falhou");
        CPH.SetArgument("doacaoCreditoErro", "");
        CPH.SetArgument("adicionarCreditoStatus", "incerto");
        CPH.SetArgument("adicionarResultado", "");
        CPH.SetArgument("adicionarErro", "");
        CPH.SetArgument("adicionarDestinatarioId", "");

        try
        {
            if (!CPH.TryGetArg("doacaoValorBRL", out double valorEmBRL) || double.IsNaN(valorEmBRL) || double.IsInfinity(valorEmBRL) || valorEmBRL <= 0)
                throw new ArgumentException("Valor em BRL inválido para calcular a recompensa.");

            int multiplicador = new Random().Next(50, 1001);
            CPH.SetArgument("doacaoMultiplicador", multiplicador);
            double calculo = Math.Round(multiplicador * valorEmBRL * 20);
            if (calculo < 0 || calculo > int.MaxValue)
                throw new ArgumentException("Quantidade calculada fora do intervalo de moedas suportado: " + calculo);

            int moedaGanha = (int)calculo;
            CPH.SetArgument("doacaoMoedasCalculadas", moedaGanha);
            if (moedaGanha == 0)
                throw new ArgumentException("O valor da doação resultou em zero moedas após o arredondamento.");

            CPH.TryGetArg("doacaoUserId", out string userId);
            CPH.TryGetArg("doacaoUserName", out string userName);
            CPH.TryGetArg("doacaoBroadcastUserId", out string broadcastUserId);
            CPH.TryGetArg("doacaoBroadcastUserName", out string broadcastUserName);
            CPH.SetArgument("origem", "doacao");
            CPH.SetArgument("targetUserId", userId);
            CPH.SetArgument("targetUserName", userName);
            CPH.SetArgument("coinsToAdd", moedaGanha);
            CPH.SetArgument("broadcastUserId", broadcastUserId);
            CPH.SetArgument("broadcastUserName", broadcastUserName);

            chamouCredito = true;
            bool executou = CPH.ExecuteMethod("Youtube Gerente de Moedas", "AdicionarMoedasUsuario");
            CPH.TryGetArg("adicionarCreditoStatus", out string status);
            CPH.TryGetArg("adicionarResultado", out string resultado);
            CPH.TryGetArg("adicionarErro", out string erro);
            bool creditou = status == "creditado";
            CPH.SetArgument("doacaoCreditoStatus", creditou ? "creditado" : (status == "falhou" ? "falhou" : "incerto"));
            CPH.SetArgument("doacaoMoedaGanha", creditou ? moedaGanha : 0);
            if (string.IsNullOrEmpty(erro) && (!creditou || !executou)) erro = $"Crédito: retorno={executou}, resultado={resultado}, status={status}.";
            CPH.SetArgument("doacaoCreditoErro", erro ?? "");
            // Uma falha posterior ao COMMIT não desfaz o crédito confirmado, mas precisa aparecer no log.
            return creditou && executou && string.IsNullOrEmpty(erro);
        }
        catch (Exception ex)
        {
            CPH.TryGetArg("adicionarCreditoStatus", out string status);
            CPH.TryGetArg("doacaoMoedasCalculadas", out int moedasCalculadas);
            bool creditou = chamouCredito && status == "creditado";
            CPH.SetArgument("doacaoCreditoStatus", creditou ? "creditado" : (!chamouCredito || status == "falhou" ? "falhou" : "incerto"));
            CPH.SetArgument("doacaoMoedaGanha", creditou ? moedasCalculadas : 0);
            CPH.SetArgument("doacaoCreditoErro", ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    public bool EnviarAgradecimento()
    {
        // Chamado uma única vez após as tentativas de recompensa, registro e Timer.
        CPH.TryGetArg("doacaoTipoAcao", out string tipoAcao);
        CPH.TryGetArg("doacaoUserName", out string usuario);
        CPH.TryGetArg("doacaoTier", out string tier);
        CPH.TryGetArg("doacaoQuantidadeGifts", out int quantidadeGifts);
        CPH.TryGetArg("doacaoJewelsAmount", out double jewelsAmount);
        CPH.TryGetArg("doacaoMoedaOrigem", out string moeda);
        CPH.TryGetArg("doacaoValorOriginal", out double valorOriginal);
        CPH.TryGetArg("doacaoValorBRL", out double valorEmBRL);
        CPH.TryGetArg("doacaoPontosMeta", out int pontosMetaInt);
        CPH.TryGetArg("doacaoMoedaGanha", out int moedaGanha);
        CPH.TryGetArg("doacaoMultiplicador", out int multiplicador);
        CPH.TryGetArg("doacaoCreditoStatus", out string statusCredito);
        CPH.TryGetArg("doacaoRegistroStatus", out string statusRegistro);

        var (nomeEvento, artigo) = tipoAcao switch
        {
            "Super Chat"        => ("Super Chat", "pelo"),
            "Super Sticker"     => ("Super Sticker", "pelo"),
            "Jewels Gifted"     => ("Joias", "pelas"),
            "New Sponsor"       => ("Novo Membro", "pelo"),
            "Member Milestone"  => ("Renovação de Assinatura", "pela"),
            "Membership Gift"   => ("Presente de Assinatura", "pelo"),
            "Tip"               => ("Contribuição", "pela"),
            _                   => ("Contribuição", "pela")
        };
        bool membro = tipoAcao == "New Sponsor" || tipoAcao == "Member Milestone" || tipoAcao == "Membership Gift";
        string detalheTier = membro && !string.IsNullOrEmpty(tier) ? $" ({tier}" + (quantidadeGifts > 1 ? $" x{quantidadeGifts})" : ")") : "";
        string agradecimento = tipoAcao == "Jewels Gifted" ? $"Obrigado {artigo} {jewelsAmount:N0} {nomeEvento}" : $"Obrigado {artigo} {nomeEvento}{detalheTier} de {ObterSimboloMoeda(moeda)} {valorOriginal:F2}";
        string mensagem = $"{agradecimento}, @{usuario}!";
        bool creditou = statusCredito == "creditado";
        bool registrou = statusRegistro == "salvo";
        if (registrou) mensagem += $" Você contribuiu com {pontosMetaInt:N0} Pontos para as metas";
        if (creditou) mensagem += (registrou ? " e ganhou " : " Você ganhou ") + $"{moedaGanha:N0} Moedas! ({multiplicador:N0} Multiplicador x {valorEmBRL:0.00#} x 20)";
        else if (registrou) mensagem += "!";
        if (mensagem.Length > 200) mensagem = mensagem.Substring(0, 197) + "...";

        CPH.SendYouTubeMessage(mensagem, true);
        return true;
    }

    private string ObterSimboloMoeda(string moeda)
    {
        var simbolos = new Dictionary<string, string> { { "BRL", "R$" }, { "USD", "U$" }, { "GBP", "£" }, { "EUR", "€" } };
        return moeda != null && simbolos.TryGetValue(moeda, out string simbolo) ? simbolo : moeda;
    }
}