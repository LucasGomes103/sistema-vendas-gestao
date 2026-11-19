# Marca LAG CHAT

Logo da plataforma, recriado em SVG vetorial (fundo transparente) a partir da arte original.

| Arquivo | Uso |
| --- | --- |
| `icone.svg` | Ícone do app (quadrado teal arredondado). Favicon, menu lateral. |
| `icone-maskable.svg` | Ícone adaptável (Android), com o desenho na zona segura. |
| `logo-fundo-escuro.svg` | Logo horizontal para fundos escuros ("LAG" branco). Tela de login. |
| `logo-fundo-claro.svg` | Logo horizontal para fundos claros ("LAG" teal). |
| `logo-mono-preto.svg` / `logo-mono-branco.svg` | Versões de uma cor (impressão, carimbo, fundo foto). |
| `icone-mono-preto.svg` / `icone-mono-branco.svg` | Ícone de uma cor. |
| `icon-512.png` | Ícone em PNG para redes sociais e lojas. |

Cores: teal `#0F766E`, laranja `#F59E0B`, branco `#FFFFFF`.
Fonte do texto: Montserrat ExtraBold (licença OFL), já convertida em contornos; não precisa ter a fonte instalada.

O painel usa os arquivos em `frontend/projects/painel/public/`. A vitrine de cada loja (`/ecsfish` etc.) continua com a marca da loja.

`gerar.py` refaz os SVGs: `python3 gerar.py montserrat-latin-800-normal.woff <pasta>` (precisa de `fonttools`).
