using System;
using System.IO;
using Newtonsoft.Json;

// Versão 260718.1323

public class CPHInline
{
	public bool Execute()
	{
        CPH.RunAction("Reset First Words", false); // Ativa a ação de resetar a primeira mensagem
		CPH.RunAction("Youtube Pontos Surpresa", false); // Ativa a ação dos pontos surpresa
        
        CPH.SendYouTubeMessage("Todas ações de início de live concluídas com sucesso!");
        
        return true;
	}
}