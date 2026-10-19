using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// Atualização 261015.1115
public class CPHInline
{
    private const int VersaoRegraPlataforma = 1;

    private const string Prefixo = "[GERENTE_DE_PLATAFORMA] ";

    private static readonly Dictionary<string, int> DescontosPorNivelMembro = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "TULIPA_BRONZE", 5 },
        { "TULIPA_PRATA", 10 },
        { "TULIPA_OURO", 15 },
        { "TULIPA_PLATINA", 20 }
    };

    public bool ProcessarComando()
    {
        // Processa os comandos relacionados à Plataforma
        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }

            Evento evento = contexto.Evento;

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length < 2)
            {
                EnviarAjuda(evento);
                return true;
            }

            string acao = partes[1].ToLowerInvariant();

            switch (acao)
            {
                case "resgatar":
                    return ResgatarItem(evento, partes);
                case "item":
                    return ConsultarItem(evento, partes);
                case "estornar":
                    return EstornarResgate(evento, partes);
                case "ajuda":
                    EnviarAjuda(evento);
                    return true;
                default:
                    EnviarMensagem($"@{evento.UserName} - ação desconhecida. Use !plataforma ajuda.", evento.BroadcastId, somenteNaTransmissao: true);
                    return true;
            }
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao processar comando: " + ex.Message);
            return false;
        }
    }

    private bool ResgatarItem(Evento evento, string[] partes)
    {
        // Processa o resgate de um item da Plataforma
        if (partes.Length < 3)
        {
            EnviarMensagem($"@{evento.UserName} - uso correto: !plataforma resgatar [item] [informação opcional]", evento.BroadcastId, somenteNaTransmissao: true);
            return true;
        }

        string item = partes[2].ToLowerInvariant();
        string metadata = partes.Length >= 4 ? partes[3].Trim() : "";

        // Lê o argumento original para distinguir não membro de informação ausente.
        bool sponsorInformado = CPH.TryGetArg("userIsSponsor", out bool isSponsor);

        string nivelMembro = "";

        if (sponsorInformado && !isSponsor)
        {
            CPH.SetArgument("plataformaRemoverNivelUserId", evento.UserId);

            if (!CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "RemoverNivelMembroUsuario"))
            {
                LogError("ERRO: falha ao remover nível de membro desatualizado no banco de dados.");
                EnviarMensagem($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }
        }
        else
        {
            // Sem informação no evento, mantém a consulta ao nível cadastrado.
            CPH.SetArgument("plataformaConsultarNivelUserId", evento.UserId);

            bool consultouNivel = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ConsultarNivelMembroUsuario");
            if (!consultouNivel)
            {
                LogError("ERRO: falha ao consultar nível de membro no banco de dados.");
                EnviarMensagem($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.", evento.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("plataformaNivelMembro", out nivelMembro);
        }

        int percentualDesconto = ObterPercentualDesconto(nivelMembro);

        CPH.SetArgument("plataformaUserId", evento.UserId);
        CPH.SetArgument("plataformaUserName", evento.UserName);
        CPH.SetArgument("plataformaItem", item);
        CPH.SetArgument("plataformaOrigem", "CHAT");
        CPH.SetArgument("plataformaMetadata", metadata);
        CPH.SetArgument("plataformaVersaoRegra", VersaoRegraPlataforma);
        CPH.SetArgument("plataformaPercentualDesconto", percentualDesconto);
        CPH.SetArgument("plataformaNivelMembro", nivelMembro ?? "");

        bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ResgatarItemPlataforma");
        if (!executou)
        {
            LogError("ERRO: falha ao processar resgate no banco de dados.");
            EnviarMensagem($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.", evento.BroadcastId, somenteNaTransmissao: true);
            return false;
        }

        CPH.TryGetArg("plataformaResultado", out string resultado);

        switch (resultado)
        {
            case "Sucesso":
                CPH.TryGetArg("plataformaNomeItem", out string nomeItem);
                CPH.TryGetArg("plataformaValorItem", out int valorItem);
                CPH.TryGetArg("plataformaSaldoRestante", out int saldoRestante);
                CPH.TryGetArg("plataformaResgateId", out long resgateId);
                EnviarMensagem($"✅ @{evento.UserName} resgatou {nomeItem} por {valorItem:N0} Moedas! " + $"Saldo: {saldoRestante:N0} Moedas. Resgate #{resgateId}.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "SaldoInsuficiente":
                CPH.TryGetArg("plataformaNomeItem", out string nomeSaldo);
                CPH.TryGetArg("plataformaValorItem", out int valorNecessario);
                CPH.TryGetArg("plataformaSaldoAtual", out int saldoAtual);
                EnviarMensagem($"@{evento.UserName} - saldo insuficiente para {nomeSaldo}. " + $"Custo: {valorNecessario:N0} | Saldo: {saldoAtual:N0} Moedas.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "LimiteDiarioAtingido":
                CPH.TryGetArg("plataformaResgatesHoje", out int resgatesHoje);
                EnviarMensagem($"@{evento.UserName} - você já realizou {resgatesHoje} resgates hoje " + $"e atingiu sua cota diária.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "LimiteItemAtingido":
                CPH.TryGetArg("plataformaNomeItem", out string nomeLimite);
                CPH.TryGetArg("plataformaQuantidadeAtual", out int quantidadeAtual);
                CPH.TryGetArg("plataformaLimiteItem", out int limiteItem);
                EnviarMensagem($"@{evento.UserName} - você já atingiu o limite de {nomeLimite} " + $"({quantidadeAtual}/{limiteItem}).", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "PrerequisitoNaoCumprido":
                CPH.TryGetArg("plataformaItemPrerequisito", out string itemPrerequisito);
                EnviarMensagem($"@{evento.UserName} - esse item exige primeiro o resgate de " + $"'{itemPrerequisito}'.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "EstoqueEsgotado":
                CPH.TryGetArg("plataformaNomeItem", out string nomeEstoque);
                EnviarMensagem($"@{evento.UserName} - {nomeEstoque} está sem estoque no momento.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "ItemNaoEncontrado":
                EnviarMensagem($"@{evento.UserName} - item '{item}' não encontrado no catálogo da Plataforma.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "ItemInativo":
                CPH.TryGetArg("plataformaNomeItem", out string nomeInativo);
                EnviarMensagem($"@{evento.UserName} - {nomeInativo} não está disponível para resgate no momento.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "ParametrosInvalidos":
                EnviarMensagem($"@{evento.UserName} - não foi possível processar o resgate por parâmetros inválidos.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "OrigemInvalida":
                LogError("ERRO: origem inválida enviada ao banco de dados.");
                EnviarMensagem($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            default:
                LogError($"ERRO: resultado inesperado do resgate: '{resultado}'.");
                EnviarMensagem($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
        }

        return true;
    }

    private int ObterPercentualDesconto(string nivelMembro)
    {
        // Obtém o desconto do nível cadastrado; nível ausente ou desconhecido recebe zero
        return DescontosPorNivelMembro.TryGetValue(nivelMembro ?? "", out int desconto) ? desconto : 0;
    }

    private bool ConsultarItem(Evento evento, string[] partes)
    {
        // Consulta um item do catálogo da Plataforma
        if (partes.Length < 3)
        {
            EnviarMensagem($"@{evento.UserName} - uso correto: !plataforma item [item]", evento.BroadcastId, somenteNaTransmissao: true);
            return true;
        }

        string item = partes[2].ToLowerInvariant();

        CPH.SetArgument("plataformaConsultarItem", item);

        bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ConsultarItemPlataforma");
        if (!executou)
        {
            LogError("ERRO: falha ao consultar item no banco de dados.");
            EnviarMensagem($"@{evento.UserName} - ocorreu uma falha técnica ao consultar o item.", evento.BroadcastId, somenteNaTransmissao: true);
            return false;
        }

        CPH.TryGetArg("plataformaItemEncontrado", out bool encontrado);
        if (!encontrado)
        {
            EnviarMensagem($"@{evento.UserName} - item '{item}' não encontrado no catálogo da Plataforma.", evento.BroadcastId, somenteNaTransmissao: true);
            return true;
        }

        CPH.TryGetArg("plataformaItemJson", out string itemJson);

        PlataformaItem itemCatalogo = JsonConvert.DeserializeObject<PlataformaItem>(itemJson ?? "");
        if (itemCatalogo == null)
        {
            LogError("ERRO: item retornado pelo banco não pôde ser interpretado.");
            return false;
        }

        string tier = itemCatalogo.Tier > 0 ? $" | Tier {itemCatalogo.Tier}" : "";
        string estoque = itemCatalogo.EstoqueGlobal.HasValue ? $" | Estoque: {itemCatalogo.EstoqueGlobal.Value}" : "";
        string disponibilidade = itemCatalogo.Ativo ? "" : " | Indisponível para resgate";

        EnviarMensagem($"{itemCatalogo.NomeExibicao} | {itemCatalogo.Valor:N0} Moedas" + $"{tier} | Limite: {itemCatalogo.LimiteMaximo}{estoque}{disponibilidade}", evento.BroadcastId, somenteNaTransmissao: true);
        return true;
    }

    private bool EstornarResgate(Evento evento, string[] partes)
    {
        // Processa o estorno administrativo de um resgate da Plataforma
        bool ehDono = !string.IsNullOrEmpty(evento.BroadcastUserId) && evento.UserId == evento.BroadcastUserId;
        if (!evento.IsMod && !ehDono)
        {
            EnviarMensagem($"@{evento.UserName} - apenas moderadores podem estornar resgates.", evento.BroadcastId, somenteNaTransmissao: true);
            return true;
        }

        if (partes.Length < 3 || !long.TryParse(partes[2], out long resgateId) || resgateId <= 0)
        {
            EnviarMensagem($"@{evento.UserName} - uso correto: !plataforma estornar [id]", evento.BroadcastId, somenteNaTransmissao: true);
            return true;
        }

        CPH.SetArgument("plataformaEstornoResgateId", resgateId);

        bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "EstornarResgatePlataforma");
        if (!executou)
        {
            LogError("ERRO: falha ao estornar resgate no banco de dados.");
            EnviarMensagem($"@{evento.UserName} - ocorreu uma falha técnica ao estornar o resgate.", evento.BroadcastId, somenteNaTransmissao: true);
            return false;
        }

        CPH.TryGetArg("plataformaEstornoResultado", out string resultado);

        switch (resultado)
        {
            case "Sucesso":
                CPH.TryGetArg("plataformaEstornoValor", out int valorEstornado);
                EnviarMensagem($"✅ Resgate #{resgateId} estornado. " + $"{valorEstornado:N0} Moedas foram devolvidas ao usuário.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "ResgateNaoEncontradoOuJaEstornado":
                EnviarMensagem($"@{evento.UserName} - o resgate #{resgateId} não existe ou já foi estornado.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "UsuarioNaoEncontrado":
                EnviarMensagem($"@{evento.UserName} - não foi possível localizar o usuário do resgate #{resgateId}.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "FalhaAoEstornar":
                EnviarMensagem($"@{evento.UserName} - não foi possível alterar o resgate #{resgateId} para ESTORNADO.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            case "ParametrosInvalidos":
                EnviarMensagem($"@{evento.UserName} - identificador de resgate inválido.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
            default:
                LogError($"ERRO: resultado inesperado do estorno: '{resultado}'.");
                EnviarMensagem($"@{evento.UserName} - ocorreu uma falha técnica ao estornar o resgate.", evento.BroadcastId, somenteNaTransmissao: true);
                break;
        }

        return true;
    }

    private void EnviarAjuda(Evento evento)
    {
        // Exibe os comandos atualmente disponíveis da Plataforma
        EnviarMensagem($"@{evento.UserName} - Plataforma: !plataforma resgatar [item] [informação opcional] | !plataforma item [item]", evento.BroadcastId, somenteNaTransmissao: true);

        bool ehDono = !string.IsNullOrEmpty(evento.BroadcastUserId) && evento.UserId == evento.BroadcastUserId;
        if (evento.IsMod || ehDono)
        {
            EnviarMensagem($"Admin: !plataforma estornar [id]", evento.BroadcastId, somenteNaTransmissao: true);
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

    public class PlataformaItem
    {
        // Representa o item do catálogo recebido do Gerente de Banco de Dados
        public bool Ativo { get; set; }
        public bool Visivel { get; set; }

        public int Tier { get; set; }
        public int Valor { get; set; }
        public int LimiteMaximo { get; set; }
        public int? EstoqueGlobal { get; set; }

        public long Id { get; set; }
        public long? ItemPrerequisitoId { get; set; }

        public string Item { get; set; }
        public string NomeExibicao { get; set; }
        public string Descricao { get; set; }
        public string Categoria { get; set; }
        public string Slot { get; set; }
        public string ItemPrerequisito { get; set; }
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
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }
}