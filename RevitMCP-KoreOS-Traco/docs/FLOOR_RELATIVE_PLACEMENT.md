# Inserção relativa ao piso acabado

`place_family_instance` aceita `floorId` e `heightAboveFloor` juntos, omitindo
`location.z`. A altura usa `units`, metros por padrão. O ID deve ser de um Floor
no documento ativo escolhido como acabamento. Não há classificação automática.

O comando intersecta uma linha vertical no XY solicitado com as faces superiores
reais do piso. Não usa elevação do nível nem topo do bounding box para calcular a cota.
O bbox apenas delimita a linha de busca. Aberturas e pontos sem superfície retornam
floor_surface_not_found; múltiplas elevações retornam ambiguous_floor_surface.
Superfícies coincidentes dentro de tolerância 1e-6 pés contam como uma interseção.

Cota de inserção = superfície local + heightAboveFloor. O resultado floorReference
registra piso, elevação da superfície e altura do ponto de inserção em metros.
Não se aplica automaticamente compensação entre a origem da família e topo do assento.
Não cria vínculo paramétrico: mudanças posteriores no piso exigem nova avaliação.

Com hospedagem por face, o ponto calculado deve pertencer à face sem projeção:
isso evita usar uma cota de piso em XY diferente do ponto final. Em limites do piso,
se não houver interseção válida, o agente deve corrigir a referência, não adivinhar.

Exemplos de validação pendente: piso horizontal +0.05 e altura 0.46 → inserção 0.51;
piso -0.03 → inserção 0.43; piso inclinado em dois XY; ponto sobre abertura;
conflito com location.z; piso estrutural explicitamente distinto do acabamento;
rollback sem peças residuais. Builds C# Revit 2026 e TypeScript passaram.
Esta mudança ainda requer validação ao vivo antes de uso no banheiro.
