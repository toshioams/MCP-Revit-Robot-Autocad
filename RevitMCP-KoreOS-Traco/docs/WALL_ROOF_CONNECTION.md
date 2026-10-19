# Encontro entre paredes e cobertura

Ferramentas: `attach_wall_top` e `create_profile_wall` (0.8.40, carregadas em 11/09);
`detach_wall_top`, `analyze_wall_roof_interface` e correção de `create_profile_wall`
(0.8.41, preparadas em 11/09, NÃO carregadas/validadas no Revit).

## Regra de decisão do agente

1. Conferir documento, identidade das paredes/telhado, geometria atual e cortes.
2. Rodar `analyze_wall_roof_interface` (0.8.41) em cada parede sob a cobertura:
   intradorso nas DUAS faces, diferença através da espessura (cunha de beiral),
   trechos sem cobertura e perfil convexo proposto.
3. Preferir `attach_wall_top` quando a parede deve subir até a cobertura e o
   vínculo nativo é adequado. Não confundir anexação com apoio estrutural.
4. Se a anexação não for possível ou for inadequada, criar parede independente
   acima da existente usando `create_profile_wall` com o perfil do passo 2.
   Preservar a parede inferior e seus vãos. Não repetir anexação sem nova evidência.
5. Não usar cumeeira externa, bounding box, níveis ou caixa de escopo como limite.
   Descontar apoios e folgas definidos no projeto (parâmetro `clearance`).
6. dryRun e execução com o dryRun NO ENVELOPE (ver incidente abaixo), leitura
   posterior, corte/3D, avisos, frestas. DryRun não substitui conferência após commit.
7. Registrar IDs/UniqueIds e estratégia. Ao alterar a cobertura, revisar oitões
   independentes: eles não acompanham o telhado. Anexações nativas acompanham.

## Validação ao vivo — 11/09/2026, Revit 0.8.40 / 109 comandos

### attach_wall_top — VALIDADA após commit (nas 4 paredes externas)

Telhado 19401056 (UniqueId ea0f9cd5-...-01280960): footprint -.6..6.6 x -.6..8.6,
intradorso 2.20 na borda, 45°, z = x+2.8 (água oeste) e z = 8.8-x (água leste).

| Parede | UniqueId (sufixo) | Antes | Depois |
|---|---|---|---|
| 19389460 sul (oitão) | ...-0127dc14 | topo 2.80 | oitão até 5.80; faces superiores coplanares ao intradorso (área .849 m² cada = 3√2 x .20) |
| 19389461 leste (beiral) | ...-0127dc15 | topo 2.80; cunha 20x20 cm aberta na face interna | 2.80 face externa / 3.00 face interna; face superior inclinada normal (.707,0,.707), área 2.206 m² |
| 19389462 norte (oitão) | ...-0127dc16 | topo 2.80 | oitão até 5.80 |
| 19389463 oeste (beiral) | ...-0127dc17 | topo 2.80 | 2.80 / 3.00, cunha fechada |

Conferido: `spatial_get_walls`, `inspect_planar_faces`, bounding boxes, parâmetro
"O topo está anexado = Sim", cortes 19401073/19401082, {3D} e elevações exteriores
1 e 3. Avisos permaneceram 34. Porta 19389512 e janelas 19389574/82/84/86 seguem
hospedadas nas mesmas paredes. Paredes do banheiro 19389464/65 não foram alteradas.

### Incidente: anexação gravada quando se pretendia dryRun

O agente chamou o HTTP direto com `{command, params:{dryRun:true}}`. O servidor só
lia `dryRun` na query ou no envelope; o parâmetro foi ignorado e as 4 anexações
foram GRAVADAS (resposta `committed:true`). Não havia ferramenta para desanexar.
Decisão: manter, pois é a estratégia preferida pela regra do usuário e a geometria
foi conferida; mas NÃO houve decisão prévia deliberada para os oitões. Se
arquiteta/engenheiro preferirem oitão independente sobre cinta de amarração a 2.80,
usar `detach_wall_top` (0.8.41) nas paredes 19389460/62 e depois `create_profile_wall`.
Ctrl+Z no Revit também pode estar disponível enquanto a sessão não for fechada.

Mesma classe de erro encontrada no MCP: `revit_create_floor_type_variant` e
`revit_set_family_orientation` declaravam dryRun mas usavam `fwd`, que enviava o
flag em params → um dryRun por essas ferramentas gravaria. Corrigido na 0.8.41 nas
duas camadas (servidor honra params/steps; `fwd` separa o flag). Testes C# cobrem.

### create_profile_wall 0.8.40 — FALHA real

dryRun (flag correto) falhou com `verification_failed` para triângulo 2.8..5.8,
retângulo 2.8..5.8, retângulo 0..3 e triângulo 0..3, com 5 tipos de parede básica.
Rejeição de perfil côncavo funcionou (`unsupported_profile`). A conferência usava
`get_BoundingBox`, que não representa a extensão de parede por perfil recém-criada
(hipótese mais provável; valores não eram retornados). 0.8.41 confere pela geometria
sólida (tolerância 1e-4 ft) e devolve Z do sólido e da bbox na falha. Revalidar:
dryRun isolado em y=-15 m; depois commit isolado, leitura e remoção; só então
usar em oitão real. Também confirmar se o perfil fica no plano central da parede.

## Contratos

`create_profile_wall`: `profile` 3..100 pontos x/y/z absolutos (sem repetir o
primeiro), `wallTypeId` (básica), `levelName`, `structural`/`flip` opcionais,
`units`, `dryRun`. Vertical, plano, estritamente convexo, segmentos retos.

`analyze_wall_roof_interface` (somente leitura): `wallId`, `roofId`,
`clearance` (>=0, padrão 0), `baseElevation` (padrão = topo do sólido da parede;
sem sentido depois de anexada), `units`. Parede reta; faces planas do telhado
com normal para baixo. Retorna segmentos por face (interior/exterior conforme
`Wall.Orientation`, não ambientes), folgas até a base, diferença através da
espessura, trechos descobertos e `proposedProfile` no plano central, com
`proposedProfileProblem` = roof_does_not_cover_span / underside_not_above_base /
profile_splits_into_several_regions / degenerate_profile. Matemática em
`WallRoofProfileMath` com testes da geometria da cabana (oitão triangular, folga,
oitão parando na face interna da parede de beiral, cunha de beiral, parede além do
beiral, cumeeira fora do eixo, vale côncavo, furo).

`detach_wall_top`: `wallId`, `roofId`, `dryRun`. Idempotente; confirma remoção.

## Atualização 11/09 ~11h30

Decidido pelos usuários: terças apoiadas nos oitões. As anexações nativas atuais serão
desfeitas: paredes de beiral terminam em 2,80 sob o frechal; oitões viram paredes por
perfil sob o lambril com junta de 1,5 cm e aberturas para as terças. Ver ROOF_STRUCTURE.md.
A seção abaixo registra a situação antes dessa decisão.

## Pendências do encontro (antes da decisão estrutural)

- Estrutura da cobertura inexistente: o telhado genérico de 15.24 cm (sem material)
  apoia direto na alvenaria. Não há frechal, terças, cumeeira, caibros, ripas nem
  cinta de amarração. Oitão de alvenaria de 3 m de altura sem travamento.
  Nada disso está dimensionado. Quando a estrutura for modelada, o telhado deverá
  subir (ou virar só a telha), e as anexações seguirão o novo intradorso.
- Beiral lateral (oitões) de 60 cm em balanço sem terças; beiral sem forro/testeira.
- Forro: só o banheiro 19394071 (2.60, encostado nas faces das paredes, sem junta).
  Sala/quarto sem forro; decidir forro inclinado x horizontal (nível N1-FORRO 2.70).
- Após reinício: validar 0.8.41 (analyze nas 4 paredes; detach/attach idempotentes
  em dryRun; profile wall isolado).
