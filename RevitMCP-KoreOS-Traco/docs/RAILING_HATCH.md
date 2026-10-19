# Tela leve dos guarda-corpos — versão preparada 0.8.61

O usuário autorizou hachura cruzada para representar a tela. Não modelar fios.

## Recursos

- `create_material`: novo material independente, cor `#RRGGBB`, `graphicsTransparency` 0..100, `crosshatch:true`, `crosshatchSpacingMeters` 0,005..1 m, `crosshatchAngleDegrees` (padrão 45). Hachura de modelo com duas direções perpendiculares. Recusa nomes existentes para preservar materiais compartilhados. Aparência Generic independente. Opcional: `cutoutImagePath` absoluto e `textureScaleMeters` para recorte bitmap; NÃO necessário nesta tarefa.
- `create_direct_shape`, sólido `panel`: `profile` com 3..32 pontos xyz em metros e `thicknessMeters` até 0,1. Polígono convexo, coplanar e sem cruzamentos; espessura centrada no plano. Um sólido por painel, sem fios. As espessuras são representação, não medição da tela.
- `inspect_planar_faces includeEdgeLoops:true`: retorna contornos reais das faces em metros. Usar para derivar o preenchimento dos tubos; não posicionar a partir de bbox ou caixa de escopo.

## Retomada após reinício

1. Verificar health 0.8.61, documento projeto Raphael e UniqueIds dos seis guarda-corpos.
2. Executar `node scripts/prepare-railing-hatch.mjs`: dryRun do material, verificar committed:false; exporta bordas reais dos tubos para E:/lidar/trabalho/guardas-hachura. Nada cria painéis automaticamente.
3. Com --apply-material cria só o material e salva, após dryRun. Cor cinza, transparência gráfica 80%, malha 5 cm a 45° como hipótese ajustável visualmente.
4. Derivar painéis por lance/patamar, encaixados entre tubos inferior/superior. Criar primeiro um painel piloto de 1 mm com materialName, dryRun → committed:false → commit. Fase criada Existente. Conferir corte/3D, transparência e hachura antes dos demais. Conferir bordas abertas do patamar L3.
5. Confirmar volume/quantidade de sólidos e ausência de novos avisos; salvar RVT e atualizar HANDOFF. Prosseguir acabamentos por layerMaterialIds (0.8.60 incorporada) e pendências 3–6.

## Limites

Hachura é padrão gráfico de superfície, não recorte físico nem textura de render. A transparência uniforme é aproximação para vistas gráficas; a aparência Generic sem bitmap permanece opaca em render. O usuário aceitou hachura cruzada. Não declarar comportamento visual validado antes da inspeção ao vivo.

Compilação C# e Node, 224 testes C# e 44 Node passaram. Essas suítes não executam geometria ou materiais dentro do Revit. Recursos novos não estão carregados nem validados ao vivo.

Referência API: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/743c74ba-12de-4d77-a677-325229525955.htm
