# Estrutura da cobertura da cabana — decisões, geometria e execução

Estado: 11/09/2026. Ferramentas na 0.8.42 (preparada, NÃO carregada). Modelo ainda sem
peças da estrutura. Nada aqui é dimensionamento estrutural.

## Decisões dos usuários (11/09)

| Tema | Decisão |
|---|---|
| Sistema | Terças apoiadas nos oitões de alvenaria: frechal nas paredes de beiral, terça intermediária e cumeeira; caibros sobre elas |
| Oitões | Seguem a estrutura: alvenaria portante até sob o lambril, perfurada pelas terças |
| Beiral | Resolvido pela estrutura: caibros em balanço no beiral (60 cm) e terças/frechal em balanço no oitão (60 cm) |
| Forro | Sem forro. Acabamento interno = lambril sobre os caibros (caibros e terças aparentes) |
| Telha | Cerâmica capa-canal |
| Junta perimetral | Lambril × alvenaria (topo dos oitões) |

Não definidos (pendentes com os usuários): espécie/classe da madeira, tratamento, bitolas
finais, cinta de amarração e chumbadores do frechal, tipo de subcobertura (manta
aluminizada?), vedação da junta (selante/tarugo), testeira e acabamento do beiral,
forro de gesso existente do banheiro 19394071 (manter ou retirar — "cabana sem forro").

## Composição (de baixo para cima)

1. Alvenaria até 2,80 m nas 4 paredes (paredes de beiral e parte inferior dos oitões).
2. Frechal 15 × 8 cm deitado no eixo das paredes de beiral (x = 0,10 e 5,90), topo 2,88;
   corre de y = −0,60 a 8,60 (balanço de 60 cm sobre cada oitão — verificar).
3. Terça intermediária 10 × 32 cm em pé (x = 1,55 e 4,45), topo 4,355; cumeeira 10 × 32
   (x = 3,00), topo 5,805. Ambas de y = −0,60 a 8,60, apoiadas nos oitões (vão livre ~7,6 m).
4. Caibros 6 × 12 cm, face inferior tocando a aresta de jusante de cada apoio; espaçamento
   ≤ 60 cm; nenhum caibro sobre a faixa dos oitões (um de cada lado, fora da mata-junta);
   caibros de testa em y = −0,57 e 8,57. 36 caibros. Cauda com corte em esquadro dentro do
   limite do beiral. Intradorso na cumeeira 5,855.
5. Bloqueios 2,5 × 23 cm entre caibros sobre o frechal (fecham o vão do beiral). 30 peças.
6. Testeiras 2,5 × 20 cm: beiral (x = −0,5875 e 6,5875) e laterais inclinadas nos oitões.
7. Pacote de cobertura como elemento Telhado (tipo novo, 17 cm): lambril 2 cm (núcleo) +
   manta (0) + contra-ripa/ripa 5 cm + telha capa-canal 10 cm (envoltória nominal).
   Face inferior = topo dos caibros; base na borda do footprint = 2,4247 m; lambril na
   cumeeira = 6,0247 m.
8. Oitões: paredes por perfil 20 cm sobre as paredes inferiores (topo 2,80), de x = 0,185
   a 5,815 (livres do frechal), topo = lambril − 1,5 cm. Três aberturas por oitão para
   as terças/cumeeira com folga de 1 cm.
9. Junta perimetral lambril × alvenaria: folga de 1,5 cm entre topo do oitão e lambril +
   mata-junta 5 × 2 cm sob o lambril, rente à face interna de cada oitão (4 peças).
   Vedação da folga (tarugo + selante) e o lado externo do oitão não são modelados.

A telha capa-canal real e ripas individuais não são elementos: a família da biblioteca
é painel de parede-cortina e não serve como camada.

## Ordem de grandeza usada para escolher as seções provisórias

Somente para não modelar peças absurdas; NÃO é verificação. Carga adotada ~1,2 kN/m²
sobre a água (telha saturada + ripamento + lambril + sobrecarga), faixa de ~1,45 m em
planta para terça intermediária e cumeeira, vão 7,8 m: M ≈ 19 kN·m. Com resistência de
cálculo ~16 MPa resulta h ≈ 27 cm para b = 10 cm; flecha L/200 com E ≈ 11 GPa pede
h ≈ 32 cm. Daí 10 × 32 (ou MLC). Caibro 6 × 12 para vão inclinado ~2,05 m @ 60 cm.
Falta verificar: flexão oblíqua das terças, apoio e esmagamento na alvenaria, balanços de
frechal/terças no oitão, sucção de vento e ancoragem, empuxo, estabilidade do oitão de
3 m (travamento), ligações e durabilidade. Alternativa a avaliar: apoio intermediário
das terças ou MLC.

## Ferramentas e sequência (após carregar 0.8.42)

- `plan_purlin_roof` (leitura): calcula tudo acima a partir do retângulo e das seções.
- `create_framing_member`: peça entre pontos 3D (create_beam achata no nível).
- `create_roof_type_variant`: tipo de telhado com as camadas.
- `detach_wall_top`, `create_profile_wall`, `create_opening_in_wall`, `analyze_wall_roof_interface`.
- `set_parameter` aceita metros para Largura/Altura de seção a partir da 0.8.42.

Roteiro: `scripts/build-purlin-roof.mjs` (dryRun por padrão; `--commit` explícito;
confere versão, documento e UniqueIds; grava saídas em `.roof-build/`):

1. `--stage plan` → conferir contagens, cotas e avisos.
2. `--stage detach` (dryRun, depois `--commit`) → paredes voltam a 2,80.
3. `--stage covering` → cria tipo e ajusta base do telhado 19401056.
4. `--stage framing` → 81 peças com comentário `MCP-COBERTURA:<papel>:<lado>`.
5. `--stage gables` → oitões por perfil e aberturas das terças.
6. `--stage verify` + cortes 19401073/19401082, 3D, avisos.

Conferir após cada commit: face inferior do telhado = topo dos caibros; justificação
"bottom" dos caibros inclinados de fato na face inferior (validar primeiro com uma peça);
terças atravessando as aberturas sem tocar a alvenaria; mata-junta sem colidir com
caibros; topo do oitão 1,5 cm abaixo do lambril; perfil da parede no plano central.

Tipos criados no modelo em 11/09 (família `_ARQPWR_Viga perfil retangular`, carregada):
19402551 Frechal 15×8, 19402553 Terça 10×32, 19402555 Cumeeira 10×32, 19402557 Caibro 6×12,
19402559 Bloqueio 2,5×23, 19402561 Testeira 2,5×20, 19402563 Mata-junta 5×2 (Largura × Altura,
conferidos). Também carregada `_ARQPWR_TELHA Cerâmica Canal` (não usada).

## Revisão do beiral (11/09, 0.8.46) — decisões do usuário e estado atual

- Fechamento: bloqueio 2,5 cm entre caibros no plano da face externa da parede, sobre o frechal,
  que ficou rente à face externa (15×8, x 0..0,15). Topo recortado pelo telhado (join_geometry).
- Testeira: recuada 5 cm da borda da telha, 2,5 × 17 cm (prumo do caibro), topo sob o lambril
  recortado pelo telhado; caibros terminam com o canto superior na face interna dela.
  Testeiras laterais 2,5 × 12 cm recuadas 5 cm; frechais, terças, cumeeira e caibros de borda
  param atrás delas.
- Calha 30×20 fixada na testeira: face de trás na testeira, topo 2 cm abaixo do lambril,
  borda da telha 5 cm dentro da calha.
- Cotas novas: intradorso dos caibros z = 2,88 + u (u a partir da face externa), cobertura base
  2,4497, lambril na cumeeira 6,0497, oitões até 6,0347.
Os números da seção anterior (frechal no eixo, base 2,4247) ficaram históricos.

## Estado modelado e validado (11/09, 0.8.43–0.8.45)

- Estrutura: 81 peças `MCP-COBERTURA:*` (ids em `.roof-build/framing-created.json`).
- Cobertura 19401056 tipo 19402587 (telha 10 + ripamento 5 + manta 0 + lambril 2 cm), base 2,4247.
- Oitões 19402931 (sul) e 19402963 (norte), perfil 2,80..6,0097 m, 3 aberturas cada.
- Calhas `_ARQPWR_Calha 30 x 20 cm` (copiada do showroom) 19408765/19408766 nos beirais.
- Cumeeira 19409188, tipo "MCP Cumeeira 45 graus" com perfil gerado a 45°. A
  `_ARQPWR_Cumeeira metálica` do escritório tem perfil fixo de 20° e foi descartada.
- Rufos: não usados. Rufo pertence a encontro do telhado com parede/platibanda; a cabana
  não tem esse encontro. A ferramenta ainda não classifica essa aresta.
- Junta lambril × alvenaria: folga 1,5 cm + 4 mata-juntas 5×2 sob o lambril.

Regras aprendidas: conferir bordas/tabicas pelo sólido (bbox inflada); `slopeCheck` antes de
aceitar cumeeira; `delete_elements` sempre em dryRun antes (esboços viram dependentes).

## Limitações conhecidas

- Cumeeira: caibros das duas águas terminam no eixo com corte em esquadro; sobreposição
  acima do eixo (sem corte a prumo). Terças com topo plano deixam cunha de 5 cm sob o caibro.
- Oitões independentes não acompanham mudanças da cobertura: ao alterar seções/inclinação,
  replanejar, apagar peças `MCP-COBERTURA:*` e oitões por perfil, e recriar.
- Apenas planta retangular, duas águas simétricas, sem rincão/espigão, sem aberturas na cobertura.
