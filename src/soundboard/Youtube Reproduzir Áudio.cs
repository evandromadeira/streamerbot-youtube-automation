using System;
using System.IO;
using Newtonsoft.Json;

// Atualização 260721.1755

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
                CPH.LogError(">>> [REPRODUZIR_AUDIO] ERRO: contexto ausente ou inválido.");
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
                CPH.LogError($">>> [REPRODUZIR_AUDIO] ERRO: arquivo '{arquivo}' não encontrado na pasta de áudios.");
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
            CPH.LogError(">>> [REPRODUZIR_AUDIO] ERRO CRÍTICO: " + ex.Message);
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