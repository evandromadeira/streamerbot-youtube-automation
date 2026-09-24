using System;
using Newtonsoft.Json;

// Atualização 260924.1600
public class CPHInline
{
    // ------------------------------------------------------------------
    // Processa os comandos relacionados ao Perfil do Usuário
    // ------------------------------------------------------------------
    public bool ProcessarComando()
    {
        Evento evento = null;

        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_DE_PERFIL] ERRO: não foi possível ler o contexto do evento.");
                return false;
            }

            evento = contexto.Evento;

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length < 2 || string.Equals(partes[1], "ajuda", StringComparison.OrdinalIgnoreCase))
            {
                EnviarAjuda(evento);
                return true;
            }

            if (!evento.IsMod)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - apenas moderadores podem alterar perfis.");
                return true;
            }

            if (partes.Length < 4)
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - uso correto: !perfil @usuario [campo] [valor]. Use !perfil ajuda para ver as opções.");
                return true;
            }

            string usuario = partes[1].Trim();
            string campo = partes[2].ToLowerInvariant();
            string valor = partes[3].Trim();

            if (usuario.StartsWith("@"))
                usuario = usuario.Substring(1);

            if (string.IsNullOrEmpty(usuario))
            {
                CPH.SendYouTubeMessage($"@{evento.UserName} - usuário inválido.");
                return true;
            }

            switch (campo)
            {
                case "membro":
                    return AtualizarNivelMembro(evento, usuario, valor);
                case "sexo":
                case "categoria":
                    return AtualizarSexo(evento, usuario, valor);
                default:
                    CPH.SendYouTubeMessage($"@{evento.UserName} - campo de perfil '{campo}' não reconhecido. Use !perfil ajuda.");
                    return true;
            }
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DE_PERFIL] ERRO CRÍTICO ao processar comando: {ex.Message}");
            if (evento != null)
                InformarFalhaAtualizacao(evento);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Exibe os comandos e valores disponíveis para o Perfil do Usuário
    // ------------------------------------------------------------------
    private void EnviarAjuda(Evento evento)
    {
        CPH.SendYouTubeMessage($"@{evento.UserName} - Perfil: permite definir nível de membro e informações do usuário. Apenas moderadores podem alterar esses dados.");
        CPH.SendYouTubeMessage("Uso: !perfil @usuario membro [nível] | Tulipa Bronze, Tulipa Prata, Tulipa Ouro ou Tulipa Platina. Exemplo: !perfil @usuario membro Tulipa Ouro");
        CPH.SendYouTubeMessage("Uso: !perfil @usuario categoria [valor] | Masculino, Feminino ou Outro. Exemplo: !perfil @usuario categoria Feminino");
    }

    // ------------------------------------------------------------------
    // Atualiza o nível de membro de um usuário
    // ------------------------------------------------------------------
    private bool AtualizarNivelMembro(Evento evento, string usuario, string valor)
    {
        string nivel;

        switch (valor.ToLowerInvariant())
        {
            case "tulipa bronze":
                nivel = "TULIPA_BRONZE";
                break;
            case "tulipa prata":
                nivel = "TULIPA_PRATA";
                break;
            case "tulipa ouro":
                nivel = "TULIPA_OURO";
                break;
            case "tulipa platina":
                nivel = "TULIPA_PLATINA";
                break;
            default:
                CPH.SendYouTubeMessage($"@{evento.UserName} - nível inválido. Use Tulipa Bronze, Tulipa Prata, Tulipa Ouro ou Tulipa Platina.");
                return true;
        }

        return AtualizarPerfil(evento, usuario, "nivelMembro", nivel);
    }

    // ------------------------------------------------------------------
    // Atualiza o sexo de um usuário
    // ------------------------------------------------------------------
    private bool AtualizarSexo(Evento evento, string usuario, string valor)
    {
        string sexo;

        switch (valor.ToLowerInvariant())
        {
            case "masculino":
                sexo = "MASCULINO";
                break;
            case "feminino":
                sexo = "FEMININO";
                break;
            case "outro":
                sexo = "OUTRO";
                break;
            default:
                CPH.SendYouTubeMessage($"@{evento.UserName} - valor inválido. Use Masculino, Feminino ou Outro.");
                return true;
        }

        return AtualizarPerfil(evento, usuario, "sexo", sexo);
    }

    // ------------------------------------------------------------------
    // Envia uma atualização de perfil para o Gerente de Banco de Dados
    // ------------------------------------------------------------------
    private bool AtualizarPerfil(Evento evento, string usuario, string campo, string valor)
    {
        CPH.SetArgument("perfilUserName", usuario);
        CPH.SetArgument("perfilCampo", campo);
        CPH.SetArgument("perfilValor", valor);
        CPH.SetArgument("perfilOrigem", "CHAT");

        bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "AtualizarPerfilUsuario");

        if (!executou)
        {
            CPH.LogError(">>> [GERENTE_DE_PERFIL] ERRO: falha ao atualizar perfil no banco de dados.");
            InformarFalhaAtualizacao(evento);
            return false;
        }

        CPH.TryGetArg("perfilResultado", out string resultado);

        switch (resultado)
        {
            case "Sucesso":
                string campoExibicao = campo == "nivelMembro" ? "nível de membro" : "informação";
                CPH.SendYouTubeMessage($"Perfil de @{usuario} atualizado: {campoExibicao} = {valor}.");
                break;
            case "UsuarioNaoEncontrado":
                CPH.SendYouTubeMessage($"@{evento.UserName} - usuário @{usuario} não encontrado.");
                break;
            case "CampoInvalido":
                CPH.LogError($">>> [GERENTE_DE_PERFIL] ERRO: campo inválido enviado ao banco: '{campo}'.");
                InformarFalhaAtualizacao(evento);
                return false;
            case "ParametrosInvalidos":
                CPH.LogError(">>> [GERENTE_DE_PERFIL] ERRO: parâmetros inválidos enviados ao banco.");
                InformarFalhaAtualizacao(evento);
                return false;
            default:
                CPH.LogError($">>> [GERENTE_DE_PERFIL] ERRO: resultado inesperado do banco: '{resultado}'.");
                InformarFalhaAtualizacao(evento);
                return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Avisa o solicitante de que a alteração do perfil não pôde ser concluída
    // ------------------------------------------------------------------
    private void InformarFalhaAtualizacao(Evento evento)
    {
        CPH.SendYouTubeMessage($"@{evento.UserName} - não foi possível atualizar o perfil. Tente novamente.");
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