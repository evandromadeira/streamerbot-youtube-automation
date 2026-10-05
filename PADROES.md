# Padrões de programação

Documento vivo dos padrões do projeto youtube-automation. Consultar antes de criar ou editar qualquer script C# do projeto, inclusive scripts de manutenção. Novos padrões podem ser acrescentados conforme forem definidos pelo usuário.

## Aplicação

- Aplicar os padrões ao código criado ou alterado. Não fazer reformatação ampla de outros trechos sem necessidade ou solicitação.
- Preservar comportamento, dependências entre declarações e contratos entre Actions ao organizar o código.
- Cada arquivo C# é um script independente do Streamer.bot; não pressupor classes compartilhadas entre arquivos.
- Preferências de organização são deliberadas e devem ser respeitadas mesmo quando não afetam a execução.

## Estrutura, Evento e Ambiente

- Manter as classes auxiliares no fim de CPHInline, depois dos métodos de processamento. Quando presentes, deixar Evento e Ambiente nessa região final, com Evento antes de Ambiente.
- Evento reúne os dados de entrada do evento. Ambiente reúne configurações e caminhos necessários ao script.
- Usar nomes explícitos para instanciar e referenciar essas classes:

```csharp
Evento evento = new Evento(CPH);
Ambiente ambiente = new Ambiente(CPH);
```

- Quando o script recebe um contexto já preparado, usar os objetos desse contexto, conforme o contrato existente:

```csharp
Evento evento = contexto.Evento;
Ambiente ambiente = contexto.Ambiente;
```

- A Action que recebe a trigger prepara os dados para os métodos chamados. O destinatário deve consumir o contrato recebido, sem voltar a depender dos argumentos nativos da trigger.
- Manter em cada classe apenas as propriedades necessárias ao script. Não copiar propriedades sem uso nem criar Evento/Ambiente quando não forem necessários.
- Resolver caminhos em Ambiente a partir das variáveis globais existentes, usando Path.Combine. Não fixar caminhos específicos do PC.
- Em classes usadas na desserialização, preservar os construtores e propriedades exigidos pelo contrato e pelo serializador adotado.

## Variáveis e propriedades

- Quando possível, agrupar declarações do mesmo tipo, uma por linha, e separar tipos diferentes com uma linha vazia. Aplicar também às propriedades de Evento.
- Exceção: chamadas consecutivas de CPH que pertencem ao mesmo bloco lógico ficam juntas, sem linha vazia entre elas, independentemente do método chamado ou do tipo das variáveis envolvidas. Isso vale para todos os métodos CPH, não apenas TryGetArg. Usar linhas vazias para separar etapas lógicas distintas, não apenas métodos ou tipos diferentes.
- Preferir a ordem int, double, string entre esses tipos. A posição dos demais tipos ainda não foi definida; mantê-los agrupados de forma coerente com o arquivo.
- Dependências entre inicializações e escopo têm prioridade sobre a ordem visual. Não mover declarações se isso alterar o comportamento.

```csharp
int quantidade;
int pontos;

double valor;
double taxa;

string usuario;
string message;
```

## Formatação e comentários

- Usar indentação de quatro espaços e chaves em linhas próprias, seguindo o estilo dos scripts.
- Quando uma variável ou objeto é obtido, criado ou preenchido e validado em seguida, manter a validação imediatamente abaixo, sem linha vazia. Vale para contexto, evento, argumentos e outros valores; obtenção e validação formam um mesmo bloco lógico.
- Terminar cada script sem quebra de linha após a última chave de fechamento: o último caractere do arquivo deve ser }. Não deixar linhas vazias no fim.
- Nunca colocar a condição if e sua instrução na mesma linha. A mesma regra vale para else e else if, inclusive return, throw e chamadas de métodos.
- Uma instrução simples pode ficar sem chaves, mas sempre na linha seguinte. Blocos com múltiplas instruções usam chaves.

```csharp
if (sucesso)
    RegistrarSucesso();
else
    RegistrarFalha();
```

- Manter cada chamada CPH.SendYouTubeMessage em uma única linha física do código. Não inserir quebras de linha no texto enviado; mensagens separadas usam chamadas separadas.
- Manter comentários claros, em português, explicando finalidade, regras ou motivos que ajudam a compreender o código.
- Preservar os separadores e a organização de comentários já adotados no arquivo. Não inventar um novo estilo a cada edição.
- Preservar os comentários de configuração de triggers e Name quando existirem; atualizar quando o contrato realmente mudar.

- Separar com uma linha vazia a preparação de argumentos via CPH.SetArgument da chamada CPH.ExecuteMethod, inclusive quando a chamada estiver dentro de um if. Preparar os dados e executar outro método são etapas distintas; isso não é a validação imediata de um valor recém-obtido.

## Data e hora de atualização

- Sempre que alterar um script, atualizar o comentário do topo: // Atualização AAMMDD.HHmm.
- Usar a data e a hora locais de America/Sao_Paulo, nunca a hora UTC ou o fuso incidental do ambiente de execução.
- Arredondar para cima ao próximo horário de cinco minutos. Se já estiver exatamente no horário de cinco minutos, mantê-lo. Considerar segundos e a passagem de hora ou dia.
- Exemplos: 03/10/2026 10:08 vira // Atualização 261003.1010; 10:10:01 vira 261003.1015; 23:58 vira a data do dia seguinte com 0000.

## Comunicação entre scripts — decisão pendente

- A preferência proposta é serializar e desserializar JSON para passar informações entre scripts.
- Ainda será discutido se esse será o padrão geral ou se todos adotarão argumentos individuais, como o Gerente de Banco de Dados.
- Até essa decisão, preservar os contratos atuais: JSON onde já é usado e argumentos individuais onde já são usados. Não migrar a comunicação apenas por causa deste documento.
- O Gerente de Banco de Dados mantém seu contrato atual de argumentos, sem migração para JSON.
- Qualquer mudança de contrato deve atualizar remetente e destinatário juntos. Resultados das operações retornam pelo contrato combinado; não confundir retorno técnico da chamada com confirmação da operação.

## Logs

- Cada script deve ter uma assinatura própria e consistente em todos os seus logs, independentemente do nível.
- Preservar a assinatura existente. Para novos scripts, definir uma assinatura que identifique claramente seu responsável.
- No Gerente de Banco de Dados, usar o prefixo completo >>> [GERENTE_DB] antes do texto em todos os logs:

```csharp
CPH.LogError(">>> [GERENTE_DB] Descrição do erro.");
```

- Não alternar assinaturas dentro do mesmo script.

## Organização do Gerente de Banco de Dados

- Sempre que possível, manter consecutivos os métodos e classes relacionados ao mesmo script consumidor ou funcionalidade.
- Preservar uma ordem coerente dentro de cada grupo e deixar os auxiliares compartilhados em uma seção própria.
- Não duplicar auxiliares compartilhados nem mudar contratos apenas para aproximar trechos no arquivo.

## Conferência antes de concluir

- Conferir estrutura de Evento/Ambiente, agrupamento de tipos, if/else, mensagens e assinaturas dos logs.
- Atualizar o comentário de data/hora em cada script modificado.
- Se houver mudança na comunicação, conferir os dois lados do contrato.
- Distinguir revisão de código e testes simulados de teste real no Streamer.bot.
