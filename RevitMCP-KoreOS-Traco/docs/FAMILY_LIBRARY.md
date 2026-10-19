# Biblioteca local de famílias

`revit_recipe_search_family_library` pesquisa arquivos `.rfa` recursivamente,
sem depender do Revit aberto. Usa a pasta `familias` na raiz do repositório por
padrão. Para outra instalação, definir `REVIT_MCP_FAMILY_LIBRARY` no ambiente
do processo MCP, com o caminho absoluto da biblioteca.

Argumentos: `query` (todos os termos devem ocorrer no nome, ignorando acentos
e maiúsculas), `offset` (padrão 0) e `limit` (padrão 50, máximo 200).
Retorna `total`, `count`, `hasMore`, `nextOffset` e arquivos com caminhos absolutos.
Links simbólicos são ignorados. Erro de leitura retorna `library_read_failed`;
não é tratado como biblioteca vazia.

Fluxo: pesquisar `banco articulado`; carregar o caminho escolhido com
`revit_load_family`; consultar os tipos carregados e `placementType`; verificar
hospedagem e dimensões antes de inserir. O nome do arquivo não demonstra
adequação técnica, conteúdo geométrico, categoria ou versão compatível do Revit.

Implementação Node, sem alteração de DLL. Reiniciar o processo MCP para expor
a ferramenta nova. Os aprimoramentos C# de inserção e consulta de tipos exigem
implantação separada; não foram implantados por esta mudança.

Validação: build TypeScript, testes de busca recursiva, acentos, paginação,
extensão e diretório ausente; gate de consistência de ferramentas.
