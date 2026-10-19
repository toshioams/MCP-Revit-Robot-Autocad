# Bibliotecas Python da TMS (fora do Revit)

Instaladas em 17/10/2026, só no usuário `toshi` (`pip install --user`). Fase de VALIDAÇÃO: nenhum resultado destas bibliotecas entra em projeto sem conferência contra o Robot/Eberick ou cálculo manual.

Dentro do Revit (conector pyRevit) roda IronPython 2.7: nada disto funciona lá. Fluxo: Revit exporta (CSV/JSON/IFC/DXF), Python 3 processa, resultado volta ao Revit ou vai ao Robot.

## Python 3.14 (padrão: `py -3`)
| Grupo | Biblioteca | Versão | Uso previsto |
|---|---|---|---|
| Interoperabilidade | ifcopenshell | 0.9.0.post1 | Ler/editar IFC de terceiros, compatibilização |
| | specklepy | 2026.9.0 | Troca de modelos via Speckle (Revit, Robot, Rhino, Tekla) |
| | pandas | 3.0.6 | Quantitativos, listas de cargas, tabelas de aço |
| | ezdxf, pywin32, openpyxl, python-docx, matplotlib | já existiam | DXF, COM (Robot/Excel/AutoCAD), planilhas, Word, gráficos |
| Geometria e malha | gmsh | 4.15.2 | Sólidos, booleanas, malhas |
| | meshio | 5.3.5 | Conversão de malhas (gmsh, VTK, Abaqus, Ansys) |
| | trimesh | 5.1.1 | Malhas 3D: volumes, interferências, cortes |
| | shapely | 2.2.0 | Geometria 2D: áreas, recortes, offsets, sobreposição |
| Cálculo estrutural | sectionproperties | 3.10.2 | Propriedades de qualquer seção (testado: Ixx 20x40 confere) |
| | concreteproperties | 0.8.0 | Concreto armado: momento-curvatura, interação N-M |
| | PyNiteFEA (`import Pynite`) | 3.2.0 | Pórticos 3D e placas, conferências rápidas |
| | scipy | 1.18.1 | Álgebra, otimização (dependência) |
| | anastruct | 1.7.0 | Pórticos 2D simples, conferência rápida |
| Memorial e unidades | sympy 1.14.0, handcalcs 1.11.0 | | Cálculo simbólico; cálculo "à mão" (fórmula, substituição, resultado) |
| | pint 0.26.1, forallpeople 3.0.0 | | Controle de unidades (kN, m, MPa) |
| Visualização | pyvista | 0.49.1 (vtk 9.7.1) | Malhas e resultados em 3D |
| PDF e Excel | pymupdf (`import fitz`) 1.28.2, pdfplumber 0.11.10 | já existiam | Ler PDFs de projetos e catálogos, inclusive tabelas |
| | xlwings | 0.37.6 | Comandar o Excel aberto |

Teste de 17/10/2026, balanço 5 m, P = 10 kN, EI = 20 000 kN.m²: PyNite, anaStruct e OpenSeesPy deram 0,020833 m = PL³/3EI.

## OpenSeesPy: ambiente separado Python 3.12
O pacote do Windows (openseespy 3.8.0.0) foi compilado só para Python 3.12 (exige `python312.dll`); no 3.14 dá "DLL load failed".
- Ambiente: `C:\Users\toshi\venvs\opensees` (criado com uv, Python 3.12.15)
- Rodar: `C:\Users\toshi\venvs\opensees\Scripts\python.exe script.py`
- Tem também numpy, matplotlib, pandas, openpyxl.
- Teste: balanço 5 m, P = 10 kN, flecha = PL³/3EI = 0,020833 m (confere).
- Obs.: `uv python install 3.12` dá erro no atalho de versão ("minor version link"); usar o caminho completo `C:\Users\toshi\AppData\Roaming\uv\python\cpython-3.12.15-windows-x86_64-none\python.exe`.

## Licenças (lidas dos metadados em 17/10/2026; não é parecer jurídico)
- **Livres para uso comercial** (MIT, BSD, Apache, PSF): pandas, numpy, scipy, shapely, meshio, trimesh, ezdxf, sectionproperties, concreteproperties, PyNite, sympy, handcalcs, pint, forallpeople, pdfplumber, xlwings (o núcleo; o "PRO" é pago), pyvista, vtk, matplotlib, openpyxl, python-docx, pywin32, specklepy.
- **LGPL (comercial OK, se a biblioteca fica separada e substituível e você entrega o aviso de licença):** ifcopenshell, cytriangle.
- **Restrição para produto comercial distribuído:**
  - pymupdf: AGPL-3.0 ou licença comercial da Artifex. A AGPL vale também para serviço na web.
  - anastruct: GPL-3.0.
  - gmsh: GPL-2.0 ou superior. A licença comercial é vendida pelos autores.
  - **openseespy: livre só para pesquisa, ensino e uso interno.** App ou serviço na nuvem comercial precisa de licença (contato no LICENSE.md: Dr. Minjie Zhu, Oregon State).
  - cytriangle: além da LGPL, declara "Other/Proprietary". O Triangle (J. R. Shewchuk), que ele usa por dentro, tem restrição para produto comercial. Conferir antes de vender algo que use sectionproperties ou concreteproperties.

## Outro projeto usar estas bibliotecas
Qualquer script rodado com `py -3` enxerga as do 3.14. Projeto com ambiente próprio (venv) precisa instalar nele (`pip install ...`).
