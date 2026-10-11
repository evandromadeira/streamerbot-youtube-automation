using System;
using System.Globalization;
using Newtonsoft.Json;

// Atualização 261011.2020
public class CPHInline
{
    private const string Prefixo = "[GERENTE_DE_OBS] ";

    private const int DuracaoVisivelMs = 10000;

    private const string CenaSobreposicoes = "Sobreposições";
    private const string FonteInscrevaSe = "Inscreva-se";
    private const string FonteInstagram = "Instagram";
    private const string ChaveUltimaFonte = "gerenteObsUltimaFonteExibida";

    private const string CenaContador = "Contador";
    private const string FonteContadorInscritos = "Inscritos";

    public bool AtualizarContadorInscritos()
    {
        try
        {
            CPH.TryGetArg("inscritosJson", out string inscritosJson);
            if (string.IsNullOrWhiteSpace(inscritosJson))
            {
                throw new ArgumentException("Contagem de inscritos ausente.");
            }

            ContagemInscritos contagem = JsonConvert.DeserializeObject<ContagemInscritos>(inscritosJson);
            if (contagem == null || !contagem.Total.HasValue || contagem.Total.Value < 0)
            {
                throw new ArgumentException("Contagem de inscritos inválida.");
            }

            CPH.ObsSetGdiText(CenaContador, FonteContadorInscritos, contagem.Total.Value.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")));
            return true;
        }
        catch (Exception ex)
        {
            LogError("Erro ao atualizar contador de inscritos: " + ex.Message);
            return false;
        }
    }

    public bool AlternarExibicaoFontes()
    {
        try
        {
            string ultimaFonte = CPH.GetGlobalVar<string>(ChaveUltimaFonte, false) ?? "";
            string proximaFonte = ultimaFonte == FonteInscrevaSe ? FonteInstagram : FonteInscrevaSe;

            CPH.ObsSetSourceVisibility(CenaSobreposicoes, proximaFonte, true);
            LogInfo($"Fonte '{proximaFonte}' exibida.");

            System.Threading.Thread.Sleep(DuracaoVisivelMs);

            CPH.ObsSetSourceVisibility(CenaSobreposicoes, proximaFonte, false);
            LogInfo($"Fonte '{proximaFonte}' escondida.");

            CPH.SetGlobalVar(ChaveUltimaFonte, proximaFonte, false);

            return true;
        }
        catch (Exception ex)
        {
            LogError($"Erro ao exibir/esconder fonte: {ex.Message}");
            return false;
        }
    }

    private void LogInfo(string mensagem)
    {
        CPH.LogInfo(Prefixo + mensagem);
    }

    private void LogError(string mensagem)
    {
        CPH.LogError(Prefixo + mensagem);
    }

    public class ContagemInscritos
    {
        public long? Total { get; set; }
    }
}