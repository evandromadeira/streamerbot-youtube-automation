using System;
using Newtonsoft.Json;

// Atualização 261016.0855
public class CPHInline
{
    private const string Prefixo = "[GERENTE_PERFIL] ";

    public bool ProcessarComando()
    {
        // Processa os comandos relacionados ao Perfil do Usuário
        Evento evento = null;

        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
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
                EnviarMensagem($"@{evento.UserName} - apenas moderadores podem alterar perfis.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            if (partes.Length < 4)
            {
                EnviarMensagem($"@{evento.UserName} - uso correto: !perfil @usuario [campo] [valor]. Use !perfil ajuda para ver as opções.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            string usuario = partes[1].Trim();
            string campo = partes[2].ToLowerInvariant();
            string valor = partes[3].Trim();

            if (usuario.StartsWith("@"))
            {
                usuario = usuario.Substring(1);
            }

            if (string.IsNullOrEmpty(usuario))
            {
                EnviarMensagem($"@{evento.UserName} - usuário inválido.", evento?.BroadcastId, somenteNaTransmissao: true);
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
                    EnviarMensagem($"@{evento.UserName} - campo de perfil '{campo}' não reconhecido. Use !perfil ajuda.", evento?.BroadcastId, somenteNaTransmissao: true);
                    return true;
            }
        }
        catch (Exception ex)
        {
            LogError($"ERRO CRÍTICO ao processar comando: {ex.Message}");

            if (evento != null)
            {
                InformarFalhaAtualizacao(evento);
            }
            return false;
        }
    }

    private void EnviarAjuda(Evento evento)
    {
        // Exibe os comandos e valores disponíveis para o Perfil do Usuário
        EnviarMensagem($"@{evento.UserName} - Perfil: permite definir nível de membro e informações do usuário. Apenas moderadores podem alterar esses dados.", evento?.BroadcastId, somenteNaTransmissao: true);
        EnviarMensagem("Uso: !perfil @usuario membro [nível] | Tulipa Bronze, Tulipa Prata, Tulipa Ouro ou Tulipa Platina.", evento?.BroadcastId, somenteNaTransmissao: true);
        EnviarMensagem("Uso: !perfil @usuario categoria [valor] | Masculino, Feminino ou Outro.", evento?.BroadcastId, somenteNaTransmissao: true);
    }

    private bool AtualizarNivelMembro(Evento evento, string usuario, string valor)
    {
        // Atualiza o nível de membro de um usuário
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
                EnviarMensagem($"@{evento.UserName} - nível inválido. Use Tulipa Bronze, Tulipa Prata, Tulipa Ouro ou Tulipa Platina.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
        }

        return AtualizarPerfil(evento, usuario, "nivelMembro", nivel);
    }

    private bool AtualizarSexo(Evento evento, string usuario, string valor)
    {
        // Atualiza o sexo de um usuário
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
                EnviarMensagem($"@{evento.UserName} - valor inválido. Use Masculino, Feminino ou Outro.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
        }

        return AtualizarPerfil(evento, usuario, "sexo", sexo);
    }

    private bool AtualizarPerfil(Evento evento, string usuario, string campo, string valor)
    {
        // Envia uma atualização de perfil para o Gerente de Banco de Dados
        CPH.SetArgument("perfilUserName", usuario);
        CPH.SetArgument("perfilCampo", campo);
        CPH.SetArgument("perfilValor", valor);
        CPH.SetArgument("perfilOrigem", "CHAT");

        bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "AtualizarPerfilUsuario");

        if (!executou)
        {
            LogError("ERRO: falha ao atualizar perfil no banco de dados.");
            InformarFalhaAtualizacao(evento);
            return false;
        }

        CPH.TryGetArg("perfilResultado", out string resultado);

        switch (resultado)
        {
            case "Sucesso":
                string campoExibicao = campo == "nivelMembro" ? "nível de membro" : "informação";
                EnviarMensagem($"Perfil de @{usuario} atualizado: {campoExibicao} = {valor}.", evento?.BroadcastId, somenteNaTransmissao: true);
                break;
            case "UsuarioNaoEncontrado":
                EnviarMensagem($"@{evento.UserName} - usuário @{usuario} não encontrado.", evento?.BroadcastId, somenteNaTransmissao: true);
                break;
            case "CampoInvalido":
                LogError($"ERRO: campo inválido enviado ao banco: '{campo}'.");
                InformarFalhaAtualizacao(evento);
                return false;
            case "ParametrosInvalidos":
                LogError("ERRO: parâmetros inválidos enviados ao banco.");
                InformarFalhaAtualizacao(evento);
                return false;
            default:
                LogError($"ERRO: resultado inesperado do banco: '{resultado}'.");
                InformarFalhaAtualizacao(evento);
                return false;
        }

        return true;
    }

    private void InformarFalhaAtualizacao(Evento evento)
    {
        // Avisa o solicitante de que a alteração do perfil não pôde ser concluída
        EnviarMensagem($"@{evento.UserName} - não foi possível atualizar o perfil. Tente novamente.", evento?.BroadcastId, somenteNaTransmissao: true);
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

    public class Evento
    {
        public bool IsMod { get; set; }

        public string UserName { get; set; }

        public string MessageText { get; set; }
        public string BroadcastId { get; set; }
    }
}