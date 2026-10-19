# Cabana colonial — estudo em desenvolvimento

## Correções de piso solicitadas

O usuário definiu banheiro ao menos 1 cm abaixo da cabana e soleira nos desníveis.
Aplicado banheiro 19389484 com deslocamento -0.01 m. Soleira em granito São Gabriel
19394218, aproximadamente 84.5 x 15 cm, topo 0.00, no vão da porta.
Piso integrado substituído por 19394209, contorno recuado às faces externas do
banheiro (X=3.45/Y=5.25), sem piso sob a alvenaria. A soleira ocupa a transição.
Regra é preferência deste projeto/escritório; requisitos de acessibilidade devem
ser avaliados conforme o caso, sem transformar o desnível em regra normativa geral.
Pendente compatibilizar espessuras/apoio: rebaixar acabamento de 5 cm em 1 cm
faz sua base chegar a -0.06 m, abaixo do topo atual da laje (-0.05).

Arquivo ativo: teste mcp.rvt. Implantação proposta 6 x 8 m externos.
Alvenaria externa 20 cm, interna 15 cm. Banheiro no fundo à direita;
estar, cozinha e dormitório integrados. Piso de banheiro separado.

Correção solicitada pelo usuário: a parede deve alcançar o apoio sob o piso
acabado. Conferido topo da laje em -0.05 m. Aplicado deslocamento de base
-0.05 às seis paredes, com altura desconectada 2.85, preservando topo em +2.80.
Não adotar -0.05 universalmente: medir o apoio de cada projeto.

IDs: paredes 19389460–19389465; laje 19389468; pisos 19389475 e 19389484;
portas 19389512/19389547; janelas 19389574/19389582/19389584/19389586;
cama 19394063; sofá 19394064; bancada 19394065; mesa 19394066;
ambientes 19394067/19394068; forro do banheiro 19394071.

Cobertura pendente: novo create_gable_roof cria telhado nativo retangular de
duas águas com ridgeAxis, slopeDegrees, roofTypeId, levelName, baseOffset e min/max.
Min/max são o perímetro do telhado, incluindo beirais, em XY. Não anexa paredes
nem fecha empenas. É necessário validar tipo/material e espessura: ainda não foi
identificado tipo de telha cerâmica no template. Não substituir por fibrocimento
silenciosamente. Proposta: 45 graus, cumeeira paralela a Y e beirais de 0.60 m.

Pendências: validar novo comando no Revit, resolver tipo de telha cerâmica e
empenas, equipar banheiro e cozinha, verificar interferências e revisar visualmente.
O modelo ainda não constitui cabana concluída. Agente não salvou o arquivo.
