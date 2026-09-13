using System;

// Atualização 260911.1045
public class CPHInline
{
    private const int DuracaoVisivelMs = 10000;

    private const string CenaSobreposicoes = "Sobreposições";
    private const string FonteInscrevaSe = "Inscreva-se";
    private const string FonteInstagram = "Instagram";
    private const string ChaveUltimaFonte = "gerenteObsUltimaFonteExibida";

    public bool AlternarExibicaoFontes()
    {
        try
        {
            string ultimaFonte = CPH.GetGlobalVar<string>(ChaveUltimaFonte, false) ?? "";
            string proximaFonte = ultimaFonte == FonteInscrevaSe ? FonteInstagram : FonteInscrevaSe;

            CPH.ObsSetSourceVisibility(CenaSobreposicoes, proximaFonte, true);
            CPH.LogInfo($"[GERENTE_DE_OBS] Fonte '{proximaFonte}' exibida.");

            System.Threading.Thread.Sleep(DuracaoVisivelMs);

            CPH.ObsSetSourceVisibility(CenaSobreposicoes, proximaFonte, false);
            CPH.LogInfo($"[GERENTE_DE_OBS] Fonte '{proximaFonte}' escondida.");

            CPH.SetGlobalVar(ChaveUltimaFonte, proximaFonte, false);

            return true;
        }
        catch (Exception ex)
        {
            CPH.LogError($"[GERENTE_DE_OBS] Erro ao exibir/esconder fonte: {ex.Message}");
            return false;
        }
    }
}