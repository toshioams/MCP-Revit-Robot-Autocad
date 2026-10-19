# Operações com chave — 0.8.65

Integração opcional inspirada no estudo de mskim274/revit-mcp-v2. O mecanismo impede uma
segunda execução da mesma chave dentro da sessão do servidor. Não é garantia de sucesso,
persistência em disco ou validação da geometria.

## Protocolo

1. Ler `GET /health` no host correto. Exigir `idempotencySupported:true` e guardar `sessionId`.
2. Criar chave `<sessionId>:<uuid-ou-identificador-unico>`. Sufixo de 1–96 caracteres ASCII
   alfanuméricos, `_` ou `-`. Guardar antes de enviar.
3. Enviar `idempotencyKey` no envelope de `/mcp` ou `/mcp/batch`. Informar
   `documentTitle` ou, preferencialmente, `documentPath` em cada comando/passo.
4. Após timeout, consultar `GET /operations?key=<chave>` com a mesma autenticação Bearer.
   `pending` significa que ainda não há resposta final. `completed` contém `data.result`.
5. Para obter novamente a resposta, reenviar exatamente a mesma operação/chave.
   A resposta inclui `operation.replayed:true`. Não trocar a chave para contornar erro.

```json
{
  "command": "create_wall",
  "idempotencyKey": "<sessionId>:parede-entrada-preview-1",
  "dryRun": true,
  "params": {
    "documentTitle": "Projeto",
    "start": {"x": 0, "y": 0, "z": 0},
    "end": {"x": 3, "y": 0, "z": 0},
    "height": 2.8,
    "levelName": "Térreo"
  }
}
```

O exemplo ilustra o envelope; conferir o esquema e os tipos/níveis reais antes de executar.
DryRun e commit são operações distintas: usar nova chave para commit após revisar a prévia.

No TypeScript, `callRevit(command, params, dryRun, key)` e
`callRevitBatch(steps, stopOnError, dryRun, key)` aceitam chave opcional. A terceira posição
continua sendo dryRun. Ferramentas MCP com forwarding simples e `revit_batch` expõem
`idempotencyKey`; receitas e callbacks específicos não recebem promessa de idempotência.
Não há nova ferramenta MCP de status nesta versão: descoberta/status usam os endpoints HTTP.

## Garantias e limites

- Reserva atômica anterior ao dispatch: chamadas simultâneas iguais aguardam a mesma tarefa.
- Fingerprint inclui comando, parâmetros e dryRun efetivo. Ordenação de propriedades JSON
  não importa, mas outros detalhes (defaults omitidos, rota alternativa de batch etc.) podem
  resultar em conflito. Repetir o envelope original.
- Mesmo identificador com conteúdo diferente retorna `idempotency_conflict`, HTTP 409.
- Nova sessão gera novo prefixo. Uma chave anterior retorna `session_mismatch`, sem execução.
  O estado antigo não sobrevive a reinício: reconciliar o modelo antes de criar outra chave.
- Capacidade: 1.024 chaves por instância de servidor, sem TTL/evicção. Ao atingir o limite,
  novas chaves são recusadas; existentes continuam disponíveis. Não há limpeza automática.
- Resultado acima de 64 KiB vira resumo `operation_result_too_large`, conservando `committed`
  quando conhecido. A operação não é repetida. Conferir o modelo para recuperar seus detalhes.
- `completed` indica término do dispatch, não sucesso nem commit. Examinar `ok`, `committed`,
  `dryRun`, falhas e verificar o modelo. Resultado em cache é histórico: não reflete Undo nem
  alterações posteriores.
- Chamada sem chave mantém o comportamento anterior. Não há retry automático de escrita.
- Clientes TS recusam envio com chave a hosts que não anunciam suporte. HTTP direto deve fazer
  essa conferência: hosts antigos podem ignorar campos desconhecidos.
- Documento explícito evita depender apenas da aba ativa, mas ainda usa nosso resolver por
  título/caminho. Identidade estável de abertura/reabertura do documento não foi implementada.
- Endpoints de status exigem autenticação quando o host está configurado com token.

## Validação

### Ao vivo — 15/09/2026

Revit estava fechado quando o usuário autorizou. A build foi ativada em pasta nova
`RevitMCP-local-20260915-102619`. Health confirmou 0.8.65 e suporte. Uma cópia independente
de Desktop/teste.rvt foi aberta em `release/live-idempotency-20260915/`.

Passaram no Revit:

- Criação de nível em dryRun e replay, ambos com `committed:false`.
- Recusa de reaproveitar chave de prévia para commit.
- Duas chamadas simultâneas: um único nível persistido, mesmo ID nas respostas e um replay.
- Consulta autenticada de operação concluída; chave com sessão incompatível recusada.
- Operação sem documento explícito recusada antes da criação.
- Batch em dryRun e batch com commit de dois níveis; resposta original descartada pelo
  cliente, resultado recuperado por status e reenvio sem novos níveis.
- Cliente TypeScript real com chave, no alias `POST /mcp command=batch`, em prévia/replay.
- Leitura posterior confirmou exatamente três níveis de teste. UniqueIds foram registrados.
- Limpeza em dryRun mostrou somente os três níveis, sem dependentes extras; exclusão
  confirmada e inventário inicial restaurado. Cópia salva, `isModified:false`.

Evidência completa local: `release/live-idempotency-20260915/evidence.json`.
Roteiro: `scripts/test-idempotency-live.mjs` (só aceita o caminho explícito da cópia).
A limpeza foi feita separadamente, após inspeção da prévia registrada pelo roteiro.

Limites: descarte de resposta reproduz recuperação sem o cliente conhecer o resultado;
não foi provocada queda real da rede ou do Revit durante o commit. A rejeição de outra
sessão foi exercitada com prefixo incompatível, sem um segundo reinício. Testes de pressão,
retenção máxima e concorrência prolongada continuam cobertos apenas parcialmente/unidades.
Não foi verificado o recarregamento dos schemas no cliente MCP desta conversa.

### Build e testes antes da ativação

15/09/2026: 230 testes C# (6 novos casos de registro/concorrência), 47 testes Node
(3 novos casos de compatibilidade/transporte), compilação TypeScript, build Release C#
sem avisos/erros e check-version passaram. Staging em
`release/staging-0.8.65-20260915-102301`, 14 arquivos com hashes conferidos. Nenhuma alteração
no manifesto ativo ou nos binários carregados. A restauração inicial dos testes falhou
porque não havia fonte NuGet configurada; uma restauração com fonte explícita resolveu.

Roteiro de validação para futuras builds: confirmar versão e suporte no health, usar documento de
teste, fazer prévia com chave, repetir, conferir um único dispatch e `committed:false`;
fazer uma edição autorizada com nova chave, simular perda de resposta e conferir uma única
edição. Testar batch, conflito, documento diferente e chave de sessão anterior. Compilação
e testes unitários não substituem essa validação.
