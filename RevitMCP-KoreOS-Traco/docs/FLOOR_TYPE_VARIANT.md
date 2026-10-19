# Variante de camadas de piso — 0.8.36

`create_floor_type_variant` recebe `sourceTypeId`, `name`, `layerWidthsMeters`
na ordem nativa e `floorIds` opcionais. Copia o tipo, preserva materiais e funções,
limpa a marca de tipo da cópia e altera somente os pisos indicados. Rejeita nomes
existentes, pisos de outro tipo, fixados ou agrupados. Falhas revertem a transação.
O comando confere as espessuras após regeneração. Não representa validação construtiva.

Aplicação prevista, ainda NÃO executada no modelo:

```json
{
  "sourceTypeId": 18691565,
  "name": ".ACABAMENTO_Porcelanato 80x80 cm - conjunto 4cm",
  "layerWidthsMeters": [0.01, 0.01, 0.02],
  "floorIds": [19389484]
}
```

Conferir antes da execução se IDs, composição e níveis continuam iguais. Manter
o piso a -0.01 m, com base resultante -0.05 m, sem alterar soleira. Conferir depois
bounding box, tipo original intacto, aviso de sobreposição eliminado e ausência
de avisos novos. Essa solução reduz a regularização em 1 cm.

A DLL nova requer reinício do Revit; o cliente MCP precisa recarregar os esquemas.
