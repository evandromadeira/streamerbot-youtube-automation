using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// Atualização 261005.1310
public class CPHInline
{
    private const int VersaoRegraPlataforma = 1;
    private static readonly Dictionary<string, int> DescontosPorNivelMembro = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "TULIPA_BRONZE", 5 },
        { "TULIPA_PRATA", 10 },
        { "TULIPA_OURO", 15 },
        { "TULIPA_PLATINA", 20 }
    };

    // ------------------------------------------------------------------
    // Processa os comandos relacionados à Plataforma
    // ------------------------------------------------------------------
    public bool ProcessarComando()
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO: não foi possível ler o contexto do evento.");
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
                    CPH.SendYouTubeMessage($"@{evento.UserName} - ação desconhecida. Use !plataforma ajuda.");
                    return true;
            }
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO CRÍTICO ao processar comando: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Processa o resgate de um item da Plataforma
    // ------------------------------------------------------------------
    private bool ResgatarItem(Evento evento, string[] partes)
    {
        if (partes.Length < 3)
        {
            CPH.SendYouTubeMessage($"@{evento.UserName} - uso correto: !plataforma resgatar [item] [informação opcional]");
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
                CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO: falha ao remover nível de membro desatualizado no banco de dados.");
                CPH.SendYouTubeMessage($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.");
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
                CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO: falha ao consultar nível de membro no banco de dados.");
                CPH.SendYouTubeMessage($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.");
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
            CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO: falha ao processar resgate no banco de dados.");
            CPH.SendYouTubeMessage($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.");
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
                CPH.SendYouTubeMessage($"✅ @{evento.UserName} resgatou {nomeItem} por {valorItem:N0} Moedas! " + $"Saldo: {saldoRestante:N0} Moedas. Resgate #{resgateId}.");
                break;
            case "SaldoInsuficiente":
                CPH.TryGetArg("plataformaNomeItem", out string nomeSaldo);
                CPH.TryGetArg("plataformaValorItem", out int valorNecessario);
                CPH.TryGetArg("plataformaSaldoAtual", out int saldoAtual);
                CPH.SendYouTubeMessage($"@{evento.UserName} - saldo insuficiente para {nomeSaldo}. " + $"Custo: {valorNecessario:N0} | Saldo: {saldoAtual:N0} Moedas.");
                break;
            case "LimiteDiarioAtingido":
                CPH.TryGetArg("plataformaResgatesHoje", out int resgatesHoje);
                CPH.SendYouTubeMessage($"@{evento.UserName} - você já realizou {resgatesHoje} resgates hoje " + $"e atingiu sua cota diária.");
                break;
            case "LimiteItemAtingido":
                CPH.TryGetArg("plataformaNomeItem", out string nomeLimite);
                CPH.TryGetArg("plataformaQuantidadeAtual", out int quantidadeAtual);
                CPH.TryGetArg("plataformaLimiteItem", out int limiteItem);
                CPH.SendYouTubeMessage($"@{evento.UserName} - você já atingiu o limite de {nomeLimite} " + $"({quantidadeAtual}/{limiteItem}).");
                break;
            case "PrerequisitoNaoCumprido":
                CPH.TryGetArg("plataformaItemPrerequisito", out string itemPrerequisito);
                CPH.SendYouTubeMessage($"@{evento.UserName} - esse item exige primeiro o resgate de " + $"'{itemPrerequisito}'.");
                break;
            case "EstoqueEsgotado":
                CPH.TryGetArg("plataformaNomeItem", out string nomeEstoque);
                CPH.SendYouTubeMessage($"@{evento.UserName} - {nomeEstoque} está sem estoque no momento.");
                break;
            case "ItemNaoEncontrado":
                CPH.SendYouTubeMessage($"@{evento.UserName} - item '{item}' não encontrado no catálogo da Plataforma.");
                break;
            case "ItemInativo":
                CPH.TryGetArg("plataformaNomeItem", out string nomeInativo);
                CPH.SendYouTubeMessage($"@{evento.UserName} - {nomeInativo} não está disponível para resgate no momento.");
                break;
            case "ParametrosInvalidos":
                CPH.SendYouTubeMessage($"@{evento.UserName} - não foi possível processar o resgate por parâmetros inválidos.");
                break;
            case "OrigemInvalida":
                CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO: origem inválida enviada ao banco de dados.");
                CPH.SendYouTubeMessage($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.");
                break;
            default:
                CPH.LogError($">>> [GERENTE_DE_PLATAFORMA] ERRO: resultado inesperado do resgate: '{resultado}'.");
                CPH.SendYouTubeMessage($"@{evento.UserName} - ocorreu uma falha técnica ao processar o resgate.");
                break;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Obtém o desconto do nível cadastrado; nível ausente ou desconhecido recebe zero
    // ------------------------------------------------------------------
    private int ObterPercentualDesconto(string nivelMembro)
    {
        return DescontosPorNivelMembro.TryGetValue(nivelMembro ?? "", out int desconto) ? desconto : 0;
    }

    // ------------------------------------------------------------------
    // Consulta um item do catálogo da Plataforma
    // ------------------------------------------------------------------
    private bool ConsultarItem(Evento evento, string[] partes)
    {
        if (partes.Length < 3)
        {
            CPH.SendYouTubeMessage($"@{evento.UserName} - uso correto: !plataforma item [item]");
            return true;
        }

        string item = partes[2].ToLowerInvariant();

        CPH.SetArgument("plataformaConsultarItem", item);

        bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ConsultarItemPlataforma");

        if (!executou)
        {
            CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO: falha ao consultar item no banco de dados.");
            CPH.SendYouTubeMessage($"@{evento.UserName} - ocorreu uma falha técnica ao consultar o item.");
            return false;
        }

        CPH.TryGetArg("plataformaItemEncontrado", out bool encontrado);

        if (!encontrado)
        {
            CPH.SendYouTubeMessage($"@{evento.UserName} - item '{item}' não encontrado no catálogo da Plataforma.");
            return true;
        }

        CPH.TryGetArg("plataformaItemJson", out string itemJson);

        var itemCatalogo = JsonConvert.DeserializeObject<PlataformaItem>(itemJson ?? "");

        if (itemCatalogo == null)
        {
            CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO: item retornado pelo banco não pôde ser interpretado.");
            return false;
        }

        string tier = itemCatalogo.Tier > 0 ? $" | Tier {itemCatalogo.Tier}" : "";
        string estoque = itemCatalogo.EstoqueGlobal.HasValue ? $" | Estoque: {itemCatalogo.EstoqueGlobal.Value}" : "";
        string disponibilidade = itemCatalogo.Ativo ? "" : " | Indisponível para resgate";

        CPH.SendYouTubeMessage($"{itemCatalogo.NomeExibicao} | {itemCatalogo.Valor:N0} Moedas" + $"{tier} | Limite: {itemCatalogo.LimiteMaximo}{estoque}{disponibilidade}");
        return true;
    }

    // ------------------------------------------------------------------
    // Processa o estorno administrativo de um resgate da Plataforma
    // ------------------------------------------------------------------
    private bool EstornarResgate(Evento evento, string[] partes)
    {
        bool ehDono = !string.IsNullOrEmpty(evento.BroadcastUserId) && evento.UserId == evento.BroadcastUserId;

        if (!evento.IsMod && !ehDono)
        {
            CPH.SendYouTubeMessage($"@{evento.UserName} - apenas moderadores podem estornar resgates.");
            return true;
        }

        if (partes.Length < 3 || !long.TryParse(partes[2], out long resgateId) || resgateId <= 0)
        {
            CPH.SendYouTubeMessage($"@{evento.UserName} - uso correto: !plataforma estornar [id]");
            return true;
        }

        CPH.SetArgument("plataformaEstornoResgateId", resgateId);

        bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "EstornarResgatePlataforma");

        if (!executou)
        {
            CPH.LogError(">>> [GERENTE_DE_PLATAFORMA] ERRO: falha ao estornar resgate no banco de dados.");
            CPH.SendYouTubeMessage($"@{evento.UserName} - ocorreu uma falha técnica ao estornar o resgate.");
            return false;
        }

        CPH.TryGetArg("plataformaEstornoResultado", out string resultado);

        switch (resultado)
        {
            case "Sucesso":
                CPH.TryGetArg("plataformaEstornoValor", out int valorEstornado);
                CPH.SendYouTubeMessage($"✅ Resgate #{resgateId} estornado. " + $"{valorEstornado:N0} Moedas foram devolvidas ao usuário.");
                break;
            case "ResgateNaoEncontradoOuJaEstornado":
                CPH.SendYouTubeMessage($"@{evento.UserName} - o resgate #{resgateId} não existe ou já foi estornado.");
                break;
            case "UsuarioNaoEncontrado":
                CPH.SendYouTubeMessage($"@{evento.UserName} - não foi possível localizar o usuário do resgate #{resgateId}.");
                break;
            case "FalhaAoEstornar":
                CPH.SendYouTubeMessage($"@{evento.UserName} - não foi possível alterar o resgate #{resgateId} para ESTORNADO.");
                break;
            case "ParametrosInvalidos":
                CPH.SendYouTubeMessage($"@{evento.UserName} - identificador de resgate inválido.");
                break;
            default:
                CPH.LogError($">>> [GERENTE_DE_PLATAFORMA] ERRO: resultado inesperado do estorno: '{resultado}'.");
                CPH.SendYouTubeMessage($"@{evento.UserName} - ocorreu uma falha técnica ao estornar o resgate.");
                break;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Exibe os comandos atualmente disponíveis da Plataforma
    // ------------------------------------------------------------------
    private void EnviarAjuda(Evento evento)
    {
        CPH.SendYouTubeMessage($"@{evento.UserName} - Plataforma: !plataforma resgatar [item] [informação opcional] | !plataforma item [item]");

        bool ehDono = !string.IsNullOrEmpty(evento.BroadcastUserId) && evento.UserId == evento.BroadcastUserId;

        if (evento.IsMod || ehDono)
        {
            CPH.SendYouTubeMessage($"Admin: !plataforma estornar [id]");
        }
    }

    // ------------------------------------------------------------------
    // Obtém o contexto criado pelo Gerente de Chat
    // ------------------------------------------------------------------
    private Contexto ObterContexto()
    {
        CPH.TryGetArg("contextoJson", out string contextoJson);

        if (string.IsNullOrEmpty(contextoJson))
            return null;

        return JsonConvert.DeserializeObject<Contexto>(contextoJson);
    }

    // ------------------------------------------------------------------
    // Representa o item do catálogo recebido do Gerente de Banco de Dados
    // ------------------------------------------------------------------
    public class PlataformaItem
    {
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

        public bool Ativo { get; set; }
        public bool Visivel { get; set; }
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
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