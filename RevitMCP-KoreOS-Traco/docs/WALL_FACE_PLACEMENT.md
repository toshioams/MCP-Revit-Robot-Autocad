# Inserção em face plana de parede

## Validação ao vivo realizada

Build 2026-09-09T22:56:51Z, arquivo teste mcp.rvt: parede 19389134,
4 m por 2.4 m, tipo .ALVENARIA_15CM. Face interior em Y=-0.075 m.
Ponto na face oposta foi rejeitado com point_off_face e committed:false.
Banco e chuveiro foram inseridos em dryRun com hospedeiro e LocationPoint
confirmados, seguido de inserção real: banco 19389149 (Z de inserção 0.46 m),
chuveiro 19389152 (Z de inserção 2.10 m). Leitura após commit confirmou host Wall.
Banco: bbox Z aproximadamente 0.27–0.50 m. Chuveiro: 0.900–2.128 m.
Portanto a cota de inserção do banco não equivale automaticamente ao topo do assento.
Vista de teste: 19389155, MCP - Teste hospedagem por face.
Ainda pendentes: testes de lado exterior, abertura, direção inválida e análise visual
de precisão do assento e saída de água. O teste não certifica adequação do banheiro.

Extensão opt-in de `place_family_instance`: `hostSide` interior/exterior e
`referenceDirection` (vetor adimensional tangente à face). Exige hostId de Wall,
família WorkPlaneBased e location.z absoluto explícito. Não aceita flips ou structural=true.

O lado corresponde à definição interior/exterior do Revit, não ao interior de um
ambiente. As faces vêm de HostObjectUtils.GetSideFaces. Entre faces planas cuja
projeção cai dentro da face recortada, escolhe a mais próxima, limitada a 50 mm.
O ponto projetado é informado em projectedLocationFeet; essa projeção pode alterar
também Z em paredes inclinadas. referenceDirection deve pertencer ao plano.

A criação usa a referência da face. Após regeneração confere o hospedeiro e
LocationPoint contra o ponto projetado, lançando placement_mismatch em divergência.
Não cobre faces curvas, links, pisos ou forros. Não afirma a orientação visual
correta da geometria da família apenas pela direção de referência.

Exemplo de parâmetros adicionais: hostSide: "interior",
referenceDirection: {x:1,y:0,z:0}, para uma face paralela ao plano XZ.

Validação pendente no Revit: criar parede de teste; inserir banco e chuveiro;
conferir host, XYZ e orientação geométrica. Repetir para os dois lados, abertura
na face, ponto fora da face, vetor normal/zero e dry-run sem elementos residuais.
Builds C# e TS passam; isso não substitui os testes de integração.
