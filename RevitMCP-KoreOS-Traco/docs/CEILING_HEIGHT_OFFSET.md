# Forro com altura explícita

Implementação local, ainda sem validação no Revit ou implantação no addin instalado.

`revit_create_ceiling` aceita `heightOffset`, relativo ao nível selecionado,
em `meters` (padrão) ou `feet`. Sem esse argumento preserva o comportamento anterior.
Exemplo: `levelName: "N1-AC", heightOffset: 2.4, units: "meters"`.

O comando escreve o parâmetro nativo de altura, regenera o documento e verifica
o valor lido. Se a escrita falhar ou divergir, lança `height_offset_failed`,
para rollback pelo dispatcher. A resposta inclui `heightOffsetFeet` e
`heightOffsetMeters`, lidos do parâmetro, inclusive quando o argumento é omitido.

Validação realizada: build C# Release para Revit 2026 com implantação desativada
e build TypeScript. Persistem os avisos NuGet de WebView2 já existentes.

Validação de integração pendente em cópia do modelo: criar com 2,40 m;
repetir com valor equivalente em pés; omitir o argumento e verificar o padrão;
executar com dry-run e conferir ausência de novo elemento após rollback.

Próximas prioridades do piloto: inspecionar hospedagem e orientação de famílias,
verificar movimento efetivo em Z e acessar famílias da biblioteca em outro documento.
As observações anteriores de bbox não provam, sozinhas, a causa desses problemas.
