# Base de conferência — 0.8.37

Implementado:
- `inspect_planar_faces`: faces planas de sólidos, normais, áreas e material;
  probe XY intercepta cada face respeitando seus limites. Assim a consulta no
  centro do cooktop pode distinguir tampo de rodamão fora desse XY.
  Paginar até hasMore=false; índices transitórios, sem referência de hospedagem.
  Geometria copiada de instâncias em coordenadas do documento; validar ao vivo
  famílias aninhadas/giradas. Faces curvas não são cobertas.
- `set_family_orientation`: estados-alvo facingFlipped e handFlipped, sem recriar
  instância. Verifica estado, ponto e hospedeiro após regeneração. Falhas revertem.
  Não decide semanticamente a frente: exige conhecimento da família e imagem.
- `scripts/audit-cabana.mjs`: regressão somente leitura para altura do cooktop,
  largura do armário e orientação das cadeiras, com guardas de identidade.
  Executar após build Node: `rtk node scripts/audit-cabana.mjs`.

Ainda pendente: catálogo semântico consultável via MCP, classificação automática
de superfícies, hospedagem horizontal, contornos/referências estáveis, persistência
genérica por projeto, regras de encontro e execução da bateria visual completa.
FAMILY_VISUAL_VALIDATION.md contém calibrações observadas, não um classificador.

Aceite no Revit após reinício: consultar bancada 19394065 com probe
(5.5,3.35,0), encontrar tampo perto de 0.90 m; consultar fora da bancada e em
rodamão para distinguir resultados; inverter janela preservando ID/host/ponto,
repetir pedido e confirmar changed=false, capturar vista exterior da pingadeira.
