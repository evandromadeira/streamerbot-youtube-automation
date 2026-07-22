using System;
using Newtonsoft.Json;

// Atualização 260722.1117
public class CPHInline
{
    public bool Execute()
    {
        try
        {
            CPH.TryGetArg("contextoMensagem", out string contextoJson);
            var contexto = string.IsNullOrEmpty(contextoJson) ? null : JsonConvert.DeserializeObject<Contexto>(contextoJson);
            if (contexto?.Evento == null)
            {
                CPH.LogError(">>> [TRANSFERIR_PONTOS] ERRO: contexto ausente ou inválido.");
                return false;
            }

            var evento = contexto.Evento;
            string[] partes = (evento.MessageText ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 3)
            {
                CPH.SendYouTubeMessage("⚠ Uso correto: !transferir [@usuario] [quantidade]");
                return false;
            }

            string destinatarioNome = partes[1].TrimStart('@').Trim();
            string quantidadeStr = partes[2];
            if (string.IsNullOrEmpty(destinatarioNome))
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, informe o usuário que vai receber os pontos.");
                return false;
            }

            if (!int.TryParse(quantidadeStr, out int quantidade) || quantidade <= 0)
            {
                CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, a quantidade precisa ser um número inteiro maior que zero.");
                return false;
            }

            CPH.SetArgument("transferirRemetenteUserId", evento.UserId);
            CPH.SetArgument("transferirDestinatarioNome", destinatarioNome);
            CPH.SetArgument("transferirQuantidade", quantidade);
            bool executou = CPH.ExecuteMethod("Youtube Gerente de Banco de Dados", "TransferirPontos");
            if (!executou)
            {
                CPH.SendYouTubeMessage("❌ Falha técnica ao transferir pontos.");
                return false;
            }

            CPH.TryGetArg("transferirResultado", out string resultado);
            switch (resultado)
            {
                case "Sucesso":
                    CPH.TryGetArg("transferirDestinatarioNomeExibido", out string nomeDestinoExibido);
                    string nomeFinal = string.IsNullOrEmpty(nomeDestinoExibido) ? destinatarioNome : nomeDestinoExibido;
                    CPH.SendYouTubeMessage($"✅ @{evento.UserName} transferiu {quantidade:N0} pontos para @{nomeFinal}!");
                    break;
                case "DestinatarioNaoEncontrado":
                    CPH.SendYouTubeMessage($"❌ @{evento.UserName}, o usuário '{destinatarioNome}' não foi encontrado.");
                    break;
                case "AutoTransferencia":
                    CPH.SendYouTubeMessage($"⚠ @{evento.UserName}, você não pode transferir pontos para si mesmo.");
                    break;
                case "SaldoInsuficiente":
                    CPH.TryGetArg("transferirSaldoRemetente", out int saldoRemetente);
                    CPH.SendYouTubeMessage($"❌ @{evento.UserName}, saldo insuficiente! Você possui apenas {saldoRemetente:N0} pontos.");
                    break;
                default:
                    CPH.SendYouTubeMessage("❌ Falha técnica ao transferir pontos.");
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError(">>> [TRANSFERIR_PONTOS] ERRO CRÍTICO: " + ex.Message);
            CPH.SendYouTubeMessage("❌ Falha técnica ao transferir pontos.");
            return false;
        }
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