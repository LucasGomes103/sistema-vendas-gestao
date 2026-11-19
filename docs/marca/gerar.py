import sys, math
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
FONT, OUT = sys.argv[1], sys.argv[2]
TEAL, ORANGE, WHITE, DARK = '#0F766E', '#F59E0B', '#FFFFFF', '#111111'

f = TTFont(FONT); gs = f.getGlyphSet(); cmap = f.getBestCmap(); hmtx = f['hmtx']
def text_path(txt, x0, base, size, track=0):
    s = size/1000.0; pen = SVGPathPen(gs); x = x0
    for ch in txt:
        g = cmap[ord(ch)]
        gs[g].draw(TransformPen(pen, (s,0,0,-s,x,base)))
        x += hmtx[g][0]*s + track
    return pen.getCommands(), x - track
def width(txt, size, track=0):
    return sum(hmtx[cmap[ord(c)]][0] for c in txt)*size/1000 + track*(len(txt)-1)

# Balão (coordenadas da arte 512x512): elipse + ponta embaixo à esquerda
cx, cy, rx, ry = 264, 236, 166, 152
def pt(a):
    a = math.radians(a); return cx+rx*math.cos(a), cy+ry*math.sin(a)
(ax, ay), (bx, by) = pt(146), pt(113)
BALAO = f"M{ax:.1f} {ay:.1f}A{rx} {ry} 0 1 1 {bx:.1f} {by:.1f}L92 430Z"
SACOLA = ('<path fill="{c}" stroke="{c}" stroke-width="26" stroke-linejoin="round" d="M190 212H334L350 318H174Z"/>'
          '<path fill="none" stroke="{c}" stroke-width="24" stroke-linecap="round" d="M225 214V182A37 37 0 0 1 299 182V214"/>')

def marca(fundo_balao, anel, sacola, contorno=None):
    s = ''
    if contorno:
        s += f'<path d="{BALAO}" fill="{contorno}" stroke="{contorno}" stroke-width="56" stroke-linejoin="round"/>'
    s += f'<path d="{BALAO}" fill="{fundo_balao}" stroke="{anel}" stroke-width="32" stroke-linejoin="round"/>'
    s += f'<g transform="translate(264 262) scale(.86) translate(-262 -262)">' + SACOLA.format(c=sacola) + '</g>'
    return s

def icone(fundo, anel, sacola, rx_=112):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="LAG CHAT">'
            f'<rect width="512" height="512" rx="{rx_}" fill="{fundo}"/>{marca(fundo, anel, sacola)}</svg>\n')

def maskable():
    # Conteúdo encolhido para a zona segura (80%) do ícone adaptável
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
            f'<rect width="512" height="512" fill="{TEAL}"/><g transform="translate(256 256) scale(.78) translate(-256 -256)">'
            f'{marca(TEAL, WHITE, ORANGE)}</g></svg>\n')

def horizontal(cor_lag, cor_chat, simbolo, simbolo_box=(66, 46, 400, 400)):
    # Símbolo ocupa a área 66..466 x 46..446 da arte (com contorno); texto à direita.
    size = 300; base = 362; x0 = 520; gap = 70; track = 4
    lag, x1 = text_path('LAG', x0, base, size, track)
    chat, x2 = text_path('CHAT', x1 + gap, base, size, track)
    w = math.ceil(x2 + 30)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="24 4 {w-24} 472" role="img" aria-label="LAG CHAT">'
            f'{simbolo}<path fill="{cor_lag}" d="{lag}"/><path fill="{cor_chat}" d="{chat}"/></svg>\n')

def mono_quadrado(fundo, frente):
    return f'<rect x="44" y="24" width="440" height="440" rx="96" fill="{fundo}"/>' + \
        f'<g transform="translate(264 244) scale(.8) translate(-262 -250)">{marca(fundo, frente, frente)}</g>'

arquivos = {
  'icone.svg': icone(TEAL, WHITE, ORANGE),
  'icone-maskable.svg': maskable(),
  'logo-fundo-escuro.svg': horizontal(WHITE, ORANGE, marca(TEAL, WHITE, ORANGE)),
  'logo-fundo-claro.svg': horizontal(TEAL, ORANGE, marca(TEAL, WHITE, ORANGE, contorno=TEAL)),
  'logo-mono-preto.svg': horizontal(DARK, DARK, mono_quadrado(DARK, WHITE)),
  'logo-mono-branco.svg': horizontal(WHITE, WHITE, mono_quadrado(WHITE, DARK)),
  'icone-mono-preto.svg': icone(DARK, WHITE, WHITE),
  'icone-mono-branco.svg': icone(WHITE, DARK, DARK),
}
for n, c in arquivos.items():
    open(f'{OUT}/{n}', 'w').write(c)
print('ok')
