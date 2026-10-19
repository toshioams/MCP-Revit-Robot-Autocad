# Caixa de escopo — regra do usuário e estado do piloto

Regra (usuário, 11/09/2026): se o projeto trabalha com caixa de escopo, o agente
precisa entender e manipular a caixa. Não pode tratar limites de caixa de escopo
ou extensões de nível desenhadas em 3D como referência construtiva.

## O que aconteceu

Caixa ESCOPO-INT 16795380 (UniqueId f018eff5-982a-4405-8255-5459864a4fe9-010046f4)
estava em x -.622..33.527, y -12.678..7.283, z -.585..3.407 (Altura 3.99). A cabana
ocupa y 0..8 (beiral -.6..8.6) e o telhado chega a 6.02. Consequências:
- as 43 vistas associadas (plantas de layout/elétrica/hidráulica/forro, elevações
  exteriores 1–4, vistas 3D 01/02 etc.) e os 4 níveis eram recortados em y=7.28,
  escondendo parede norte, janela 19389586 e parte do banheiro, e em z=3.41;
- a cama 19394063 (limite norte 7.306) e as mesas 19401031/32 (7.225) foram
  posicionadas ~50 cm longe da face interna da parede norte (y=7.80), alinhadas à
  borda da caixa/níveis visíveis no 3D, e não à parede.

## Correção aplicada (dryRun real + commit + conferência)

1. `move_element` 16795380 por (-.2, +1.5, 0) m e `set_parameter` Altura = 7.20 m.
   Resultado: x -.822..33.327, y -11.178..8.783, z -.585..6.615. Envolve a cabana.
2. Cama +.494 m em Y → y 5.800..7.800; mesas +.575 m → y 7.350..7.800.
Avisos 34. Imagens conferidas: PLANTA BAIXA DE LAYOUT GERAL (18570484) mostra
parede norte e móveis encostados; VISTA 3D 01 (4462326) e elevações exteriores 1/3
mostram oitão e cumeeira sem corte.

Pendente para o usuário: a caixa ainda tem 34 m em X (herdada do modelo-base), o que
deixa as vistas com muito espaço vazio. A API não permite redimensionar a extensão
em planta; ajuste pelas alças no Revit se desejar enquadramento justo.

## Limitações da API e ferramenta

Revit API: sem criação nem redimensionamento em planta de caixa de escopo. Possível:
mover/girar (move_element/rotate_element) e alterar o parâmetro Altura.
`inspect_scope_boxes` (0.8.41, não validada ao vivo): extensões no referencial da
caixa (considera rotação), níveis/eixos e vistas associados, elementos fora e a
extensão necessária. Rodar antes de modelar e depois de ampliar o modelo.
Mover a caixa altera o recorte de todas as vistas associadas: listar as vistas e
conferir imagens depois. Seções associadas não podem ficar atrás da caixa.
