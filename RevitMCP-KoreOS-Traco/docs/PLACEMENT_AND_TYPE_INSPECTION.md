# Consulta de tipos e elevação de inserção

## Inspeção anterior à inserção e conferência posterior

`load_family` e `list_families` agora devolvem `placementType`.
Após carregar, consultar `get_element_info` com um ID de tipo para obter seus
parâmetros; a resposta inclui `familyPlacement` com a família e seu modo de inserção.
Para instâncias, esse objeto inclui hospedeiro efetivo, vetores de orientação,
opções e estado de inversão e, quando há LocationPoint, ponto em pés e rotação em radianos.
Vetores facing/hand seguem o sistema da família; não garantem por si só qual lado
da geometria representa as costas de uma louça.

Parâmetros passam a incluir `parameterId`, `isShared`, `sharedGuid`, `dataTypeId`
e `referencedElementName`. Campos antigos são preservados. O bbox declara `feet`;
valores Double continuam nas unidades internas correspondentes ao tipo de grandeza.
Isto distingue parâmetros homônimos sem quebrar consumidores existentes.

Em 09/09 o Revit respondeu com a build de 08/09 e Projeto1 sem alterações.
Nenhuma família foi carregada nesse documento por esta etapa; os novos dados
exigem implantação e validação ao vivo antes de serem considerados disponíveis.

Alterações locais ainda não implantadas no Revit.

## Consulta

`list_family_types` aceita `includeSystemTypes: true`. Exemplo: categoria
`OST_Ceilings` para consultar tipos de forro. O padrão false preserva a lista
de FamilySymbols. Os novos campos `className`, `isLoadableFamily` e
`placementType` distinguem a classe e o modo de inserção declarado pela família.
Para tipos sem FamilySymbol, `familyId`, `isActive` e `placementType` são nulos.
Categorias inválidas geram `invalid_parameter`, em vez de retirar o filtro.

## Inserção

O código anterior substituía location.z por level.Elevation antes da criação.
Agora Z explícito é elevação absoluta, nas unidades solicitadas; omitir Z mantém
a elevação do nível. Após regeneração, o comando confere LocationPoint.Z.
Se a família não conservar a elevação ou não oferecer LocationPoint para verificá-la,
lança `placement_mismatch` para rollback pelo dispatcher.

A resposta acrescenta `actualLocationFeet`, `actualHostId` e `placementType`.
O ponto de inserção não representa necessariamente o ponto mais baixo da geometria.
Isto não implementa hospedagem por referência de face: esse suporte continua pendente.

`move_element` passa a regenerar antes de consultar o bbox final. O código existente
já encaminhava o vetor completo à API; os relatos anteriores não demonstram que ele
descarta Z. Restrições da família e resultado efetivo ainda exigem teste no Revit.

## Validação

Build C# Release Revit 2026 sem implantação, build TypeScript e 24 testes existentes
do servidor passaram. Esses testes não exercitam a geometria nem o novo contrato em Revit.
Permanecem os avisos NuGet WebView2 preexistentes.

Testar em cópia: consulta OST_Ceilings com e sem opt-in; categoria inválida;
inserção sem Z em nível não zero; Z absoluto em metros e pés; família restrita com
elevação divergente (sem elemento residual); dry-run; movimento vertical e leitura
posterior à transação. Repetir com o chuveiro do piloto antes de declarar o caso resolvido.
