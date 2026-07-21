using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

// Versão 260718.1540

public class CPHInline
{
    public bool Execute()
    {
        try
        {
            CPH.TryGetArg("contextoMensagem", out string contextoJson);
            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);

            if (contexto?.Evento == null || contexto.Ambiente == null)
            {
                CPH.LogError(">>> [NOVO_AUDIO] ERRO: contexto ausente ou inválido.");
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

            if (partes.Length != 4)
            {
                CPH.SendYouTubeMessage("⚠ Uso correto: !novoaudio [apelidos separados por vírgula] [custo] [arquivo.mp3]");
                return false;
            }

            string aliasesRaw = partes[1];
            string custoStr = partes[2];
            string arquivo = partes[3];

            if (!int.TryParse(custoStr, out int custo) || custo < 0)
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, o custo precisa ser um número inteiro maior ou igual a zero.");
                return false;
            }

            if (!arquivo.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, o arquivo precisa terminar em .mp3");
                return false;
            }

            string caminhoArquivo = Path.Combine(ambiente.PastaAudios, arquivo);
            if (!File.Exists(caminhoArquivo))
            {
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, arquivo '{arquivo}' não encontrado na pasta de áudios.");
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
                    { "grupoId", grupoId },
                    { "comando", alias },
                    { "arquivo", arquivo },
                    { "custo", custo },
                    { "ativo", 1 }
                };

                CPH.SetArgument("salvarTabela", "YoutubeComandosAudio");
                CPH.SetArgument("salvarColunasJson", JsonConvert.SerializeObject(colunas));
                CPH.SetArgument("salvarChaveConflito", "comando");
                CPH.SetArgument("salvarColunasSomenteInsercaoJson", JsonConvert.SerializeObject(new[] { "criadoPor", "criadoEm" }));

                salvo = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "SalvarRegistro");
                if (!salvo) break;
            }

            if (!salvo)
            {
                CPH.SendYouTubeMessage($"❌ @{evento.UserName}, falha ao salvar o áudio. Verifique os logs.");
                return false;
            }

            string listaAliases = string.Join(", ", aliases);
            CPH.SendYouTubeMessage($"✅ Áudio #{grupoId} '{arquivo}' cadastrado! Comandos: {listaAliases} — custo: {custo:N0} pontos.");

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [NOVO_AUDIO] ERRO CRÍTICO: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao cadastrar áudio.");
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