using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Atualização 260722.1545
public class CPHInline
{
    public bool Execute()
    {
        return true;
    }

    // ------------------------------------------------------------------
    // Cadastro de áudio (!novoaudio)
    // ------------------------------------------------------------------
    public bool CadastrarAudio()
    {
        try
        {
            CPH.TryGetArg("contextoMensagem", out string contextoJson);
            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);

            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                CPH.LogError(">>> [AUDIO] ERRO: contexto ausente ou inválido.");
                return false;
            }

            var evento = contexto.Evento;
            var ambiente = contexto.Ambiente;

            bool ehDono = !string.IsNullOrEmpty(evento.BroadcastUserId) && evento.UserId == evento.BroadcastUserId;
            if (!evento.IsMod && !ehDono)
            {
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, apenas moderadores podem cadastrar áudios.");
                return false;
            }

            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 5)
            {
                CPH.SendYouTubeMessage("⚠ Uso correto: !novoaudio [apelidos separados por vírgula] [custo] [cooldown em segundos] [arquivo.mp3]");
                return false;
            }

            string aliasesRaw = partes[1];
            string custoStr = partes[2];
            string cooldownStr = partes[3];
            string arquivo = partes[4];

            if (!int.TryParse(custoStr, out int custo) || custo < 0)
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, o custo precisa ser um número inteiro maior ou igual a zero.");
                return false;
            }

            if (!int.TryParse(cooldownStr, out int cooldownSegundos) || cooldownSegundos < 0)
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, o cooldown precisa ser um número inteiro de segundos maior ou igual a zero.");
                return false;
            }

            if (!arquivo.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, o arquivo precisa terminar em .mp3");
                return false;
            }

            if (arquivo.Contains("/") || arquivo.Contains("\\") || arquivo.Contains(".."))
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, o nome do arquivo não pode conter caminhos (só o nome do .mp3).");
                return false;
            }

            string caminhoArquivo = Path.Combine(ambiente.PastaAudios, arquivo);
            if (!File.Exists(caminhoArquivo))
            {
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, arquivo '{arquivo}' não encontrado na pasta de áudios.");
                return false;
            }

            var aliases = aliasesRaw.Split(',').Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)).Select(a => a.StartsWith("!") ? a : "!" + a).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (aliases.Count == 0)
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, informe pelo menos um comando (ex: hidratação,agua).");
                return false;
            }

            CPH.SetArgument("grupoAudioAliasesJson", JsonConvert.SerializeObject(aliases));
            bool grupoOk = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "ObterOuCriarGrupoIdAudio");
            if (!grupoOk)
            {
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, falha ao resolver identificador do áudio.");
                return false;
            }

            CPH.TryGetArg("grupoIdResultado", out int grupoId);
            bool salvo = true;
            foreach (var alias in aliases)
            {
                var colunas = new Dictionary<string, object>
                {
                    { "grupoId", grupoId }, { "comando", alias }, { "arquivo", arquivo }, { "custo", custo }, { "cooldownSegundos", cooldownSegundos }, { "ativo", 1 }
                };

                CPH.SetArgument("salvarTabela", "YoutubeComandosAudio");
                CPH.SetArgument("salvarColunasJson", JsonConvert.SerializeObject(colunas));
                CPH.SetArgument("salvarChaveConflito", "comando");

                salvo = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SalvarRegistro");
                if (!salvo)
                    break;
            }

            if (!salvo)
            {
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, falha ao salvar o áudio. Verifique os logs.");
                return false;
            }

            string listaAliases = string.Join(", ", aliases);
            CPH.SendYouTubeMessage($"✅ Áudio #{grupoId}: '{arquivo}' cadastrado! Comandos: {listaAliases}. Custo: {custo:N0} pontos. Cooldown: {cooldownSegundos}s.");
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [AUDIO] ERRO CRÍTICO ao cadastrar áudio: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao cadastrar áudio.");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Reprodução de áudio (comandos avulsos, ex: !agua)
    // ------------------------------------------------------------------
    public bool ReproduzirAudio()
    {
        try
        {
            CPH.TryGetArg("contextoMensagem", out string contextoJson);
            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);
            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                CPH.LogError(">>> [AUDIO] ERRO: contexto ausente ou inválido.");
                return false;
            }

            var evento = contexto.Evento;
            var ambiente = contexto.Ambiente;

            string comando = (evento.MessageText ?? "").Trim().Split(' ')[0].ToLower();
            if (string.IsNullOrEmpty(comando) || !comando.StartsWith("!"))
                return false;

            CPH.SetArgument("buscarAudioComando", comando);
            bool buscaOk = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "BuscarAudioPorComando");
            if (!buscaOk)
                return false;

            CPH.TryGetArg("audioEncontrado", out bool audioEncontrado);
            if (!audioEncontrado)
                return false;

            CPH.TryGetArg("audioArquivo", out string arquivo);
            CPH.TryGetArg("audioCusto", out int custo);
            CPH.TryGetArg("audioGrupoId", out int grupoId);
            CPH.TryGetArg("audioCooldownSegundos", out int cooldownSegundos);
            CPH.TryGetArg("audioUltimoUso", out string ultimoUsoStr);

            if (cooldownSegundos > 0 && !string.IsNullOrEmpty(ultimoUsoStr))
            {
                DateTime ultimoUso;
                if (DateTime.TryParse(ultimoUsoStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out ultimoUso))
                {
                    double segundosDesdeUltimoUso = (DateTime.UtcNow - ultimoUso).TotalSeconds;
                    if (segundosDesdeUltimoUso < cooldownSegundos)
                    {
                        int restante = (int)Math.Ceiling(cooldownSegundos - segundosDesdeUltimoUso);
                        CPH.SendYouTubeMessage($"⏳ @{evento.UserName}, esse áudio está em cooldown. Tente novamente em {restante}s.");
                        return true;
                    }
                }
            }

            string caminhoArquivo = Path.Combine(ambiente.PastaAudios, arquivo);
            if (!File.Exists(caminhoArquivo))
            {
                CPH.LogError($">>> [AUDIO] ERRO: arquivo '{arquivo}' não encontrado na pasta de áudios.");
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, áudio indisponível no momento.");
                return true;
            }

            if (custo > 0)
            {
                CPH.SetArgument("debitarUserId", evento.UserId);
                CPH.SetArgument("debitarBroadcastUserId", evento.BroadcastUserId);
                CPH.SetArgument("debitarCusto", custo);
                
                bool debitou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "DebitarPontos");
                if (!debitou)
                {
                    CPH.SendYouTubeMessage($"❌ @{evento.UserName}, saldo insuficiente (custo: {custo:N0} pontos).");
                    return true;
                }
            }

            CPH.PlaySound(caminhoArquivo, 1, false, "", true);
            CPH.SetArgument("atualizarUltimoUsoGrupoId", grupoId);
            CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "AtualizarUltimoUsoAudio");
            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [AUDIO] ERRO CRÍTICO ao reproduzir áudio: " + ex.Message);
            return false;
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
        public bool IsSub { get; set; }
        public bool IsSpo { get; set; }
        public bool IsMod { get; set; }
    }

    public class Ambiente
    {
        public string PastaRaiz { get; set; }
        public string PastaStream { get; set; }
        public string CaminhoBanco { get; set; }
        public string PastaAudios { get; set; }
    }
}