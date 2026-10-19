# Sessão 01 — WC PCD modelado do zero pelo MCP

Registro da primeira sessão de modelagem guiada: Lucas dita, o agente executa via MCP,
e cada travamento vira especificação. Não é ata — é o que o Revit realmente gravou.

- **Data**: 08/09/2026
- **Ambiente**: Revit 2026, addin `0.8.34` (commit `af98c1c`), documento `Projeto1`
  criado a partir do template do escritório
- **Briefing**: banheiro PCD típico, bacia + chuveiro + acessórios, pé-direito 2,40,
  vedação em divisória (decisão do Lucas)

## 1. Partido adotado

Compartimento interno **1,90 × 2,60 m** (4,94 m²). Subiu em relação à proposta inicial de
1,80 × 2,40 porque o box de chuveiro (0,90 × 0,95) e a área de transferência da bacia
(0,80 × 1,20) não convivem na área menor sem se sobrepor.

Origem local (0,0) = canto interno sudoeste.

| Elemento | Posição | Id |
|---|---|---|
| Divisória sul | y = −0,015, x −0,015→1,915 | 19386647 |
| Divisória leste | x = 1,915 | 19386648 |
| Divisória norte | y = 2,615 | 19386649 |
| Divisória oeste | x = −0,015 | 19386650 |
| Laje `.ESTRUTURA_Laje 10cm` | nível `N1-ES` (−0,05) | 19386653 |
| Piso `.ACABAMENTO_Porcelanato 80x80 cm` | nível `N1-AC` (0,00) | 19386661 |
| Porta `P14 - Porta interna PNE 0.90x2.10` | divisória sul, eixo x = 0,55, abre para fora | 19386673 |
| Forro `.GESSO_Placa de gesso comum (ST)` | deslocamento 2,40 | 19386677 |
| Bacia `Deca LK_P.230_Caixa Acoplada` | costas em y = 2,60, eixo x = 0,40 | 19386686 |
| Lavatório `Lavatório de Canto com Barras` | face oeste, y 0,70→1,20 | 19386692 |
| Ralo `Ralo linear 70-90cm - Tigre` | x 1,05→1,76, y ≈ 0,12 | 19386690 |
| Chuveiro `Deca_Chuveiro Quadrado` | face leste, **ainda no piso — ver lacuna 1** | 19386693 |
| Ambiente `WC PCD 01` | centro (0,95; 1,30) | 19386691 |

## 2. Convenções do escritório confirmadas na prática

- **`N1-ES` / `N1-AC` = estrutura / acabamento.** O template repete a dupla do Ginásio
  (−0,05 e 0,00). Laje vai na ES, revestimento na AC. Isso não estava escrito em lugar
  nenhum — foi deduzido do par de níveis e confirmado ao modelar.
- **Divisória de granito é tipo de parede** (`DIVISÓRIA GRANITO`, 3 cm), não família.
- **Fase**: elementos nascem em "Construção nova" (86961) mesmo com uma vista de fase
  "Existente" ativa. Comportamento correto, mas ver lacuna 8.
- **Vistas**: `N1-AC` é planta de **forro**; a planta baixa arquitetônica é
  `PLANTA BAIXA TÉCNICA`. Nome de vista não indica tipo — outra confirmação da regra
  "nunca decidir por nome".

## 3. Lacunas encontradas

Ordenadas por severidade. As três primeiras bloqueiam autonomia de verdade.

### 1. `place_family_instance` descarta `location.z` em silêncio — CRÍTICO
Chamei com `z: 2.1` para o chuveiro. Resposta `ok: true`, `placed: true`. A peça foi para
o piso (bbox z 0,00–0,55). Repeti **com `hostId`** da parede: mesmo resultado.
**Por que é crítico**: o comando reporta sucesso e o modelo fica errado. Um agente
autônomo não tem como saber que falhou sem medir tudo depois. Esta é a pior classe de
falha que existe para automação.
*Correção*: aplicar o z, ou rejeitar com erro tipado quando a família não aceitar.

### 2. `move_element` descarta `translation.z` — CRÍTICO
Contorno tentado para a lacuna 1: mover +2,10 em z. A resposta trouxe
`translationFeet.z: 6.89` e `changeSummary` com o vetor completo — mas o `changes`
before/after mostra o z inalterado, e o bbox confirma: não subiu.
**Mesma classe da lacuna 1**: relata o que não fez.

### 3. Impossível saber o ponto de inserção e a orientação de uma família antes de colocar
Três das quatro peças entraram fora do lugar: bacia atravessando a divisória norte,
ralo 26 cm para fora, chuveiro furando a parede. O ponto de inserção da bacia fica nas
**costas** e o corpo cresce em +Y — descobri isso medindo o bbox depois de errar.
*Correção*: `get_family_placement_info` (ponto de inserção relativo ao bbox do tipo,
direção de facing/hand, se é hospedada e em quê) **e** parâmetro `rotation` no
`place_family_instance` — hoje é obrigatório um `rotate_element` depois, em segunda
transação.

### 4. Sem comando para listar tipos de família de sistema
`list_family_types` com `OST_Ceilings` volta vazio. Forro, telhado e piso são famílias de
sistema; só piso e parede têm comando dedicado (`list_floor_types`, `list_wall_types`).
Tive que adivinhar `.GESSO_Placa de gesso comum (ST)` pelo que vi no Ginásio.
*Correção*: `list_system_types(category)` genérico.

### 5. `create_ceiling` não aceita altura
O forro nasce com o deslocamento padrão do Revit (2,60) e exige um `set_parameter`
depois para virar 2,40. Duas transações para uma operação só.

### 6. `get_element_info` — unidades e chaves (já previsto no plano F0)
Confirmado de novo em produção: bbox em **pés** sem rótulo, `Nível = 3711007` (id cru em
vez de nome), e chaves repetidas (`Categoria` 2×, `Nível` 2×).

### 7. Erro enganoso quando o cliente não manda UTF-8
`set_parameter` com `"Altura do deslocamento do nível"` vindo de um shell em Latin-1
respondeu `not_found: elemento não tem parâmetro X`. O parâmetro existe; o problema era
encoding. Pelo MCP (JSON UTF-8 correto) funcionou de primeira.
*Correção*: detectar bytes inválidos e responder `bad_encoding`, não `not_found`.
**Não é bug do addin** — mas a mensagem manda o agente caçar o problema errado.

### 8. Criação sem aviso de visibilidade
Elementos criados em "Construção nova" não aparecem na vista ativa de fase "Existente".
O usuário olha a tela e não vê nada, sem nenhum aviso.
*Correção*: quando a fase do elemento criado não for visível na vista ativa, devolver
`warning` no envelope dizendo em qual vista olhar.

### 9. Sem acesso à biblioteca aberta em outro documento
`load_family` só aceita `.rfa` em disco. O escritório trabalha com o arquivo de
biblioteca **aberto ao lado**, e o MCP não enxerga outro documento.
*Correção*: `list_open_documents` + `copy_elements_between_documents` /
`load_family_from_open_document`.

### 10. Louça PCD catalogada em categoria errada (achado de padrão, não do MCP)
`Bacia Sanitária com Barras` está em **Mobiliário**, não em Peças hidrossanitárias — ficaria
fora da tabela `QUANTITATIVO DE LOUÇAS E METAIS`. Por isso usei a bacia Deca na categoria
certa. É exatamente o tipo de desvio que o `audit_standard` (F1) tem que pegar.

## 4. O que funcionou de primeira

Vale registrar, porque define a base confiável: `create_wall` (4×), `create_floor` (2×),
`create_ceiling`, `create_room`, `place_family_instance` **hospedado** para a porta (cortou
o vão sozinho), `set_parameter` com conversão de unidade, `rotate_element`, `move_element`
em XY, `zoom_to_elements`, `get_view_image`, `open_view`.

## 5. Pendências desta sessão

- **Biblioteca**: faltam barras de apoio soltas, papeleira, banco articulado de chuveiro,
  alarme de emergência e piso tátil. Bloqueado pela lacuna 9 até termos o caminho dos `.rfa`.
- **Chuveiro na altura certa**: bloqueado pelas lacunas 1 e 2.
- **Conferência NBR 9050**: rodar `spatial_clearance_envelope` e `check_clearance` sobre o
  círculo de giro e a área de transferência. Ainda não executado.
- **Detalhamento**: cortes, cotas e prancha do banheiro — próximo bloco.

## 6. Efeito no plano

As lacunas 1, 2 e 3 mudam a prioridade do [plano](PLANO_MODELAGEM_DETALHAMENTO.md):
antes de qualquer comando novo de modelagem (F2), **o que existe precisa parar de mentir**.
Um comando que responde `ok` sem ter feito o que foi pedido inviabiliza a faixa A inteira,
porque toda receita serial confia no retorno do passo anterior.

Proposta de reordenação: **F0.5 — honestidade dos comandos de escrita**, antes da F1.
Cobre as lacunas 1, 2, 5, 8 e a política geral: nenhum comando devolve `ok` para um
parâmetro que ignorou.
