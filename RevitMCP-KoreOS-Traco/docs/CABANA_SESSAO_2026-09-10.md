# Continuidade sem reinício

## 11/09 ~13h45 — beiral refeito (0.8.46)

Usuário questionou fechamento, testeira e calha no corte. Decidido: bloqueio na face externa,
testeira recuada 5 cm com altura do prumo, calha na testeira. Planejador e ferramentas
atualizados (join_geometry, offsets de borda). Roteiro recriou tipos, desmontou e refez
estrutura, oitões e calhas; cada estágio em dryRun antes do commit. Avisos 34.

## 11/09 ~12h30 — oitões, bordas, junta do banheiro, cumeeira a 45°

0.8.43: oitões por perfil criados após descobrir que Wall.Create com perfil nasce preso ao
próximo nível; aberturas das terças. Regressão de documento (batch/UI) corrigida na 0.8.44.
0.8.44: tipos copiados do showroom R01_2026; calhas nos beirais; rufos laterais removidos a
pedido do usuário (sem platibanda); forro do banheiro recriado com folga de 2 cm e Tabica
tipo 2 nas 4 bordas. 0.8.45: perfil de cumeeira gerado a 45°, cumeeira metálica de 20°
substituída; slopeCheck detecta o perfil errado. Avisos 34 ao fim.

## 11/09 ~11h30 — decisões da cobertura e ferramentas de estrutura

Usuários decidiram: terças nos oitões, lambril sobre caibros sem forro, capa-canal,
junta lambril × alvenaria. Constatado que create_beam achata as extremidades no nível
(sem caibros inclinados) e que a telha capa-canal da biblioteca é painel cortina.
Carregadas as famílias de viga retangular e telha (sem sobrescrever). Criados 7 tipos
de madeira; set_parameter recusou metros em Largura (spec de seção) e os valores foram
gravados em pés e conferidos. Implementados create_framing_member, create_roof_type_variant,
plan_purlin_roof (planejador puro com 9 testes) e roteiro build-purlin-roof.mjs, que
recusou executar na 0.8.40 como previsto. Pacote 0.8.42 preparado. Avisos 34.

## 11/09 ~11h — encontro paredes/telhado, caixa de escopo (Revit 0.8.40)

Revit foi recarregado pelo usuário durante a inspeção (0.8.38 → 0.8.40/109).
Documento reaberto sem modificações; UniqueIds do telhado, paredes e cortes
conferidos. Cortes antes: cunha 20x20 cm aberta na face interna das paredes de
beiral (intradorso 2.80 ext./3.00 int.) e oitões abertos de 2.80 a 5.80.

attach_wall_top nas 4 paredes externas: gravado por engano (dryRun enviado em
params). Geometria conferida por faces, bbox, cortes, 3D e elevações; avisos 34;
vãos preservados. Mantido; oitões por anexação pendentes de aprovação.
create_profile_wall: dryRun falhou em todos os perfis/tipos (verificação por bbox);
rejeição de côncavo OK. Nenhuma parede por perfil criada.

Usuário apontou famílias no limite da caixa de escopo/níveis. Confirmado: ESCOPO-INT
terminava em y=7.283/z=3.407; cama e mesas a ~50 cm da parede norte. Caixa movida
+1.5 m Y, -.2 m X, Altura 7.20; cama e mesas encostadas (y máx 7.80). dryRun real
antes, commit, leitura e imagens (layout, 3D 01, elevações 1 e 3). Avisos 34.

Código 0.8.41 preparado: dryRun seguro, create_profile_wall por sólido,
analyze_wall_roof_interface, detach_wall_top, inspect_scope_boxes. RVT não salvo.

## 11/09 — oitões por perfil, versão0.8.40

Implementado create_profile_wall para fechamento independente acima de paredes
existentes quando anexação não serve. Perfil vertical convexo, parede básica,
validação geométrica de entrada e conferência das elevações após regenerar.
Anexação continua preferencial; regra detalhada em WALL_ROOF_CONNECTION.md.
Ferramentas de anexação/perfil expostas também no grupo architecture.
Builds C#/Node e testes existentes170/26 passaram, consistência109/103 passou.
Preparado pacote RevitMCP-local-20260911-094350 e atualizado manifesto.
Revit permaneceu0.8.38/107 comandos; nenhuma edição no RVT nesta etapa.
Nova API ainda precisa de teste real após recarga. Perfil não acompanha telhado
automaticamente nem substitui análise de apoios, espessura e folgas.

## Revisão do box

Tipo19400968 renomeado220 x210 cm (uma instância afetada). Vista isométrica
19401005 com recorte exibiu linhas distantes e escala inadequada, inclusive após
isolamento temporário. Criada perspectiva19401016 para revisão frontal, com
paredes/forro/porta do banheiro e bancada/armário/cooktop da cozinha ocultados
somente nessa vista para retirar obstruções da câmera. Não foram removidos do modelo.
Parâmetros de tipo não oferecem abertura explícita; movimento da folha e vão útil
continuam sem validação. Necessário melhorar enquadramento/exportação e inspeção
da geometria da folha móvel antes de declarar abertura funcional aprovada.

## Box deslizante

Criado19400983, família _ARQPWR_VDR Box com porta deslizante, tipo19400968.
Largura alterada de1.40 para2.20 m; nome de tipo ainda140 x210 cm, não corresponde
à largura atual e precisa renomeação. Inserção central (4.7,6.7,-.01) m; limites
laterais x3.6..5.8 m entre paredes. Altura nominal2.10 m; caixa inclui perfil
superior até2.12 m. Faixa de banho norte com profundidade aproximadamente1.10 m.
Vista19400986 mostra painéis mas recorta o conjunto: abertura útil, puxadores,
orientação funcional e compatibilidade com layout completo ainda não validados.
Avisos34, sem incremento. Não considerar o banheiro concluído: bacia, ralo,
caimento, torneira e redes continuam pendentes.

## Toalheiro e teste de misturador

Toalheiro 19399942 instalado na parede sul do banheiro, ponto (5.15,5.4,1.19)
m, host19389464, face exterior da API (interior físico), referência +X.
Caixa de aproximadamente31 cm de largura e projeção5.7 cm para o banheiro.
Vista19399946 criada para conferência. Misturador de parede19399940 removido:
primeira tentativa não apresentou bica corretamente orientada sobre o lavatório.
Torneira continua pendente de calibração da família. A remoção inicial em batch
com toalheiro falhou e foi revertida; segundo batch confirmou remoção e instalação.

## Lavatório

Lavatório Vogue Plus 19399925, tipo L.510.17_Branco Gelo GE17,
hospedado na face x=5.8 da parede 19389461, eixo y=6.0 alinhado ao espelho.
Origem da família não coincide com borda: inserção inicial a .84 m resultou em
topo 1.64 m. Corrigida inserção para .04 m; topo final .840016 m, aproximadamente
.85 m acima do piso acabado -.01 m. Deslocamento vertical confirmado após commit.
Captura mostrou cuba orientada para cima; vista anterior recorta parte inferior,
motivando vista específica do lavatório. Torneira, sifão/rede e conferência do
conjunto completo permanecem pendentes. Avisos permaneceram 34.

## Acessórios e vista interna

Espelho circular com moldura 19397967, diâmetro observado 0.75 m, centro
(5.8,6.0,1.49) m; hospedado na face interna física da parede leste. Comando
do chuveiro 19397980 em (5.8,7.4,1.09) m, afastado 25 cm do eixo do chuveiro
após imagem revelar superposição com a mangueira. Sem conexões hidráulicas.
Vista perspectiva 19397970, MCP - Conferência banheiro - acessórios, permite
conferência próxima. Capturas confirmaram espelho, chuveiro e comando separados.
Avisos permanecem 34. Lavatório/box e redes ainda pendentes.

Gap encontrado: CreatePerspectiveView devolvia eye em metros com fator invertido,
embora a câmera fosse criada corretamente. Corrigido fator para FeetToMeters,
build passou e DLL preparada para próximo reinício; retorno corrigido ainda não
validado ao vivo. Outros gaps: enquadramento automático corta bordas do espelho;
checagem de interferência entre acessórios deve preceder aceitação da posição.

## Terceira etapa: armário inferior

Carregadas sem sobrescrever: _ARQPWR_MRC Armário básico e _ARQPWR_HDR Cuba
retangular estilo Farm. Armário 19396967 sob a bancada, altura 0.80 m,
profundidade 0.55 m, largura final solicitada 2.16 m e inserção (5.8,5.06,0) m,
rotação -90 graus. Ajuste de 4 cm evita extrapolação da caixa envolvente em direção
à parede do banheiro. Conferir visualmente frentes/aberturas da família.
Cuba não instalada: hospedagem WorkPlaneBased impediu retenção de z=1 m;
transação revertida. Instalação depende de face/plano horizontal.
Avisos após carga/armário: 34. SetParameter retornava texto anterior à regeneração;
adicionada regeneração antes da leitura do resultado no código pendente.

## Segunda etapa: cozinha e jantar

Criados refrigerador 19394313, cadeiras de madeira 19394314/19394315 e cooktop
19394316. Refrigerador girado 90 graus para abrir em direção ao ambiente; cadeira
norte girada 180 graus para a mesa. Bancada 19394065 foi encontrada na orientação
original (rotação 0) e reposicionada: limites x=5.2..5.8, y=2.9..5.1 m,
topo=1.0 m. Cooktop girado 90 graus, inserção (5.5,3.35,1.0) m.
Conferência por bounding boxes e rotação, sem revisão visual nesta etapa.
Cooktop ainda requer recorte/compatibilização da bancada; cozinha ainda sem cuba,
torneira e armários. Contagem de avisos permaneceu 34 antes da rotação final.

Usuário pediu seguir modelando e acumular atualizações do MCP para reinício posterior.

Instalado chuveiro Deca Aspen 19394309 na parede leste 19389461, face x=5.8 m.
Ponto (5.8, 7.15, 2.09) m, 2.10 m acima do piso do banheiro. Direção de referência
(0,-1,0). Bounding box confirma corpo entre aproximadamente 0.89 e 2.12 m de
elevação e projeção para dentro do banheiro. Verificação geométrica, não visual.
Na orientação atual da parede, a API chama essa face de exterior; interior/exterior
do tipo não deve ser confundido com dentro/fora do ambiente.

Tentativa de bacia 19394310 foi removida com seus dois dependentes. A família
WorkPlaneBased ficou presa ao plano z=0 e ignorou movimento vertical de -1 cm.
Nenhuma bacia dessa tentativa permanece. Precisa hospedagem por face horizontal.
MoveElement agora verifica o deslocamento real de elementos com LocationPoint e
reverte se houver diferença; mudança compilada, ainda não carregada no Revit.

Telhado: dryRun falhou com ArgumentNullException. Inicializado ModelCurveArray
antes da chamada nativa como correção candidata. Necessário reteste após reinício;
nenhum telhado criado. Tipo cerâmico/empenas continuam pendentes.

Contagem de avisos após modelagem: 34, igual à inicial. Sobreposição original do
piso/laje continua pendente da ferramenta de variante 0.8.36.
