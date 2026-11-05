#!/usr/bin/env python3
"""Build Jetwake Brush from original centerline lettering paths.

The companion SVGs and the TTF use the same polygon outlines. The provided
reference is a style guide only; none of its pixels or glyph contours are
traced or sampled here.
"""
from __future__ import annotations

import math
from pathlib import Path
from xml.sax.saxutils import escape

from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.fontBuilder import FontBuilder
from shapely.geometry import LineString, Point
from shapely.geometry.polygon import orient
from shapely.ops import unary_union


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Fonts/JetwakeBrush"
OUT.mkdir(parents=True, exist_ok=True)

# Stroke skeletons in a 600 × 760 em. Tails and ascenders intentionally have
# hand-painted rhythm; a thick rounded brush with tapered terminals builds them.
P = {
"A":[[(55,45),(70,700),(190,765),(330,690),(420,350),(455,50)],[(90,380),(300,410),(430,375)]],
"B":[[(95,35),(105,735),(280,750),(405,660),(355,500),(155,405)],[(155,405),(350,450),(430,350),(420,180),(320,65),(95,80)]],
"C":[[(440,650),(330,750),(170,700),(80,530),(75,250),(170,75),(340,55),(440,170)]],
"D":[[(100,45),(105,720),(230,755),(370,660),(440,420),(400,180),(275,65),(100,80)]],
"E":[[(420,700),(160,730),(90,550),(110,190),(175,65),(420,100)],[(120,400),(350,435)]],
"F":[[(110,45),(110,700),(420,745)],[(115,420),(340,450)]],
"G":[[(430,660),(335,750),(180,705),(75,520),(90,245),(200,75),(380,80),(430,245),(290,280)]],
"H":[[(90,50),(100,710)],[(420,60),(410,710)],[(105,390),(405,430)]],
"I":[[(300,65),(285,720)]],
"J":[[(395,700),(370,180),(280,55),(125,110),(95,250)]],
"K":[[(100,50),(105,710)],[(415,700),(145,385),(440,60)]],
"L":[[(120,715),(105,175),(160,60),(430,95)]],
"M":[[(75,55),(90,715),(330,250),(570,715),(585,55)]],
"N":[[(80,55),(95,700),(415,90),(405,710)]],
"O":[[(285,755),(140,700),(75,505),(90,235),(205,70),(360,90),(445,280),(425,535),(340,710),(285,755)]],
"P":[[(95,10),(100,715),(260,740),(410,650),(410,500),(330,420),(120,425)]],
"Q":[[(285,755),(140,700),(75,505),(90,235),(205,70),(360,90),(445,280),(425,535),(340,710),(285,755)],[(340,165),(500,-80)]],
"R":[[(95,25),(100,710),(270,735),(415,645),(410,505),(320,420),(120,425)],[(275,420),(450,55)]],
"S":[[(420,660),(300,755),(155,680),(125,530),(365,410),(425,250),(350,95),(190,65),(70,175)]],
"T":[[(70,690),(435,720)],[(265,700),(250,55)]],
"U":[[(95,705),(105,245),(195,75),(335,80),(420,255),(405,705)]],
"V":[[(70,700),(180,120),(250,50),(325,145),(430,705)]],
"W":[[(60,700),(150,55),(340,510),(530,55),(620,700)]],
"X":[[(90,690),(420,75)],[(430,700),(75,55)]],
"Y":[[(75,695),(240,410),(410,705)],[(240,410),(220,80)]],
"Z":[[(80,680),(430,710),(100,100),(430,85)]],
"0":[[(280,740),(150,680),(90,480),(100,220),(205,65),(350,80),(430,260),(420,530),(340,700),(280,740)]],
"1":[[(145,570),(290,735),(300,55)]],
"2":[[(90,570),(150,710),(315,725),(420,610),(375,470),(115,90),(430,100)]],
"3":[[(105,650),(245,735),(395,665),(350,500),(215,405)],[(215,405),(375,430),(425,285),(380,110),(225,55),(85,155)]],
"4":[[(350,55),(370,730),(90,300),(440,325)]],
"5":[[(405,710),(150,700),(120,445),(330,470),(420,345),(395,155),(255,55),(95,130)]],
"6":[[(370,700),(210,690),(95,475),(105,210),(235,65),(385,130),(425,285),(340,415),(130,410)]],
"7":[[(80,690),(430,725),(205,55)]],
"8":[[(270,405),(125,530),(140,680),(280,735),(415,635),(360,480),(205,400),(100,265),(150,105),(295,55),(420,145),(385,290),(270,405)]],
"9":[[(390,385),(180,380),(85,510),(135,680),(285,730),(415,630),(410,365),(305,105),(175,55)]],
"!":[[(285,710),(275,245)],[(275,65),(275,55)]],
"?": [[(105,575),(170,710),(325,720),(425,605),(380,485),(275,390),(265,255)],[(265,60),(265,50)]],
".":[[(260,65),(255,55)]], ",":[[(265,100),(225,-75)]], ":":[[(260,470),(255,460)],[(255,70),(250,60)]],
";": [[(260,470),(255,460)],[(255,100),(215,-75)]], "-": [[(115,360),(400,375)]], "'": [[(275,650),(245,525)]],
'"': [[(205,650),(180,530)],[(355,660),(330,535)]], "(": [[(330,710),(215,500),(190,280),(280,60)]], ")":[[(220,710),(345,500),(370,280),(280,60)]],
"/": [[(90,30),(420,720)]], "\\": [[(100,720),(415,30)]], "+":[[(260,520),(260,160)],[(100,340),(420,360)]], "=": [[(110,430),(420,440)],[(110,260),(420,270)]],
"&": [[(400,600),(310,710),(200,640),(215,490),(365,295),(380,170),(265,65),(130,135),(150,260),(430,60)]],
"%": [[(110,640),(175,705),(240,630),(170,555),(110,640)],[(100,70),(440,700)],[(310,150),(380,220),(445,150),(380,80),(310,150)]],
"#": [[(190,690),(155,80)],[(355,700),(320,75)],[(85,450),(445,470)],[(60,270),(420,290)]],
"@": [[(420,180),(355,90),(210,95),(100,220),(85,460),(190,650),(360,630),(405,485),(390,310),(300,300),(260,465),(345,500)]],
"$": [[(405,620),(310,710),(175,670),(110,545),(365,440),(420,300),(335,115),(165,75),(75,170)],[(270,760),(255,20)]],
"*": [[(260,530),(260,180)],[(105,420),(410,280)],[(105,280),(410,430)]],
"<": [[(390,600),(120,370),(390,140)]], ">": [[(120,600),(390,370),(120,140)]], "[": [[(340,710),(160,710),(160,80),(340,80)]], "]": [[(190,710),(370,710),(370,80),(190,80)]], "_": [[(90,-60),(430,-45)]],
"©": [[(355,575),(280,650),(170,615),(115,500),(125,300),(215,175),(340,200),(395,300)],[(305,500),(255,545),(195,500),(185,360),(235,275),(310,300)]],
"€": [[(435,630),(340,735),(205,705),(110,540),(100,300),(210,95),(350,65),(440,170)],[(85,410),(335,425)],[(85,290),(340,305)]],
"~": [[(90,320),(180,410),(280,315),(390,405),(465,330)]],
"{": [[(365,720),(260,710),(240,545),(175,410),(245,365),(260,130),(365,110)]],
"}": [[(175,720),(280,710),(300,545),(365,410),(295,365),(280,130),(175,110)]],
"[": [[(340,710),(160,710),(160,80),(340,80)]],
"]": [[(190,710),(370,710),(370,80),(190,80)]],
}

# Marks are built around x=0 and placed over the centre of their base glyph.
DIERESIS = [[(-92, 790), (-90, 790)], [(90, 790), (92, 790)]]
BREVE = [[(-95, 835), (-55, 770), (0, 750), (55, 770), (95, 835)]]
DOT = [[(-1, 790), (1, 790)]]
CEDILLA = [[(-10, 50), (10, -10), (-15, -75), (55, -65)]]
MARK_RADIUS = {"dieresis": 50, "breve": 38, "dot": 54, "cedilla": 48}

# Capitals with a mark above are lowered so the mark clears them and stays inside the ascent.
ACCENTED_HEIGHT = .8
ACCENTS = {"Ö": ("O", "dieresis"), "ö": ("O", "dieresis"), "Ü": ("U", "dieresis"), "ü": ("U", "dieresis"),
           "Ğ": ("G", "breve"), "ğ": ("G", "breve"), "İ": ("I", "dot"), "i": ("I", "dot"),
           "Ç": ("C", "cedilla"), "ç": ("C", "cedilla"), "Ş": ("S", "cedilla"), "ş": ("S", "cedilla")}
MARK_STROKES = {"dieresis": DIERESIS, "breve": BREVE, "dot": DOT, "cedilla": CEDILLA}

RADIUS = 76
GLYPH_RADIUS = {"M": 64, "W": 64}     # the widest letters need a lighter brush to keep their counters open
SIDE_BEARING = 34
SPACE_WIDTHS = {" ": 270}
DIGITS = "0123456789"


def ink_of(strokes, radius=RADIUS):
    shapes=[]
    for si, stroke in enumerate(strokes):
        for j,(a,b) in enumerate(zip(stroke,stroke[1:])):
            # varying pressure along a brush stroke avoids a mechanically even outline
            r=radius*(1 + .055*math.sin((si+1)*1.7+(j+1)*2.1))
            shapes.append(LineString([a,b]).buffer(r,quad_segs=8,cap_style="round",join_style="round"))
    return unary_union(shapes)


def glyph_ink(ch):
    """Outline of one character in em units, before spacing is applied."""
    if ch in ACCENTS:
        base, mark = ACCENTS[ch]
        above = mark != "cedilla"
        strokes = [[(x, y * ACCENTED_HEIGHT if above else y) for x, y in stroke] for stroke in P[base]]
        body = ink_of(strokes, GLYPH_RADIUS.get(base, RADIUS))
        cx = (body.bounds[0] + body.bounds[2]) / 2
        placed = [[(x + cx, y) for x, y in stroke] for stroke in MARK_STROKES[mark]]
        return unary_union([body, ink_of(placed, MARK_RADIUS[mark])])
    base = "I" if ch == "ı" else ch.upper() if ch.islower() else ch
    return ink_of(P[base], GLYPH_RADIUS.get(base, RADIUS))


def write_ink(pen, ink, dx):
    for poly in ([ink] if ink.geom_type=="Polygon" else list(ink.geoms)):
        poly=orient(poly,sign=-1.0)
        for ring in [poly.exterior,*poly.interiors]:
            pts=[(x+dx,y) for x,y in ring.coords]
            pen.moveTo(pts[0])
            for pt in pts[1:-1]: pen.lineTo(pt)
            pen.closePath()


def spacing(chars):
    """(shift, advance) per character: proportional letters, equal-width digits so a running score does not jitter."""
    result={}
    inks={c:glyph_ink(c) for c in chars if c not in SPACE_WIDTHS}
    digit=max(inks[d].bounds[2]-inks[d].bounds[0] for d in DIGITS)+SIDE_BEARING*2
    for c,ink in inks.items():
        x0,_,x1,_=ink.bounds; w=x1-x0
        advance=digit if c in DIGITS else w+SIDE_BEARING*2
        result[c]=((advance-w)/2-x0,round(advance))
    return inks,result


def main():
    chars = list("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789")
    chars += list("ÇçĞğİıÖöŞşÜü")
    chars += list("!?.:,;-_'\"()/\\+=&%#@$*<>€~{}[]")
    chars += [" "]
    chars = list(dict.fromkeys(chars))
    names={" ":"space"}
    inks,spaced=spacing(chars)
    for c in chars:
        if c != " ": names[c]=c if c.isascii() and c.isalpha() else f"uni{ord(c):04X}"
    order=[".notdef"]+[names[c] for c in chars]
    fb=FontBuilder(1000,isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap({ord(c):names[c] for c in chars})
    glyphs={".notdef":TTGlyphPen(None).glyph()}
    metrics={".notdef":(640,40)}
    # Fill the missing-glyph box with a simple bold outlined question glyph.
    nd=TTGlyphPen(None); write_ink(nd,ink_of([[(100,100),(100,700),(500,700),(500,100),(100,100)]]),0); glyphs[".notdef"]=nd.glyph()
    for c in chars:
        name=names[c]
        pen=TTGlyphPen(None)
        if c in SPACE_WIDTHS:
            glyphs[name]=pen.glyph(); metrics[name]=(SPACE_WIDTHS[c],0)
            continue
        shift,advance=spaced[c]
        write_ink(pen,inks[c],shift)
        glyphs[name]=pen.glyph()
        metrics[name]=(advance,round(inks[c].bounds[0]+shift))
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=900,descent=-180,lineGap=70)
    fb.setupNameTable({
      "familyName":"Jetwake Brush","styleName":"Regular","uniqueFontIdentifier":"Jetwake Brush Regular 1.001",
      "fullName":"Jetwake Brush Regular","psName":"JetwakeBrush-Regular","version":"Version 1.001",
      "copyright":"Copyright 2026 Jetski Runner Project. Licensed under the SIL Open Font License 1.1.",
      "manufacturer":"Jetski Runner Project","designer":"Jetski Runner Project","licenseDescription":"This Font Software is licensed under the SIL Open Font License, Version 1.1.",
      "licenseInfoURL":"https://openfontlicense.org"
    })
    fb.setupOS2(sTypoAscender=900,sTypoDescender=-180,sTypoLineGap=70,usWinAscent=900,usWinDescent=180,usWeightClass=700,usWidthClass=5,fsSelection=0x40,fsType=0)
    fb.setupPost(italicAngle=0)
    fb.setupMaxp()
    font=fb.font
    # Hand-added pairs make short headings flow more like hand-lettering.
    addOpenTypeFeaturesFromString(font,"""
      feature kern {
        pos T A -35; pos T O -30; pos T E -30; pos V A -30; pos W A -20;
        pos Y A -32; pos Y O -28; pos F A -20; pos P A -18; pos R A -12;
      } kern;
    """)
    ttf=OUT/"JetwakeBrush-Regular.ttf"
    font.save(ttf)
    # Preserve each source glyph as a standalone, editable SVG in the exact TTF units.
    for c in chars:
        if c==" ": continue
        pen=SVGPathPen(None)
        write_ink(pen,inks[c],spaced[c][0])
        d=pen.getCommands()
        safe=f"U{ord(c):04X}"
        label=escape(c)
        svg=f'''<svg xmlns="http://www.w3.org/2000/svg" width="640" height="1040" viewBox="0 0 640 1040" role="img" aria-label="{label}"><g transform="matrix(1 0 0 -1 0 860)"><path fill="#101010" d="{d}"/></g></svg>\n'''
        (OUT/(safe+".svg")).write_text(svg,encoding="utf-8")
    # A proof strip as real text is built separately by HTML/CSS using the TTF.
    print(f"Built {ttf} with {len(chars)} mapped glyphs and {len(chars)-1} SVG source files")


if __name__=="__main__": main()
