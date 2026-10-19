# Raphael — revisão das pranchas em 16/09/2026

Documento `projeto Raphael` conferido pelo caminho completo; Ginásio Tapurah aberto e consultado apenas como referência. Add-in 0.8.65 carregado (health), 145 comandos. RTK não disponível no PATH; usados comandos diretos. Nenhuma DLL alterada nesta revisão.

## Alterações efetivamente gravadas

- Subsolo: cadeias dos pilares pelas faces reais, IDs 19429038/39. Valores horizontais 3,425/0,19/2,93/0,19/2,995 m; verticais 3,19/0,30/2,74/0,30/2,77 m. Referências de família da versão carregada funcionaram ao vivo.
- AA: cotas afastadas das chamadas, textos à direita refeitos com largura em papel de 38 mm. Nota da calçada retirada porque o trecho ficou fora do recorte do modelo.
- BB: cadeias de pavimentos, altura total, espessuras de laje e pés-direitos; chamadas de material, mezanino, cobertura e identificação dos ambientes.
- Fachadas: chamadas redistribuídas, faixas laterais mais largas, cotas preservadas.
- P1/P2: etiquetas do Banheiro 2 reposicionadas para liberar as louças. Cobertura: chamada de platibanda afastada da cota.
- Viewports recriados com tipo 4024876, **Título com Número e Escala**. Todas as cinco plantas continuam 1:50; cortes e fachadas 1:100.

## Pranchas atuais

| Folha | ID | Conteúdo | Viewports |
|---|---:|---|---|
| LEV-01 |19413786|Subsolo e térreo|19429052/53|
| LEV-02 |19417434|Pavimentos 1 e 2|19429054/55|
| LEV-03 |19429040|Cobertura, AA e BB|19429056/57/58|
| LEV-04 |19429046|Fachadas frontal e lateral|19429059/60|

As quatro usam MANÁ A1. PDFs exportados para `Edificio Novo/HANDOFF_AGENTE/REVISAO_PRANCHAS_2026-09-16`, arquivos LEV-01_REVISAO.pdf até LEV-04_REVISAO.pdf. São PDFs de revisão, não emissão executiva final. Imagens em E:/lidar/trabalho/final-LEV-01.png até final-LEV-04.png.

## Verificações e limites

- Exclusões precedidas de leitura da identidade/UniqueId e dryRun: apenas notas, cotas/etiquetas auxiliares e viewports; dependentes conferidos. O arquivo do ginásio não foi modificado.
- Exportação visual de cada vista e das quatro folhas: desenhos dentro das margens e sem colisão entre viewports/carimbo.
- Auditoria de caixas: cobertura, AA, BB e fachadas sem pares sobrepostos. P1/P2 e subsolo têm apenas cruzamentos de linhas de cotas (0,0007 m²); essa checagem **não inclui todos os elementos do modelo**, nem equivale a conformidade normativa.
- Térreo do usuário preservado: auditoria acusa sobreposição de etiquetas 19416503/19421225. Ainda revisar pontualmente sem desfazer seu detalhamento.
- **Marcas AA/BB continuam ausentes nas quatro plantas baixas**, mas aparecem corretamente na cobertura/LEV-03. Mostrar OST_Sections, desocultar IDs do marcador e da vista, desativar recorte de anotação, testar escala de ocultação e disciplina não resolveu. Testes de disciplina/escala foram revertidos. OST_Viewers não tem Category acessível; OST_Views/OST_SectionHeads não podem ser ocultadas/mostradas pela API nessa vista. Não repetir cegamente essas tentativas nem afirmar que está resolvido.
- Adicionadas notas de navegação em LEV-01/02: consultar cortes e seu traçado na cobertura da LEV-03. São notas estáticas (19429069/70), não marcas nativas vinculadas.
- Para manter as marcas da cobertura dentro da folha, AA usa recorte do modelo U=-1,3..16,1; BB U=-4,5..6,2; ambos V=-3,4..13,7, recorte de anotação desativado. Textos/cotas fora do recorte geométrico são intencionais e continuam impressos. Ampliar o recorte geométrico dos cortes alonga também suas marcas nas plantas.
- RoomTags dos banheiros em BB recusaram translação (movement_mismatch, rollback); retirados os dois recém-criados e substituídos por notas locais Banheiro 2 (19429067/68). As demais etiquetas permanecem associativas.
- 33 avisos preexistentes, todos de marca de tipo duplicada, ao final. RVT salvo. Responsável técnico/CAU e demais pendências de modelagem dos registros anteriores não concluídos.

## Roteiros

E:/lidar/trabalho/revisao_pranchas_0916.mjs, revisao_qa_0916.mjs, fechar_pranchas_0916.mjs e titulos_pranchas_0916.mjs. **Não reexecutar etapas de mutação cegamente**: algumas criam elementos e deslocam por incremento. Snapshot de caixas/UniqueIds em verificacao.json junto aos PDFs; placements-0916.json lista os viewports.

## Refino gráfico — 16/09/2026, correção após crítica do usuário
REGRA EXPLÍCITA: sempre tirar print e revisar. Não considerar encaixe na prancha ou relatório de caixas como aprovação gráfica. Conferir vista ampliada e folha inteira após mudanças, corrigir e repetir.
Chamadas de AA/BB e fachadas refeitas com joelhos ortogonais, trechos horizontais, colunas alinhadas (direita alinhada à direita). Altura do joelho depende do número de linhas e do lado de ligação; lado direito da nota liga na última linha, esquerdo na primeira. Preview fornece altura da caixa. Evitar que leaders atravessem valores de cotas, níveis e etiquetas, além de impedir caixas de texto sobrepostas.
Aplicada região .TERRENO 18619419 (Earth 45 Small) sob pisos em AA (19429157/66/75) e BB (19429184). Representação gráfica de solo, não camada geotécnica levantada nem fundação dimensionada. Etiqueta Garagem AA afastada da linha de nível. Chamada piso BB transferida à direita após print revelar linha sobre área da garagem. Pontas de chamadas AA corrigidas para tocar guarda-corpo/escada. Faixa esquerda de texto da fachada frontal afastada das cotas.
Capturas revistas: E:/lidar/trabalho/revisao-visual-*.png, refino-19413769.png e refino-LEV-03/04.png. Caixas das quatro vistas sem pares sobrepostos na última consulta; não substitui conferência dos leaders por imagem. PDFs LEV-03/04_REVISAO atualizados, RVT salvo, sem exigir reinício. Pendências anteriores de marcas AA/BB nas plantas continuam.
Scripts: refino_grafico_0916.mjs, solo_cortes_0916.mjs, refino_pontas_0916.mjs, exportar_refino_0916.mjs. Não reexecutar cegamente: criam/substituem elementos.
