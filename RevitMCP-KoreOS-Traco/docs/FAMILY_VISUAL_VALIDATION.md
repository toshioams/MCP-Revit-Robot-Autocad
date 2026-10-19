# Interpretação e conferência das famílias

## Superfície de apoio do cooktop

Corrigido 19394316: elevação de 1.00 para 0.90 m, confirmada após commit.
O máximo Z da bancada (1.00 m) incluía o rodamão; Altura do tipo identifica
o tampo a 0.90 m nesta família. Captura posterior mostra cooktop junto ao tampo.
Nunca usar máximo Z global como superfície de apoio de cuba/equipamento.
Implementação futura deve consultar a face horizontal no XY de instalação,
com normal, contorno e identificação de elemento/subelemento. Recorte do tampo
para embutir o corpo do cooktop continua pendente; altura corrigida não resolve
automaticamente a interseção com a pedra.

Correção de método após revisão visual do usuário em 10/09/2026. Bounding boxes
não identificam frente, encosto, portas, faces de apoio ou abertura. FacingOrientation
também não garante frente física; depende de como cada família foi construída.

## Procedimento para o agente

1. Consultar parâmetros da instância E do tipo: IDs, unidades, leitura/escrita,
   booleanos de visibilidade e dimensões. Distinguir mudança de tipo compartilhada
   de alteração local; conferir todas as instâncias afetadas antes de editar tipo.
2. Usar get_view_image antes e depois. A captura exporta a vista do modelo,
   não a interface completa do Revit. Preferir vistas que mostrem os lados relevantes.
3. Identificar visualmente a frente real e relacioná-la à orientação local da família.
   Registrar calibração por família, sem generalizar para outras famílias.
4. Medir distância entre faces acabadas. Ajustar dimensões e ponto de inserção;
   não compensar um móvel curto apenas movendo-o. Caixas envolventes podem incluir
   linhas simbólicas/controles e não são superfícies físicas de contato.
5. Mapear rodamão/saia esquerdo/direito para os lados físicos após a rotação.
   Rodamão na ponta livre e saia contra parede devem ser avaliados pelo encontro.
6. Relê-los após regeneração/commit, capturar a vista e consultar avisos. Ausência
   de avisos não comprova ausência de colisões ou adequação funcional.

## Calibrações observadas nesta cabana

- Cadeira MS Wood Elle: facingOrientation apontava para o encosto; corrigidas
  ambas as cadeiras com giro de 180 graus a partir da posição anterior.
- Refrigerador Electrolux 504L: frente real invertida em relação à interpretação
  anterior do vetor. Giro de 180 graus; depois translação x=-0.33 m para manter
  caixa envolvente até x=5.75 m e afastamento de 5 cm da face x=5.8 m.
- Bancada ARQPWR linear: Largura no tipo 19394040 de 2.20 para 2.35 m;
  inserção y=5.25 m, ponta oposta y=2.90 m. Rodamão esquerdo=0 corresponde
  à ponta livre nesta rotação; Saia direita=0 no encontro com a parede.
  Mantidos rodamão posterior e rodamão junto à parede transversal.
- Armário 19396967: largura de instância 2.35 m, inserção y=5.25 m;
  corpo encosta na parede transversal. A extensão simbólica da caixa envolvente
  não foi usada para encurtar novamente o armário.

Este documento registra o procedimento e a calibração observada. Ainda não existe
classificador semântico automático universal de famílias implementado no MCP.

## Janelas e pingadeiras

Usuário identificou pingadeiras internas. Instâncias 19389574/19389582 (oeste),
19389584 (leste) e 19389586 (norte) têm FacingOrientation dirigido para o interior
da cabana e CanFlipFacing=true. Não basta inverter indiscriminadamente qualquer
família: conferir projeção/caimento da pingadeira, face externa do edifício e
abertura/maçanetas em imagem ou corte antes e depois.

MCP carregado permite flipFacing somente na criação. Espelhamento sem cópia
de 19389574 foi rejeitado no commit com erro de instância sem hospedeiro,
GUID c0777eaf-1ec2-432b-8e92-56c6eebb4c7e; rollback confirmado.
DryRun havia passado, demonstrando novamente que não valida o commit.
Janelas permanecem sem correção nesta etapa. Implementação prioritária:
set_family_orientation com estado-alvo facingFlipped/handFlipped, idempotente,
checagem CanFlip*, preservação de ID/hospedeiro/ponto e leitura após regeneração.
Conferência semântica da pingadeira depende também de geometria e imagem;
não declarar resolvida apenas por mudar o booleano.
