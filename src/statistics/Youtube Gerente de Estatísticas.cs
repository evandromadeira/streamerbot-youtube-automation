using System;
using Newtonsoft.Json;

// Atualização 260904.1140
public class CPHInline
{
    public bool ConsultarPresenca()
    {
        return ConsultarPresencaBase("ObterDiasDePresenca", "");
    }

    public bool ConsultarPresencaMes()
    {
        return ConsultarPresencaBase("ObterDiasDePresencaNoMes", " neste mês");
    }

    private bool ConsultarPresencaBase(string metodoBanco, string sufixoMensagem)
    {
        try
        {
            var contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [GERENTE_DE_ESTATISTICAS] ERRO: contexto inválido.");
                return false;
            }
            var evento = contexto.Evento;

            string mensagem = evento.MessageText ?? "";
            string[] partes = mensagem.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            string userIdConsulta = null;
            string userNameConsulta;

            if (partes.Length > 1)
            {
                userNameConsulta = partes[1].TrimStart('@');
            }
            else
            {
                userIdConsulta = evento.UserId;
                userNameConsulta = evento.UserName;
            }

            CPH.SetArgument("presencaUserId", userIdConsulta ?? "");
            CPH.SetArgument("presencaUserName", userNameConsulta ?? "");

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", metodoBanco);
            if (!executou)
            {
                CPH.LogError(">>> [GERENTE_DE_ESTATISTICAS] ERRO: falha ao consultar dias de presença.");
                return false;
            }

            CPH.TryGetArg("presencaEncontrado", out bool encontrado);
            CPH.TryGetArg("presencaDias", out int dias);
            CPH.TryGetArg("presencaUserNameResolvido", out string nomeResolvido);

            string nomeExibicao = !string.IsNullOrEmpty(nomeResolvido) ? nomeResolvido : userNameConsulta;

            string resposta = (!encontrado || dias == 0)
                ? $"@{nomeExibicao} ainda não te vi por aqui{sufixoMensagem}! 👀"
                : $"@{nomeExibicao} já participou de {dias} live{(dias > 1 ? "s" : "")}{sufixoMensagem}! 🎥";

            CPH.SendYouTubeMessage(resposta);

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($">>> [GERENTE_DE_ESTATISTICAS] ERRO CRÍTICO: {ex.Message}");
            return false;
        }
    }

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
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
    }
}