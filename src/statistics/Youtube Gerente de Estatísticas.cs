using System;
using Newtonsoft.Json;

// Atualização 261011.2020
public class CPHInline
{
    private const string Prefixo = "[GERENTE_DE_ESTATISTICAS] ";

    public bool Execute()
    {
        return ReceberInscritosStudio();
    }

    public bool ReceberInscritosStudio()
    {
        try
        {
            CPH.TryGetArg("inscritosJson", out string inscritosJson);
            if (string.IsNullOrWhiteSpace(inscritosJson))
            {
                throw new ArgumentException("Contagem do Studio ausente.");
            }

            string broadcastUserId = CPH.YouTubeGetBroadcaster()?.UserId ?? "";

            ContagemInscritos contagem = JsonConvert.DeserializeObject<ContagemInscritos>(inscritosJson);
            if (contagem == null || string.IsNullOrWhiteSpace(broadcastUserId) || contagem.CanalId != broadcastUserId || !contagem.Total.HasValue || contagem.Total.Value < 0)
            {
                throw new ArgumentException("Canal ou contagem do Studio inválidos.");
            }

            // ------------------------------------------------------------------
            // Youtube Gerente de OBS: atualiza o contador de inscritos.
            // ------------------------------------------------------------------
            CPH.SetArgument("inscritosJson", JsonConvert.SerializeObject(contagem));

            if (!CPH.ExecuteMethod("Youtube Gerente de OBS", "AtualizarContadorInscritos"))
            {
                LogError("Não foi possível atualizar o contador de inscritos no OBS.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("Erro ao atualizar inscritos: " + ex.Message);
            return false;
        }
    }

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
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null)
            {
                LogError("ERRO: contexto inválido.");
                return false;
            }

            Evento evento = contexto.Evento;

            string mensagem = evento.MessageText ?? "";
            string userIdConsulta = null;
            string userNameConsulta;

            string[] partes = mensagem.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (partes.Length > 1)
            {
                userNameConsulta = partes[1].TrimStart('@');
            }
            else
            {
                userIdConsulta = evento.UserId;
                userNameConsulta = evento.UserName;
            }

            // ------------------------------------------------------------------
            // Youtube Gerente de Banco de Dados: consulta os dias de presença.
            // ------------------------------------------------------------------
            CPH.SetArgument("presencaUserId", userIdConsulta ?? "");
            CPH.SetArgument("presencaUserName", userNameConsulta ?? "");

            CPH.SetArgument("presencaEncontrado", false);
            CPH.SetArgument("presencaDias", 0);
            CPH.SetArgument("presencaUserNameResolvido", "");

            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", metodoBanco);
            if (!executou)
            {
                LogError("ERRO: falha ao consultar dias de presença.");
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
            LogError($"ERRO CRÍTICO: {ex.Message}");
            return false;
        }
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

    private void LogError(string mensagem)
    {
        CPH.LogError(Prefixo + mensagem);
    }

    public class ContagemInscritos
    {
        public long? Total { get; set; }

        public string CanalId { get; set; }
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