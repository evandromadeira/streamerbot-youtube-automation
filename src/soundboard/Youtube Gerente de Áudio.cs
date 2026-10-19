using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

// Atualização 261016.0905
public class CPHInline
{
    private const string Prefixo = "[GERENTE_AUDIO] ";

    public bool CadastrarAudio()
    {
        Evento evento = null;

        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }

            evento = contexto.Evento;
            Ambiente ambiente = contexto.Ambiente;

            bool ehDono = !string.IsNullOrEmpty(evento.BroadcastUserId) && evento.UserId == evento.BroadcastUserId;
            if (!evento.IsMod && !ehDono)
            {
                EnviarMensagem($"❌ @{evento.UserName}, apenas moderadores podem cadastrar áudios.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 5)
            {
                EnviarMensagem("⚠ Uso correto: !novoaudio [apelidos separados por vírgula] [custo] [cooldown em segundos] [arquivo.mp3]", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string aliasesRaw = partes[1];
            string custoStr = partes[2];
            string cooldownStr = partes[3];
            string arquivo = partes[4];

            if (!int.TryParse(custoStr, out int custo) || custo < 0)
            {
                EnviarMensagem($"⚠ @{evento.UserName}, o custo precisa ser um número inteiro maior ou igual a zero.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            if (!int.TryParse(cooldownStr, out int cooldownSegundos) || cooldownSegundos < 0)
            {
                EnviarMensagem($"⚠ @{evento.UserName}, o cooldown precisa ser um número inteiro de segundos maior ou igual a zero.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            if (!arquivo.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                EnviarMensagem($"⚠ @{evento.UserName}, o arquivo precisa terminar em .mp3", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            if (arquivo.Contains("/") || arquivo.Contains("\\") || arquivo.Contains(".."))
            {
                EnviarMensagem($"⚠ @{evento.UserName}, o nome do arquivo não pode conter caminhos (só o nome do .mp3).", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string caminhoArquivo = Path.Combine(ambiente.PastaAudios, arquivo);
            if (!File.Exists(caminhoArquivo))
            {
                EnviarMensagem($"❌ @{evento.UserName}, arquivo '{arquivo}' não encontrado na pasta de áudios.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            var aliases = aliasesRaw
                .Split(',')
                .Select(a => a.Trim())
                .Where(a => !string.IsNullOrEmpty(a))
                .Select(a => a.StartsWith("!") ? a : "!" + a)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            if (aliases.Count == 0)
            {
                EnviarMensagem($"⚠ @{evento.UserName}, informe pelo menos um comando (ex: hidratação,agua).", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.SetArgument("grupoAudioAliasesJson", JsonConvert.SerializeObject(aliases));

            bool grupoOk = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ObterOuCriarGrupoIdAudio");
            if (!grupoOk)
            {
                EnviarMensagem($"❌ @{evento.UserName}, falha ao resolver identificador do áudio.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("grupoIdResultado", out int grupoId);

            bool salvo = true;
            foreach (var alias in aliases)
            {
                var colunas = new Dictionary<string, object>
                {
                    { "grupoId", grupoId },
                    { "comando", alias },
                    { "arquivo", arquivo },
                    { "custo", custo },
                    { "cooldownSegundos", cooldownSegundos },
                    { "ativo", 1 }
                };

                CPH.SetArgument("salvarTabela", "YoutubeComandosAudio");
                CPH.SetArgument("salvarColunasJson", JsonConvert.SerializeObject(colunas));
                CPH.SetArgument("salvarChaveConflito", "comando");

                salvo = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SalvarRegistro");
                if (!salvo)
                {
                    break;
                }
            }

            if (!salvo)
            {
                EnviarMensagem($"❌ @{evento.UserName}, falha ao salvar o áudio. Verifique os logs.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            string listaAliases = string.Join(", ", aliases);
            EnviarMensagem($"✅ Áudio #{grupoId}: '{arquivo}' cadastrado! Comandos: {listaAliases}. Custo: {custo:N0} Moedas. Cooldown: {cooldownSegundos}s.", evento?.BroadcastId, somenteNaTransmissao: true);
            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao cadastrar áudio: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao cadastrar áudio.", evento?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }
    }

    public bool ListarAudios()
    {
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

            bool consultaOk = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ListarAudiosPorGrupo");

            if (!consultaOk)
            {
                EnviarMensagem("❌ Falha ao consultar a lista de áudios.", evento?.BroadcastId, somenteNaTransmissao: true);
                return false;
            }

            CPH.TryGetArg("audiosListaJson", out string audiosJson);
            var audios = string.IsNullOrEmpty(audiosJson) ? new List<AudioResumo>() : JsonConvert.DeserializeObject<List<AudioResumo>>(audiosJson);

            if (audios == null || audios.Count == 0)
            {
                EnviarMensagem("ℹ Nenhum áudio cadastrado no momento.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            foreach (var mensagem in MontarMensagensDeAudios(audios))
            {
                EnviarMensagem(mensagem, evento?.BroadcastId, somenteNaTransmissao: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError("ERRO CRÍTICO ao listar áudios: " + ex.Message);
            EnviarMensagem("❌ Falha técnica ao listar áudios.", evento?.BroadcastId, somenteNaTransmissao: true);
            return false;
        }
    }

    private List<string> MontarMensagensDeAudios(List<AudioResumo> audios)
    {
        const int limiteCaracteres = 200;
        const string prefixo = "Áudios: ";
        const string separador = " | ";

        var mensagens = new List<string>();
        var mensagemAtual = new StringBuilder(prefixo);
        bool mensagemVazia = true;

        foreach (var audio in audios)
        {
            string item = $"{audio.Comando} - ${audio.Custo:N0}";
            string pedaco = mensagemVazia ? item : separador + item;

            // Se não couber na mensagem atual, fecha essa mensagem e começa uma nova
            // (o item inteiro vai para a próxima — nunca é dividido).
            if (mensagemAtual.Length + pedaco.Length > limiteCaracteres && !mensagemVazia)
            {
                mensagens.Add(mensagemAtual.ToString());
                mensagemAtual = new StringBuilder(prefixo + item);
                mensagemVazia = false;
                continue;
            }

            mensagemAtual.Append(pedaco);
            mensagemVazia = false;
        }

        if (!mensagemVazia)
        {
            mensagens.Add(mensagemAtual.ToString());
        }

        return mensagens;
    }

    public bool ReproduzirAudio()
    {
        Evento evento = null;

        int custo = 0;
        int grupoId = 0;

        string operacaoId = Guid.NewGuid().ToString("D");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string comando = null;
        string arquivo = null;
        string caminhoArquivo = null;
        string debitoStatus = "nao_iniciado";
        string etapa = "consulta";

        try
        {
            Contexto contexto = ObterContexto();
            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                LogError("ERRO: não foi possível ler o contexto do evento.");
                return false;
            }

            evento = contexto.Evento;
            Ambiente ambiente = contexto.Ambiente;

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0)
            {
                return false;
            }

            comando = partes[0].ToLowerInvariant();
            if (!comando.StartsWith("!"))
            {
                return false;
            }

            CPH.SetArgument("buscarAudioComando", comando);

            bool buscaOk = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "BuscarAudioPorComando");
            if (!buscaOk)
            {
                return false;
            }

            CPH.TryGetArg("audioEncontrado", out bool audioEncontrado);
            if (!audioEncontrado)
            {
                return false;
            }

            CPH.TryGetArg("audioArquivo", out arquivo);
            CPH.TryGetArg("audioCusto", out custo);
            CPH.TryGetArg("audioGrupoId", out grupoId);
            CPH.TryGetArg("audioCooldownSegundos", out int cooldownSegundos);
            CPH.TryGetArg("audioUltimoUso", out string ultimoUsoStr);

            if (cooldownSegundos > 0 && !string.IsNullOrEmpty(ultimoUsoStr))
            {
                DateTime ultimoUso;
                if (DateTime.TryParse(ultimoUsoStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out ultimoUso))
                {
                    double segundosDesdeUltimoUso = (DateTime.Now - ultimoUso).TotalSeconds;
                    if (segundosDesdeUltimoUso < cooldownSegundos)
                    {
                        int restante = (int)Math.Ceiling(cooldownSegundos - segundosDesdeUltimoUso);
                        EnviarMensagem($"⏳ @{evento.UserName}, esse áudio está em cooldown. Tente novamente em {restante}s.", evento?.BroadcastId, somenteNaTransmissao: true);
                        return true;
                    }
                }
            }

            caminhoArquivo = Path.Combine(ambiente.PastaAudios, arquivo);
            if (!File.Exists(caminhoArquivo))
            {
                LogError($"ERRO: arquivo '{arquivo}' não encontrado na pasta de áudios.");
                EnviarMensagem($"❌ @{evento.UserName}, áudio indisponível no momento.", evento?.BroadcastId, somenteNaTransmissao: true);
                return true;
            }

            if (custo > 0)
            {
                CPH.SetArgument("debitarUserId", evento.UserId);
                CPH.SetArgument("debitarBroadcastUserId", evento.BroadcastUserId);
                CPH.SetArgument("debitarCusto", custo);

                etapa = "debito";
                debitoStatus = "incerto";

                bool debitou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "DebitarMoedasUsuario");
                if (!debitou)
                {
                    RegistrarFalhaAudio(operacaoId, timestamp, evento, comando, arquivo, caminhoArquivo, grupoId, custo, debitoStatus, etapa, "DebitarMoedasUsuario retornou false; consulte o log do banco para distinguir saldo insuficiente de falha técnica.");
                    EnviarMensagem($"❌ @{evento.UserName}, não foi possível confirmar o débito de {custo:N0} moedas para o áudio. Confira seu saldo.", evento?.BroadcastId, somenteNaTransmissao: true);
                    return true;
                }
            }

            debitoStatus = custo > 0 ? "debitado" : "gratuito";
            etapa = "reproducao";
            CPH.PlaySound(caminhoArquivo, 1, false, "", true);
            etapa = "cooldown";
            CPH.SetArgument("atualizarUltimoUsoGrupoId", grupoId);

            if (!CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "AtualizarUltimoUsoAudio"))
            {
                RegistrarFalhaAudio(operacaoId, timestamp, evento, comando, arquivo, caminhoArquivo, grupoId, custo, debitoStatus, etapa, "AtualizarUltimoUsoAudio retornou false; a chamada de reprodução já terminou sem exceção.");
            }
            return true;
        }
        catch (Exception ex)
        {
            RegistrarFalhaAudio(operacaoId, timestamp, evento, comando, arquivo, caminhoArquivo, grupoId, custo, debitoStatus, etapa, ex.ToString());

            if (etapa == "reproducao" && debitoStatus == "debitado")
            {
                try
                {
                    EnviarMensagem($"⚠ @{evento.UserName}, não foi possível confirmar a reprodução do áudio. Se ele não tocou, informe o streamer.", evento?.BroadcastId, somenteNaTransmissao: true);
                }
                catch (Exception avisoEx)
                {
                    LogError($"Falha ao enviar aviso da operação {operacaoId}: {avisoEx}");
                }
            }
            return etapa == "reproducao" || etapa == "cooldown";
        }
    }

    private void RegistrarFalhaAudio(string operacaoId, string timestamp, Evento evento, string comando, string arquivo, string caminhoArquivo, int grupoId, int custo, string debitoStatus, string etapa, string erro)
    {
        bool podeConferirDevolucao = debitoStatus == "debitado" && etapa == "reproducao";
        string comandoSugerido = podeConferirDevolucao && !string.IsNullOrWhiteSpace(evento?.UserName) && evento.UserName.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) < 0 ? $"!adicionar @{evento.UserName.TrimStart('@')} {custo}" : null;
        LogError("RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { operacaoId, timestamp, userId = evento?.UserId, userName = evento?.UserName, broadcastUserId = evento?.BroadcastUserId, broadcastUserName = evento?.BroadcastUserName, comando, arquivo, caminhoArquivo, grupoId, custo, quantidadeDebitadaConfirmada = debitoStatus == "debitado" ? custo : 0, debitoStatus, etapa, reproducaoRetornouSemExcecao = etapa == "cooldown", erro, comandoSugerido, orientacao = podeConferirDevolucao ? "Antes de devolver, confirmar que o áudio não tocou, conferir usuário/ID e verificar que ainda não houve devolução. O comando é apenas uma sugestão, nunca executado automaticamente." : "Não devolver automaticamente. Débito incerto exige conferência no banco; falha no cooldown não indica falha na reprodução." }));
    }

    public class AudioResumo
    {
        public int Custo { get; set; }

        public string Comando { get; set; }
    }

    public class Contexto
    {
        public Evento Evento { get; set; }
        public Ambiente Ambiente { get; set; }
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

    public class Evento
    {
        public bool IsMod { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }
        public string MessageText { get; set; }
        public string BroadcastId { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; } = "";

        public string PastaAudios => Path.Combine(PastaRaiz, "Data", "Audios");

        // Construtor vazio necessário para desserialização do contexto.
        public Ambiente()
        {
        }

        public Ambiente(IInlineInvokeProxy CPH)
        {
            PastaRaiz = CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true) ?? "";
        }
    }
}