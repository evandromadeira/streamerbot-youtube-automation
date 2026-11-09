using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Atualização 261105.1620
// Padrões aplicados: 5
// Revisão concluída: 1
// Chamado pelos gerentes de Chat e Doações; SincronizarPendentes é chamado por Action temporizada.
public class CPHInline
{
    private const int DuracaoMaximaSincronizacaoMs = 5000;
    private const int LimiteTentativasRegistro = 3;
    private const int IntervaloTentativasConsultaMs = 150;

    private const decimal CentavosPorReal = 100m;

    private const string Prefixo = "[MINECRAFT_BRIDGE] ";
    private const string GerenteBanco = "Youtube Gerente de Banco de Dados";

    private static readonly object SincronizacaoPendentes = new object();
    private static long ultimoResgateConsultado;
    private static string mundoDaConsulta = "";

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    public bool ProcessarComando()
    {
        Resposta resposta = new Resposta("NAO_ENVIADO", "Comando não processado.");

        string nome = "";
        string usuario = "";
        string motivoChat = "";

        try
        {
            string contextoJson = LerTexto("contextoJson");
            if (string.IsNullOrWhiteSpace(contextoJson))
            {
                throw new ArgumentException("Contexto do Gerente de Chat não informado.");
            }

            JObject evento = JObject.Parse(contextoJson)["Evento"] as JObject;
            if (evento == null)
            {
                throw new ArgumentException("Evento de chat inválido.");
            }

            string mensagem = Texto(evento, "MessageText");

            var partes = mensagem.Trim().Split((char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 0 || !partes[0].Equals("!minecraft", StringComparison.OrdinalIgnoreCase))
            {
                motivoChat = "USO";
                throw new ArgumentException("Comando diferente de !minecraft.");
            }

            if (partes.Length == 1)
            {
                string link = LerTexto("minecraftCatalogUrl");

                Uri url;

                string ajuda = Uri.TryCreate(link, UriKind.Absolute, out url) && (url.Scheme == "https" || url.Scheme == "http") ? "Catálogo Minecraft: " + link : "Use !minecraft resgatar <nome da recompensa>.";

                EnviarMensagem(ajuda, null, somenteNaTransmissao: false);

                return Publicar(new Resposta("NAO_ENVIADO", ajuda), true);
            }
            if (partes.Length != 3 || !partes[1].Equals("resgatar", StringComparison.OrdinalIgnoreCase))
            {
                motivoChat = "USO";
                throw new ArgumentException("Use !minecraft resgatar <nome da recompensa>.");
            }

            usuario = ValidarTexto(Texto(evento, "UserName"), "Usuário", 80);
            nome = ValidarTexto(partes[2].Trim(), "Nome do efeito", 256);
            string userId = ValidarTexto(Texto(evento, "UserId"), "ID do usuário", 256);
            string canalId = ValidarTexto(Texto(evento, "BroadcastUserId"), "ID do canal", 256);
            string nomeCanal = (string)evento["BroadcastUserName"] ?? "";
            string mensagemId = ValidarTexto(Texto(evento, "MessageId"), "ID da mensagem", 1024);
            string mundo = MundoConfigurado();

            CPH.SetArgument("origem", "moedas");

            if (!BancoConfigurado())
            {
                throw new InvalidOperationException("Resgates por moedas exigem banco configurado.");
            }

            // Youtube Gerente de Banco de Dados: consulta o saldo antes de qualquer pedido ao jogo.
            long saldo = ConsultarSaldoComTentativas(userId, canalId);
            if (saldo <= 0)
            {
                resposta = new Resposta("NAO_ENVIADO", "Saldo insuficiente para resgatar.");
                resposta.Log = "Resgate do Minecraft recusado por saldo insuficiente: Usuário '" + usuario + "' (" + userId + ") | Saldo " + saldo.ToString("N0", Cultura) + ".";
                return ResponderChat(resposta, false, usuario, nome, motivo: "SALDO");
            }

            string id = IdentificarResgate(canalId, userId, mensagemId);
            resposta = Enviar("streamerbot reserve " + Citar(nome) + " " + Citar(usuario), mundo, id);
            bool reservaPossivel = resposta.Recibo || resposta.Estado == "INCERTO";
            if (reservaPossivel)
            {
                // A reserva congela o preço, mas ainda não autoriza a execução.
                resposta = ConsultarReciboComTentativas(mundo, id);
            }
            if (!resposta.Recibo)
            {
                if (reservaPossivel)
                {
                    EnviarEtapa("isolate", mundo, id);
                }
                return ResponderChat(resposta, false, usuario, nome, motivo: reservaPossivel ? "CONFERIR" : "");
            }

            string regraReservada = resposta.RegraId;
            long custoReservado = resposta.Custo;
            if (custoReservado < 1 || custoReservado > int.MaxValue)
            {
                throw new InvalidOperationException("Preço de resgate inválido; nenhuma cobrança autorizada.");
            }
            PrepararRegistro(id, userId, canalId, regraReservada, mundo, custoReservado, usuario, nomeCanal);

            bool cobrar = false;
            if (resposta.Estado == "RESERVADO")
            {
                if (resposta.PagamentoIncerto)
                {
                    return ResponderChat(new Resposta("INCERTO", "Reserva antiga isolada para conferência: " + id + ". Nenhuma cobrança ou execução repetida."), false, usuario, nome);
                }
                if (saldo < custoReservado)
                {
                    Resposta cancelamento = EnviarEtapa("cancel", mundo, id);
                    resposta = new Resposta("NAO_ENVIADO", "Saldo insuficiente." + (cancelamento.Recibo && cancelamento.Estado == "FALHOU" ? " Reserva cancelada." : " Confira o cancelamento da reserva " + id + "."));
                    resposta.Log = "Resgate do Minecraft recusado por saldo insuficiente: Usuário '" + usuario + "' (" + userId + ") | Regra Id '" + regraReservada + "' | Valor " + custoReservado.ToString("N0", Cultura) + " | Saldo " + saldo.ToString("N0", Cultura) + ".";
                    return ResponderChat(resposta, false, usuario, nome, custoReservado, "SALDO");
                }

                // O aceite persistido libera a fila antes da cobrança; nenhuma falha financeira o desfaz.
                resposta = EnviarEtapa("confirm", mundo, id);
                if (!resposta.Recibo || resposta.Estado == "RESERVADO")
                {
                    resposta = ConsultarReciboComTentativas(mundo, id);
                }
                if (resposta.Recibo && (resposta.RegraId != regraReservada || resposta.Custo != custoReservado))
                {
                    resposta = new Resposta("INCERTO", "Confirmação divergente da reserva; conferir operação " + id + ".");
                }
                cobrar = resposta.Recibo && (resposta.Estado == "PENDENTE" || resposta.Estado == "EXECUTANDO" || resposta.Estado == "EXECUTADO" || resposta.Estado == "PARCIAL");
                if (!resposta.Recibo || resposta.Estado == "RESERVADO")
                {
                    EnviarEtapa("isolate", mundo, id);
                }
            }

            return ConcluirResgateMoedas(resposta, cobrar, nome, id, userId, usuario, regraReservada, custoReservado);
        }
        catch (Exception ex)
        {
            LogError("Falha no resgate: " + ex.Message);

            resposta.Mensagem = ex.Message + (resposta.Recibo ? " Confira a operação antes de repetir." : "");

            return ResponderChat(resposta, false, usuario, nome, motivo: motivoChat.Length > 0 ? motivoChat : resposta.Recibo || resposta.Estado == "INCERTO" ? "CONFERIR" : "INDISPONIVEL");
        }
    }

    public bool ProcessarDoacao()
    {
        // Consulta e reserva o envio no Banco; as tentativas de recuperação nunca repetem o pedido ao jogo.
        Resposta resposta = new Resposta("NAO_ENVIADO", "Nenhum pedido enviado por esta chamada.");
        Contexto contexto = null;
        Doacao doacao = null;

        try
        {
            contexto = LerContexto();
            doacao = contexto?.Doacao;
            if (doacao == null)
            {
                throw new ArgumentException("Contexto da doação ausente.");
            }

            RegistroContribuicao dados = PrepararContribuicao(doacao);
            RegistroEnvio envio = new RegistroEnvio
            {
                EnvioId = Guid.NewGuid().ToString("D"),
                Dados = dados
            };

            string consulta = RegistrarContribuicaoComRecuperacao(envio, "CONSULTAR", null);
            if (consulta != "AUSENTE")
            {
                throw new InvalidOperationException("Operação existente, conflitante ou consulta não confirmada; nenhum pedido enviado ao jogo.");
            }

            string inicial = RegistrarContribuicaoComRecuperacao(envio, "PREPARAR_ENVIO", null);
            if (inicial != "SALVO")
            {
                throw new InvalidOperationException("Reserva desta execução não confirmada no Banco; nenhum pedido enviado ao jogo.");
            }

            // O pedido ao jogo fica fora de todas as tentativas de consulta e gravação no Banco.
            resposta = Enviar(dados.Comando, dados.Mundo, dados.OperacaoId);
            envio.Resultado = resposta;
            RegistrarContribuicaoComRecuperacao(envio, "RECUPERAR_RESULTADO", resposta);
            return Publicar(resposta, resposta.Aceito, false);
        }
        catch (Exception ex)
        {
            LogError("RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { Doacao = doacao, etapa = "minecraft", estado = resposta.Estado, erro = ex.ToString(), orientacao = "Confira a operação no Banco e na ponte antes de qualquer intervenção; nenhum efeito reenviado automaticamente." }));
            if (resposta.Estado == "NAO_ENVIADO")
            {
                resposta.Mensagem = "Nenhum pedido enviado por esta chamada: " + ex.Message;
            }
            return Publicar(resposta, false, false);
        }
        finally
        {
            if (doacao != null)
            {
                doacao.MinecraftAceito = resposta.Aceito;
                doacao.MinecraftStatus = resposta.Estado;
                doacao.MinecraftMensagem = resposta.Mensagem;
            }
            CPH.SetArgument("contextoJson", contexto == null ? "" : JsonConvert.SerializeObject(contexto));
        }
    }

    public bool SincronizarPendentes()
    {
        // Consulta periódica dos recibos; nunca debita, confirma reservas ou reenvia efeitos.
        if (!System.Threading.Monitor.TryEnter(SincronizacaoPendentes))
        {
            return true;
        }

        try
        {
            if (!BancoConfigurado())
            {
                return true;
            }

            string mundo = MundoConfigurado();
            if (mundoDaConsulta != mundo)
            {
                ultimoResgateConsultado = 0;
                mundoDaConsulta = mundo;
            }

            CPH.SetArgument("minecraftConsultaMundo", mundo);
            CPH.SetArgument("minecraftConsultaAposId", ultimoResgateConsultado);
            CPH.SetArgument("minecraftPendentesJson", "[]");

            if (ChamarBanco("ListarResgatesMinecraftPendentes") != "CONSULTADO")
            {
                return false;
            }

            JArray pendentes = JArray.Parse(LerTexto("minecraftPendentesJson"));
            if (pendentes.Count == 0)
            {
                ultimoResgateConsultado = 0;
                return true;
            }

            var tempo = System.Diagnostics.Stopwatch.StartNew();
            foreach (JObject pendente in pendentes)
            {
                long registroId = pendente.Value<long>("id");
                string operacaoId = ValidarId(Texto(pendente, "operacaoId"));

                ConferirRegistroResgate(operacaoId, true, mundo, out bool interromperConsulta);
                if (interromperConsulta)
                {
                    break;
                }

                ultimoResgateConsultado = registroId;
                if (tempo.ElapsedMilliseconds >= DuracaoMaximaSincronizacaoMs)
                {
                    break;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            LogError("Falha ao sincronizar pendências: " + ex.Message);
            return false;
        }
        finally
        {
            System.Threading.Monitor.Exit(SincronizacaoPendentes);
        }
    }

    public bool ConferirResgate()
    {
        return ConferirRegistroResgate(LerTexto("minecraftOperacaoId"), false, null, out bool interromperConsulta);
    }

    private bool ConferirRegistroResgate(string operacaoId, bool automatica, string mundoDaSincronizacao, out bool interromperConsulta)
    {
        // Consulta pelo ID persistido. Nenhum efeito é reenviado e nenhuma reserva é confirmada.
        interromperConsulta = false;
        Resposta resposta = new Resposta("NAO_ENVIADO", "Consulta não realizada.");

        try
        {
            string id = ValidarId(operacaoId);

            if (!BancoConfigurado())
            {
                throw new InvalidOperationException("Conferência exige banco configurado.");
            }

            CPH.SetArgument("minecraftOperacaoId", id);

            string financeiro = ChamarBanco("ConsultarResgateMinecraft", out bool consultaConfirmada);
            if (!consultaConfirmada)
            {
                throw new InvalidOperationException("Consulta ao banco não confirmada.");
            }

            if (financeiro != "DEBITADO" && financeiro != "PENDENTE" && financeiro != "EXECUTADO" && financeiro != "PARCIAL" && financeiro != "FALHOU" && financeiro != "INCERTO" && financeiro != "ESTORNADO")
            {
                throw new InvalidOperationException("Registro indisponível; nenhuma execução enviada.");
            }

            bool estornoPendente = financeiro == "FALHOU" && LerTexto("minecraftStatusCobranca") == "CONFIRMADA";
            if (automatica && financeiro != "PENDENTE" && financeiro != "DEBITADO" && financeiro != "INCERTO" && !estornoPendente)
            {
                return true;
            }

            if (financeiro == "ESTORNADO")
            {
                return Publicar(new Resposta("FALHOU", "Resgate já estornado."), true);
            }

            string mundo = ValidarTexto(LerTexto("mundoEsperado"), "Mundo registrado", 256);
            string origem = LerTexto("origem");
            string regra = LerTexto("minecraftRegraId");

            if (automatica && mundo != mundoDaSincronizacao)
            {
                return true;
            }

            resposta = EnviarEtapa("status", mundo, id);
            if (automatica && !resposta.Recibo)
            {
                // Mundo fechado, destino incorreto ou resposta incerta: preservar o banco.
                interromperConsulta = resposta.Estado == "NAO_ENVIADO" || resposta.Estado == "RECUSADO";
                return false;
            }
            if (!resposta.Recibo || (regra.Length > 0 && resposta.RegraId != regra))
            {
                throw new InvalidOperationException("Consulta não confirmou o recibo registrado.");
            }

            if (resposta.Estado == "RESERVADO")
            {
                if (automatica)
                {
                    return true;
                }
                throw new InvalidOperationException("Reserva sem confirmação. Conferir pagamento; nenhum efeito reenviado.");
            }

            if (origem == "moedas")
            {
                CPH.TryGetArg("minecraftCusto", out int custo);
                if (resposta.Custo != custo)
                {
                    throw new InvalidOperationException("Preço do recibo diferente do registro.");
                }
            }

            CPH.SetArgument("minecraftRegraId", resposta.RegraId);

            string estado = EstadoRegistro(resposta, true);
            if (automatica && estado == financeiro && !estornoPendente)
            {
                return true;
            }

            CPH.SetArgument("minecraftCobrancaInicial", "INCERTA");
            string registrado = origem == "moedas" ? OperarMoedas(estado, resposta.Mensagem) : RegistrarContribuicao("ATUALIZAR", estado, resposta.Mensagem);

            bool confirmou = origem == "moedas" ? registrado == estado || registrado == "ESTORNADO" : registrado == "SALVO";

            resposta.Mensagem = "Operação " + id + ": " + resposta.Estado + "; registro: " + registrado + (origem == "moedas" ? "; cobrança: " + LerTexto("minecraftStatusCobranca") : "") + ".";

            return Publicar(resposta, confirmou, !automatica);
        }
        catch (Exception ex)
        {
            LogError("Falha na conferência: " + ex.Message);

            resposta.Mensagem = ex.Message;

            return Publicar(resposta, false, !automatica);
        }
    }

    private bool ConcluirResgateMoedas(Resposta resposta, bool cobrar, string nome, string id, string userId, string usuario, string regra, long custo)
    {
        // Mantém o resultado do jogo mesmo que cobrança, consulta ou registro falhem.
        string estado = EstadoRegistro(resposta, true);
        string cobranca;
        CPH.SetArgument("minecraftStatusRegistro", estado);
        if (cobrar)
        {
            cobranca = OperarMoedas("DEBITAR", resposta.Mensagem, incluirDadosLog: true);
            if (cobranca == "INCERTO")
            {
                cobranca = ConsultarCobrancaComTentativas();
            }
        }
        else
        {
            // Repetição de uma mensagem ou recibo antigo só consulta; nunca cobra depois.
            cobranca = ConsultarCobrancaComTentativas();
        }

        CPH.SetArgument("minecraftCobrancaInicial", !cobrar && cobranca == "AUSENTE" ? "NAO_REALIZADA" : "INCERTA");

        string registrado = OperarMoedas(estado, resposta.Mensagem, incluirDadosLog: estado == "EXECUTADO");
        if (registrado == estado || registrado == "ESTORNADO")
        {
            cobranca = LerTexto("minecraftStatusCobranca");
        }
        string detalheCobranca = cobranca == "CONFIRMADA" ? "Cobrança confirmada." :
            cobranca == "ESTORNADA" ? "Moedas estornadas após falha confirmada." :
            cobranca == "NAO_REALIZADA" ? "Cobrança não realizada; recompensa mantida." :
            "Cobrança pendente de conferência; nenhuma nova cobrança automática.";

        if (resposta.Estado == "EXECUTADO")
        {
            resposta.Mensagem = nome + ": resgate concluído. Custo: " + custo.ToString("N0", Cultura) + " moedas. " + detalheCobranca;
            resposta.Log = MontarLogResgate("executado", userId, usuario, regra, custo) + " | " + detalheCobranca;
        }
        else if (resposta.Estado == "PENDENTE" || resposta.Estado == "EXECUTANDO")
        {
            resposta.Mensagem = nome + ": resgate aceito e na fila. Custo: " + custo.ToString("N0", Cultura) + " moedas. " + detalheCobranca;
        }
        else
        {
            resposta.Mensagem = "Resgate " + id + ": " + resposta.Estado + ". " + detalheCobranca;
        }
        if (registrado != estado && registrado != "ESTORNADO")
        {
            resposta.Mensagem += " Registro pendente de conferência (" + id + ").";
            resposta.Log = null;
        }
        if (cobranca != "CONFIRMADA" && cobranca != "ESTORNADA")
        {
            LogWarn("Operação " + id + " | Execução: " + estado + " | Cobrança: " + cobranca + " | " + detalheCobranca);
        }
        return ResponderChat(resposta, resposta.Aceito, usuario, nome, custo);
    }

    private RegistroContribuicao PrepararContribuicao(Doacao doacao)
    {
        // Congela os dados usados pelo jogo e pelo Banco para todas as tentativas da mesma operação.
        string id = ValidarId(doacao.ProcessamentoId);
        string usuario = ValidarTexto(doacao.Usuario, "Doador", 80);
        string tipo = doacao.TriggerName;
        string origem = tipo == "Tip" ? "livePixPayPal" : tipo == "Super Chat" || tipo == "Super Sticker" ? "superChatSticker" : null;
        if (origem == null)
        {
            throw new ArgumentException("Tipo de contribuição ainda não integrado ao Minecraft.");
        }

        long centavos = checked((long)decimal.Round(doacao.ValorBRL * CentavosPorReal, 0, MidpointRounding.AwayFromZero));
        if (doacao.ValorBRL <= 0 || centavos < 1)
        {
            throw new ArgumentException("Valor da contribuição inválido.");
        }

        return new RegistroContribuicao
        {
            OperacaoId = id,
            Origem = origem,
            UsuarioId = doacao.UsuarioId ?? "",
            Usuario = usuario,
            CanalId = doacao.BroadcastUserId ?? "",
            Canal = doacao.BroadcastUserName ?? "",
            Mundo = MundoConfigurado(),
            ValorBRL = doacao.ValorBRL,
            DoacaoId = doacao.RegistroStatus == "salvo" && doacao.RegistroId > 0 ? doacao.RegistroId : 0,
            Comando = "streamerbot donation " + origem + " " + centavos.ToString(CultureInfo.InvariantCulture) + " " + Citar(usuario)
        };
    }

    private string RegistrarContribuicaoComRecuperacao(RegistroEnvio envio, string operacao, Resposta resposta)
    {
        // Youtube Gerente de Banco de Dados: até três tentativas por etapa, com os dados e o envio originais.
        RegistroContribuicao dados = envio.Dados;
        string estado = resposta == null ? "INCERTO" : EstadoRegistro(resposta, false);
        string erro = "";

        for (int tentativa = 1; tentativa <= LimiteTentativasRegistro; tentativa++)
        {
            string chamadaId = Guid.NewGuid().ToString("D");
            try
            {
                PrepararRegistro(dados.OperacaoId, dados.UsuarioId, dados.CanalId, resposta?.RegraId ?? "", dados.Mundo, 0, dados.Usuario, dados.Canal, dados.DoacaoId);
                CPH.SetArgument("origem", dados.Origem);
                CPH.SetArgument("doacaoValorBRL", (double)dados.ValorBRL);
                CPH.SetArgument("doacaoUserId", dados.UsuarioId);
                CPH.SetArgument("doacaoBroadcastUserId", dados.CanalId);
                CPH.SetArgument("minecraftDbOperacao", operacao);
                CPH.SetArgument("minecraftStatusRegistro", estado);
                CPH.SetArgument("minecraftMensagemResultado", resposta?.Mensagem ?? "");
                CPH.SetArgument("minecraftEnvioId", envio.EnvioId);
                CPH.SetArgument("minecraftRegistroChamadaId", chamadaId);
                CPH.SetArgument("minecraftDbResultado", "INCERTO");
                CPH.SetArgument("minecraftRegistroRetorno", "");

                CPH.ExecuteMethod(GerenteBanco, "RegistrarContribuicaoMinecraft");

                RetornoRegistro retorno = JsonConvert.DeserializeObject<RetornoRegistro>(LerTexto("minecraftRegistroRetorno"));
                if (retorno != null && retorno.OperacaoId == dados.OperacaoId && retorno.ChamadaId == chamadaId)
                {
                    if (operacao == "CONSULTAR" && retorno.Resultado == "AUSENTE")
                    {
                        return retorno.Resultado;
                    }
                    if (retorno.Resultado == "EXISTENTE" || retorno.Resultado == "CONFLITO")
                    {
                        LogWarn("RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { etapa = operacao, envio, retorno, orientacao = "Operação existente ou conflitante; não reenviar nem sobrescrever o histórico." }));
                        return retorno.Resultado;
                    }
                    if ((operacao != "CONSULTAR" && retorno.Resultado == "SALVO" && retorno.Status == estado) || (operacao == "RECUPERAR_RESULTADO" && retorno.Resultado == "PRESERVADO" && (retorno.Status == "EXECUTADO" || retorno.Status == "PARCIAL" || retorno.Status == "FALHOU" || retorno.Status == "PENDENTE")))
                    {
                        if (tentativa > 1 || retorno.Resultado == "PRESERVADO")
                        {
                            LogInfo("REGISTRO_RECUPERADO " + JsonConvert.SerializeObject(new { dados.OperacaoId, operacao, tentativa, retorno }));
                        }
                        return retorno.Resultado;
                    }
                }
                erro = "Confirmação ausente, inválida ou de outra tentativa.";
            }
            catch (Exception ex)
            {
                erro = ex.ToString();
            }

            LogWarn("REGISTRO_NAO_CONFIRMADO " + JsonConvert.SerializeObject(new { dados.OperacaoId, chamadaId, operacao, tentativa, estado, erro }));
        }

        LogError("RECUPERACAO_MANUAL " + JsonConvert.SerializeObject(new { etapa = operacao, envio, estado, erro, orientacao = "Conferir a gravação por operacaoId. Repetir somente o registro, nunca o pedido ao jogo, crédito ou cobrança." }));
        return "INCERTO";
    }

    private void PrepararRegistro(string id, string usuario, string canal, string regra, string mundo, long custo, string nomeUsuario, string nomeCanal, long doacaoId = 0)
    {
        CPH.SetArgument("minecraftOperacaoId", id);
        CPH.SetArgument("minecraftUserId", usuario);
        CPH.SetArgument("minecraftCanalId", canal);
        CPH.SetArgument("minecraftRegraId", regra);
        CPH.SetArgument("mundoEsperado", mundo);
        CPH.SetArgument("minecraftCusto", checked((int)custo));
        CPH.SetArgument("minecraftUserName", nomeUsuario);
        CPH.SetArgument("minecraftBroadcastUserName", nomeCanal);
        CPH.SetArgument("minecraftDoacaoId", doacaoId);
    }

    private string OperarMoedas(string operacao, string mensagem = "", bool incluirDadosLog = false)
    {
        CPH.SetArgument("minecraftDbOperacao", operacao);
        CPH.SetArgument("minecraftMensagemResultado", mensagem);
        CPH.SetArgument("minecraftIncluirDadosLog", incluirDadosLog);

        return ChamarBanco("ProcessarResgateMinecraft");
    }

    private string RegistrarContribuicao(string operacao, string estado, string mensagem = "")
    {
        CPH.SetArgument("minecraftDbOperacao", operacao);
        CPH.SetArgument("minecraftStatusRegistro", estado);
        CPH.SetArgument("minecraftMensagemResultado", mensagem);

        return ChamarBanco("RegistrarContribuicaoMinecraft");
    }

    private string ChamarBanco(string metodo)
    {
        return ChamarBanco(metodo, out bool confirmou);
    }

    private string ChamarBanco(string metodo, out bool confirmou)
    {
        confirmou = false;

        CPH.SetArgument("minecraftDbResultado", "INCERTO");

        try
        {
            bool chamou = CPH.ExecuteMethod(GerenteBanco, metodo);

            confirmou = chamou;

            string resultado = LerTexto("minecraftDbResultado");
            // SEM_DEBITO só é produzido após verificação/rollback confirmado no gerente do banco.
            return resultado == "SEM_DEBITO" || (chamou && !string.IsNullOrWhiteSpace(resultado)) ? resultado : "INCERTO";
        }
        catch (Exception ex)
        {
            LogError("Falha em " + metodo + ": " + ex.Message);
            return "INCERTO";
        }
    }

    private Resposta EnviarEtapa(string etapa, string mundo, string operacao)
    {
        return Enviar("streamerbot " + etapa + " " + operacao, mundo, operacao, true);
    }

    private Resposta Enviar(string comando, string mundo, string operacao, bool consultaOuConfirmacao = false)
    {
        string transporte = consultaOuConfirmacao ? Guid.NewGuid().ToString("D") : operacao;

        bool iniciouEnvio = false;

        CPH.SetArgument("minecraftRequestId", transporte);

        try
        {
            ValidarTexto(comando, "Comando", 4096);
            ValidarTexto(mundo, "Mundo", 256);

            string token = CPH.GetGlobalVar<string>("minecraftYoutubeBridgeToken", true);
            if (string.IsNullOrWhiteSpace(token) || token.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                return new Resposta("NAO_ENVIADO", "Configure minecraftYoutubeBridgeToken.");
            }

            int porta = 8766;

            object configuracaoPorta = CPH.GetGlobalVar<object>("minecraftBridgePort", true);
            if (configuracaoPorta != null && (!int.TryParse(Convert.ToString(configuracaoPorta, CultureInfo.InvariantCulture), NumberStyles.None,
                CultureInfo.InvariantCulture, out porta) || porta < 1 || porta > 65535))
            {
                return new Resposta("NAO_ENVIADO", "minecraftBridgePort inválida.");
            }

            byte[] corpo = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { id = transporte, command = comando, expectedWorld = mundo }));

            HttpWebRequest pedido = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + porta + "/v1/commands");
            pedido.Method = "POST";
            pedido.Proxy = null;
            pedido.AllowAutoRedirect = false;
            pedido.KeepAlive = false;
            pedido.Timeout = 5000;
            pedido.ReadWriteTimeout = 5000;
            pedido.ContentType = "application/json; charset=utf-8";
            pedido.Accept = "application/json";
            pedido.Headers[HttpRequestHeader.Authorization] = "Bearer " + token.Trim();
            pedido.ContentLength = corpo.Length;

            iniciouEnvio = true;
            try
            {
                using (Stream stream = pedido.GetRequestStream())
                    stream.Write(corpo, 0, corpo.Length);

                using (HttpWebResponse resposta = (HttpWebResponse)pedido.GetResponse())
                    return LerResposta(resposta, transporte, operacao);
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse resposta)
                {
                    using (resposta)
                        return LerResposta(resposta, transporte, operacao);
                }
                if (ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.NameResolutionFailure)
                {
                    return new Resposta("NAO_ENVIADO", "Ponte indisponível. Confira o Minecraft, o mod e a porta.");
                }

                return new Resposta("INCERTO", "Comunicação interrompida; confira a operação antes de repetir.");
            }
        }
        catch (Exception ex)
        {
            LogError("Transporte " + transporte + ": " + ex.Message);
            return new Resposta(iniciouEnvio ? "INCERTO" : "NAO_ENVIADO", "Falha ao comunicar com a ponte; confira a configuração e o registro.");
        }
    }

    private string EstadoRegistro(Resposta resposta, bool exigeRecibo)
    {
        if (resposta.Recibo && (resposta.Estado == "EXECUTADO" || resposta.Estado == "PARCIAL" || resposta.Estado == "FALHOU"))
        {
            return resposta.Estado;
        }

        if (resposta.Recibo && (resposta.Estado == "PENDENTE" || resposta.Estado == "EXECUTANDO"))
        {
            return "PENDENTE";
        }

        if (!exigeRecibo && (resposta.Estado == "NAO_ENVIADO" || resposta.Estado == "RECUSADO" || resposta.Estado == "COOLDOWN"))
        {
            return "FALHOU";
        }

        return "INCERTO";
    }

    private bool ResponderChat(Resposta resposta, bool sucesso, string usuario, string nome, long custo = 0, string motivo = "")
    {
        bool resultado = Publicar(resposta, sucesso);

        try
        {
            EnviarMensagem(MontarRespostaChat(resposta, usuario, nome, custo, motivo), null, somenteNaTransmissao: false);
        }
        catch (Exception ex)
        {
            LogError("Falha ao responder no chat: " + ex.Message);
            return false;
        }
        return resultado;
    }

    private static string MontarRespostaChat(Resposta resposta, string usuario, string nome, long custo, string motivo)
    {
        // Separa a resposta ao espectador dos detalhes preservados em Mensagem e no log.
        string destinatario = string.IsNullOrWhiteSpace(usuario) ? "" : "@" + usuario.TrimStart('@') + ", ";
        string preco = custo > 0 ? " Custo: " + custo.ToString("N0", Cultura) + " moedas do canal." : "";
        if (motivo == "USO")
        {
            return "Use !minecraft resgatar <nome da recompensa>.";
        }
        if (motivo == "SALDO")
        {
            return destinatario + "saldo insuficiente para " + nome + "." + preco;
        }
        if (motivo == "CONFERIR" || resposta.Estado == "INCERTO" || resposta.Estado == "PARCIAL" || resposta.Estado == "RESERVADO" || resposta.Estado == "CONFLITO")
        {
            return destinatario + "seu resgate precisa de conferência. Aguarde antes de repetir.";
        }
        if (motivo == "INDISPONIVEL")
        {
            return destinatario + "os resgates de Minecraft estão indisponíveis no momento.";
        }
        if (resposta.Recibo && (resposta.Estado == "EXECUTADO" || resposta.Estado == "PENDENTE" || resposta.Estado == "EXECUTANDO"))
        {
            return destinatario + "resgate de " + nome + " confirmado!" + preco;
        }
        if (resposta.Estado == "COOLDOWN")
        {
            var tempo = System.Text.RegularExpressions.Regex.Match(resposta.Mensagem ?? "", @"Tente novamente em ([0-9]+ (?:s|min)(?: e [0-9]+ s)?)\.$");
            if (tempo.Success)
            {
                return destinatario + nome + " disponível em " + tempo.Groups[1].Value + ".";
            }
            return destinatario + nome + " está indisponível para novo resgate. Aguarde.";
        }
        if (resposta.Estado == "RECUSADO" && resposta.Mensagem == "Resgate recusado: Nenhum resgate ativo com esse nome.")
        {
            return destinatario + "recompensa não encontrada.";
        }
        return destinatario + "os resgates de Minecraft estão indisponíveis no momento.";
    }

    private string MontarLogResgate(string resultado, string userId, string userName, string regraId, long custo)
    {
        CPH.TryGetArg("minecraftLogUsuarioEncontrado", out bool encontrado);
        CPH.TryGetArg("minecraftLogCoinBalance", out long saldo);
        CPH.TryGetArg("minecraftLogResgateId", out long resgateId);

        string saldoTexto = "indisponível (usuário não encontrado no canal)";
        if (encontrado)
        {
            userId = LerTexto("minecraftLogUserId");
            userName = LerTexto("minecraftLogUserName");
            saldoTexto = saldo.ToString("N0", Cultura);
        }

        return "Resgate do Minecraft " + resultado + ": Usuário '" + userName + "' (" + userId
            + ") | Regra Id '" + regraId + "' | Origem 'moedas' | Valor " + custo.ToString("N0", Cultura)
            + " | Saldo " + saldoTexto + (resgateId > 0 ? " | Resgate #" + resgateId.ToString(Cultura) : "") + ".";
    }

    private bool Publicar(Resposta resposta, bool sucesso, bool publicarArgumentos = true)
    {
        if (publicarArgumentos)
        {
            CPH.SetArgument("minecraftAceito", resposta.Aceito);
            CPH.SetArgument("minecraftResultado", resposta.Estado == "EXECUTADO");
            CPH.SetArgument("minecraftStatus", resposta.Estado);
            CPH.SetArgument("minecraftMensagem", resposta.Mensagem);
        }

        if (sucesso)
        {
            LogInfo((resposta.Log ?? (resposta.Estado + ": " + resposta.Mensagem)));
        }
        else
        {
            LogWarn((resposta.Log ?? (resposta.Estado + ": " + resposta.Mensagem)));
        }

        return sucesso;
    }

    private static string ValidarTexto(string texto, string nome, int limite)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Length > limite)
        {
            throw new ArgumentException(nome + " ausente ou fora do limite.");
        }

        foreach (char caractere in texto)
            if (char.IsControl(caractere) || caractere == '\u2028' || caractere == '\u2029')
            {
                throw new ArgumentException(nome + " contém caracteres de controle.");
            }

        return texto;
    }

    private static string ValidarId(string id)
    {
        if (!Guid.TryParseExact(id, "D", out Guid valor))
        {
            throw new ArgumentException("ID da operação inválido.");
        }

        return valor.ToString("D");
    }

    private static string IdentificarResgate(string canal, string usuario, string mensagem)
    {
        byte[] hash;
        using (SHA256 sha = SHA256.Create())
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new[] { "minecraft", canal, usuario, mensagem })));
        byte[] bytes = new byte[16];
        Array.Copy(hash, bytes, bytes.Length);
        return new Guid(bytes).ToString("D");
    }

    private static string Citar(string texto)
    {
        return "\"" + texto.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
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

    private void LogWarn(string mensagem)
    {
        CPH.LogWarn(Prefixo + mensagem);
    }

    private void LogInfo(string mensagem)
    {
        CPH.LogInfo(Prefixo + mensagem);
    }

    private long ConsultarSaldoComTentativas(string userId, string canalId)
    {
        // Falha técnica permite nova consulta; saldo consultado, inclusive zero, encerra as tentativas.
        for (int tentativa = 1; tentativa <= LimiteTentativasRegistro; tentativa++)
        {
            CPH.SetArgument("consultarChave", userId);
            CPH.SetArgument("consultarPorId", true);
            CPH.SetArgument("consultarCanalId", canalId);
            CPH.SetArgument("consultarIncluirRank", false);
            CPH.SetArgument("consultarValidarMinecraft", true);
            CPH.SetArgument("consultarEncontrado", false);
            CPH.SetArgument("consultarMoedas", 0L);

            try
            {
                if (CPH.ExecuteMethod(GerenteBanco, "SaldoMoedasUsuario") && CPH.TryGetArg("consultarMoedas", out long saldo))
                {
                    return saldo;
                }
            }
            catch (Exception ex)
            {
                LogError("Falha na consulta de saldo: " + ex.Message);
            }
            if (tentativa < LimiteTentativasRegistro)
            {
                System.Threading.Thread.Sleep(IntervaloTentativasConsultaMs);
            }
        }
        throw new InvalidOperationException("Consulta de saldo falhou após três tentativas; nenhum pedido enviado ao jogo.");
    }

    private Resposta ConsultarReciboComTentativas(string mundo, string id)
    {
        // Recupera o recibo da mesma operação sem reenviar a recompensa ou sua confirmação.
        Resposta resposta = new Resposta("INCERTO", "Aceite não confirmado.");
        for (int tentativa = 1; tentativa <= LimiteTentativasRegistro; tentativa++)
        {
            resposta = EnviarEtapa("status", mundo, id);
            if (resposta.Recibo || resposta.Estado == "NAO_ENVIADO" || resposta.Estado == "RECUSADO")
            {
                return resposta;
            }
            if (tentativa < LimiteTentativasRegistro)
            {
                System.Threading.Thread.Sleep(IntervaloTentativasConsultaMs);
            }
        }
        return resposta;
    }

    private string ConsultarCobrancaComTentativas()
    {
        // Somente leitura: nunca repete o débito, inclusive quando o registro está ausente.
        for (int tentativa = 1; tentativa <= LimiteTentativasRegistro; tentativa++)
        {
            string resultado = OperarMoedas("CONSULTAR_COBRANCA");
            if (resultado != "INCERTO")
            {
                return resultado;
            }
            if (tentativa < LimiteTentativasRegistro)
            {
                System.Threading.Thread.Sleep(IntervaloTentativasConsultaMs);
            }
        }
        return "INCERTA";
    }

    private static string Texto(JObject objeto, string campo)
    {
        if (objeto[campo]?.Type != JTokenType.String)
        {
            throw new ArgumentException("Campo de texto inválido: " + campo + ".");
        }

        return (string)objeto[campo];
    }

    private string MundoConfigurado()
    {
        return ValidarTexto(CPH.GetGlobalVar<string>("minecraftYoutubeBridgeWorldName", true), "Mundo autorizado", 256);
    }

    private bool BancoConfigurado()
    {
        return !string.IsNullOrWhiteSpace(CPH.GetGlobalVar<string>("caminhoPastaStreamerBot", true));
    }

    private Resposta LerResposta(HttpWebResponse resposta, string transporte, string operacao)
    {
        int http = (int)resposta.StatusCode;
        // Estas recusas ocorrem antes de executar; timeout, conflito e 5xx permanecem incertos.
        if (http == 401)
        {
            return new Resposta("RECUSADO", "Token recusado. Confira minecraftYoutubeBridgeToken.");
        }

        if (http == 400 || http == 403 || http == 404 || http == 405 || http == 413 || http == 415)
        {
            return new Resposta("RECUSADO", "Solicitação recusada pela ponte (HTTP " + http + ").");
        }

        if (http != 200)
        {
            return new Resposta("INCERTO", "Resposta HTTP " + http + "; confira a operação antes de repetir.");
        }

        string corpo;

        using (Stream stream = resposta.GetResponseStream())
        using (StreamReader leitor = new StreamReader(stream, Encoding.UTF8))
        {
            char[] buffer = new char[16385];

            int total = 0;
            int quantidade;

            while (total < buffer.Length && (quantidade = leitor.Read(buffer, total, buffer.Length - total)) > 0)
                total += quantidade;

            if (total == buffer.Length)
            {
                throw new InvalidDataException("Resposta excede o limite.");
            }

            corpo = new string(buffer, 0, total);
        }

        JObject json = JObject.Parse(corpo);
        if (Texto(json, "id") != transporte || json["success"]?.Type != JTokenType.Boolean)
        {
            throw new InvalidDataException("Identificação da resposta inválida.");
        }

        string estado = Texto(json, "executionStatus");
        string mensagem = Texto(json, "message");

        bool aceito = json.Value<bool>("success");
        bool comRecibo = estado == "RESERVADO" || estado == "PENDENTE" || estado == "EXECUTANDO" || estado == "EXECUTADO" || estado == "PARCIAL" || estado == "FALHOU";
        if (!comRecibo && estado != "INCERTO" && estado != "RECUSADO" && estado != "COOLDOWN" && estado != "CONFLITO" && estado != "NAO_ENCONTRADO")
        {
            throw new InvalidDataException("Estado de execução desconhecido.");
        }

        Resposta resultado = new Resposta(estado, mensagem);
        if (json["commands"] is JArray comandos)
        {
            foreach (JToken comando in comandos)
                LogInfo("Comando do mod (" + operacao + "): " + comando.ToString());
        }
        if (aceito != resultado.Aceito)
        {
            throw new InvalidDataException("Estado e aceite inconsistentes.");
        }

        if (comRecibo || (estado == "INCERTO" && mensagem.StartsWith("{", StringComparison.Ordinal)))
        {
            JObject recibo = JObject.Parse(mensagem);

            string regra = Texto(recibo, "ruleId");
            if (Texto(recibo, "reservationId") != operacao || string.IsNullOrWhiteSpace(regra) || recibo["price"]?.Type != JTokenType.Integer)
            {
                throw new InvalidDataException("Recibo inválido.");
            }

            long custo = recibo.Value<long>("price");
            if (custo < 0 || custo > int.MaxValue)
            {
                throw new InvalidDataException("Preço inválido no recibo.");
            }

            resultado.PagamentoIncerto = recibo["paymentUncertain"]?.Type == JTokenType.Boolean && recibo.Value<bool>("paymentUncertain");
            resultado.Recibo = true;
            resultado.RegraId = regra;
            resultado.Custo = custo;
            resultado.Mensagem = Texto(recibo, "detail");
        }
        return resultado;
    }

    private string LerTexto(string argumento)
    {
        CPH.TryGetArg(argumento, out string valor);
        return valor ?? "";
    }

    private Contexto LerContexto()
    {
        CPH.TryGetArg("contextoJson", out string json);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<Contexto>(json);
    }

    private class RegistroContribuicao
    {
        public long DoacaoId { get; set; }

        public decimal ValorBRL { get; set; }

        public string OperacaoId { get; set; }
        public string Origem { get; set; }
        public string UsuarioId { get; set; }
        public string Usuario { get; set; }
        public string CanalId { get; set; }
        public string Canal { get; set; }
        public string Mundo { get; set; }
        public string Comando { get; set; }
    }

    private class RegistroEnvio
    {
        public string EnvioId { get; set; }

        public RegistroContribuicao Dados { get; set; }
        public Resposta Resultado { get; set; }
    }

    private class RetornoRegistro
    {
        public string OperacaoId { get; set; }
        public string ChamadaId { get; set; }
        public string Resultado { get; set; }
        public string Status { get; set; }
        public string RegraId { get; set; }
    }

    private class Resposta
    {
        public bool Recibo;
        public bool PagamentoIncerto;
        public bool Aceito => Estado == "RESERVADO" || Estado == "PENDENTE" || Estado == "EXECUTANDO" || Estado == "EXECUTADO";

        public long Custo;

        public string Estado;
        public string Mensagem;
        public string Log;
        public string RegraId = "";

        public Resposta(string estado, string mensagem)
        {
            Estado = estado;
            Mensagem = mensagem;
        }
    }

    public class Contexto
    {
        public Doacao Doacao { get; set; }
        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }

    public class Doacao
    {
        public bool MinecraftAceito { get; set; }

        public long RegistroId { get; set; }

        public decimal ValorBRL { get; set; }

        public string ProcessamentoId { get; set; }
        public string Usuario { get; set; }
        public string UsuarioId { get; set; }
        public string TriggerName { get; set; }
        public string BroadcastUserId { get; set; }
        public string BroadcastUserName { get; set; }
        public string RegistroStatus { get; set; }
        public string MinecraftStatus { get; set; }
        public string MinecraftMensagem { get; set; }

        // Preserva os resultados das outras etapas ao devolver o contexto.
        [JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, JToken> DadosComplementares { get; set; }
    }
}