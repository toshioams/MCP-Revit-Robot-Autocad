# Armaduras (vergalhões) nativas - 0.9.0

Primeira entrega da frente estrutural: leitura e auditoria de armadura e a parede circular armada como
primeira ferramenta de escrita. Tudo é criado como **armadura nativa e editável do Revit**, nunca como
geometria solta. A modelagem segue o mínimo da NBR 6118 e a série de bitolas da NBR 7480; o MCP não
dimensiona.

## Ferramentas

| Ferramenta | Tipo | Para quê |
| --- | --- | --- |
| `revit_list_rebar_types` | leitura | Tipos de barra (Ø nominal, aço, diâmetros de dobra, kg/m), ganchos, cobrimentos, formas, área/caminho, telas soldadas, emendas, etiquetas e tabelas de vergalhão |
| `revit_get_rebars` | leitura | Quantidade, comprimento e peso por bitola (modelo, vista ou hospedeiros) e por conjunto |
| `revit_get_rebar_host_info` | leitura | Se a peça aceita armadura, cobrimento por face e armadura que já contém |
| `revit_audit_rebar_template` | leitura | Lacunas do template contra NBR 7480 e NBR 6118 |
| `revit_new_project_from_template` | interface | Cria um `.rvt` a partir de um `.rte` (a API não abre `.rte` como documento) |
| `revit_create_curved_wall` | escrita | Parede em arco ou círculo completo (duas meias paredes), raio pelo eixo, face interna ou externa |
| `revit_reinforce_curved_wall` | escrita | Anéis e verticais de parede circular, com traspasses |

## Peso de aço

`massa = comprimento total × π Ø²/4 × 7850 kg/m³` com o diâmetro **nominal** (NBR 7480: Ø8 = 0,395 kg/m,
Ø10 = 0,617 kg/m). Sem acréscimo de perdas. É o mesmo critério do IFC exportado pelo Revit
(`CrossSectionArea × BarLength`), o que permite conferir a leitura contra o IFC.

## Parede circular

Exemplo (reservatório): Ø interno 10,70 m, parede de 20 cm, verticais Ø10 c/15 e anéis Ø10 c/15 nas
duas faces, cobrimento 3 cm.

```text
revit_create_curved_wall    center {x:0,y:0}, radius 5.35, radiusReference interior, height 3, wallTypeName "<parede de concreto 20>"
revit_reinforce_curved_wall wallIds [..], ringBar {diameterMm:10, spacing:0.15}, verticalBar {diameterMm:10, spacing:0.15}, cover 0.03
```

**Como fica modelado**

- **Anéis**: conjuntos de barras em arco (*shape driven*), distribuídos na vertical por "número com
  espaçamento". Cada conjunto continua editável no Revit (tipo de barra, espaçamento, quantidade).
  Cada anel é dividido em barras de no máximo 12 m (comprimento comercial, `maxBarLength`) que se
  sobrepõem pelo comprimento de traspasse.
- **Traspasses alternados**: os anéis se dividem em dois grupos (pares e ímpares); os pontos de emenda do
  segundo grupo ficam defasados de meia barra. Em qualquer seção vertical no máximo metade das barras
  está emendada.
- **Verticais**: conjuntos de forma livre (*free form*), um por face, meia parede, grupo e trecho, do
  cobrimento inferior ao superior. Paredes mais altas que uma barra (silos) são divididas em barras de
  até 12 m com traspasse; barras vizinhas alternam o ponto de emenda em meia barra (50% por seção).
  Traspasse vertical pela NBR com boa aderência (barras verticais, η2 = 1): Ø10 C30 = 65 cm.
- **Cobrimento constante**: centro e faces vêm das faces cilíndricas da própria parede, então todas as
  camadas são concêntricas. Lição do silo 26-060 R00: um conjunto modelado num círculo próprio
  (~Ø1060) variou de 33,6 a 64,7 mm de cobrimento ao longo da volta, enquanto as camadas concêntricas
  ficaram constantes (40,0 / 51,7 / 67,0 mm).
- **Diâmetro**: "Ø10,70 m" de silo/reservatório costuma ser o **eixo** da parede (26-060: eixo Ø1070,
  faces Ø1045 e Ø1095). Use `radiusReference` explícito e confirme com o usuário.
- **Camadas**: anéis por fora (mais próximos das faces) e verticais por dentro; `layerOrder` inverte.
- **Vista 3D**: as barras ficam "sem obstrução" em todas as vistas 3D; no nível de detalhe Alto aparecem
  sólidas (desde o Revit 2023 a API não tem mais "ver como sólido").

**Traspasse (NBR 6118 9.5.2.2), quando `lapLength` não é informado**

```text
fctd   = 0,21 fck^(2/3) / 1,4                  = 1,448 MPa (C30)
fbd    = η1 η2 η3 fctd = 2,25 × 0,7 × 1,0 × 1,448 = 2,281 MPa  (anel horizontal: má aderência, conservador)
lb     = Ø/4 × fyd/fbd = 10/4 × 434,8/2,281    = 476,5 mm  (≥ 25Ø)
α0t    = 1,8  (50% das barras emendadas na mesma seção, tabela 9.4)
l0t    = α0t × lb,nec = 1,8 × 476,5            = 857,8 mm → 90 cm (arredondado a 5 cm)
```

Com `ringBond: "good"` o traspasse cai para 65 cm. `lb,nec` usa As,calc/As,ef = 1 (sem informação de
cálculo); com a relação real o traspasse pode ser menor.

**Quantitativo dos exemplos (planejador, conferido nos testes)**

- Reservatório Ø interno 10,70 m × 3 m, e = 20 cm: 21 anéis por face (14,65 cm), 4 barras por anel
  (até 9,56 m), 226 verticais internas e 231 externas, sem emenda vertical.
- Silo 26-060 (eixo Ø10,70 m, e = 25 cm, 25,50 m, cobrimento 4 cm): verticais de 25,42 m em 3 barras de
  até 9,12 m (grupo defasado: 4 barras), 222 internas e 228 externas.

## Limitações conhecidas

- As verticais são *free form* com as barras posicionadas uma a uma: o Ø é editável no Revit, mas para
  mudar o espaçamento rode a ferramenta de novo. A distribuição "alinhada" de forma livre (espaçamento
  paramétrico em superfície curva) existe na interface do Revit, mas a API pública não a cria.
- Esperas e ancoragem na fundação não são geradas (a parede é tratada isoladamente).
- Raios medidos pelas faces cilíndricas da parede inteira: em paredes com camadas de acabamento, o
  cobrimento passa a contar da face de acabamento.
- No Revit 2026 `Rebar.CreateFromCurves` (com ganchos) e `CreateFreeForm(..., out error)` estão marcados
  como obsoletos; continuam funcionando e são os que existem no 2025. Migrar quando o 2025 sair do suporte.
