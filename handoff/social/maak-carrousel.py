#!/usr/bin/env python
"""
Maakt de PDF-carrousel voor de declaratie-post.

LinkedIn noemt dit een "document"-post. Volgens het Algorithm InSights-onderzoek
van Richard van der Blom scoort dat format rond 6,6% engagement, tegen circa 2%
voor een gewone tekstpost. Daarom staat de sterkste case in dit format.

Zeven slides van 1080x1350, geexporteerd als PDF. Upload de PDF rechtstreeks in
Buffer of LinkedIn; het platform maakt er zelf een swipebare carrousel van.

    python handoff/social/maak-carrousel.py
"""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).parent))
from PIL import Image, ImageDraw

from importlib import import_module
mb = import_module("maak-beelden".replace("-", "_")) if False else None

# de hulpfuncties uit maak-beelden.py hergebruiken zonder de bestandsnaam te wijzigen
import importlib.util
_spec = importlib.util.spec_from_file_location("beelden", Path(__file__).parent / "maak-beelden.py")
beelden = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(beelden)

B, H = beelden.B, beelden.H
UIT = Path("handoff/social/img")


def slide(nummer, totaal, kop, regels, groot=None, accent=None):
    im = beelden.achtergrond()
    d = ImageDraw.Draw(im)

    # voortgang: bolletjes bovenaan, zodat doorswipen logisch voelt
    for i in range(totaal):
        x = 72 + i * 30
        vol = i < nummer
        d.ellipse([x, 96, x + 14, 110],
                  fill=beelden.GROEN if vol else None,
                  outline=beelden.INK_MUTE, width=2)

    im.paste(beelden.verloopvlak(160, 6), (72, 160))

    y = 210
    f_kop = beelden.font(70, True)
    for regel in beelden.wikkel(d, kop, f_kop, B - 144):
        d.text((72, y), regel, font=f_kop, fill=beelden.INK)
        y += 86

    if groot:
        y += 30
        grootte = 170
        f = beelden.font(grootte, True)
        while d.textlength(groot, font=f) > B - 144 and grootte > 80:
            grootte -= 6
            f = beelden.font(grootte, True)
        beelden.verlooptekst(im, (72, y), groot, f)
        y += int(grootte * 1.2)

    y += 40
    f_r = beelden.font(40)
    for r in regels:
        if r.startswith("* "):
            beelden.vinkje(d, 74, y + 4, 30)
            r = r[2:]
            marge = 128
        else:
            marge = 72
        for regel in beelden.wikkel(d, r, f_r, B - marge - 72):
            d.text((marge, y), regel, font=f_r, fill=beelden.INK_DIM)
            y += 54
        y += 24

    if accent:
        d.text((72, H - 210), accent, font=beelden.font(34, True), fill=beelden.GROEN)

    beelden.logo_op(im)
    beelden.voettekst(im, f"{nummer} / {totaal}")
    return im


def main():
    UIT.mkdir(parents=True, exist_ok=True)
    T = 7
    slides = [
        slide(1, T, "Wat dit een zorgpraktijk scheelde:", [], groot="400 uur",
              accent="per jaar. Tien werkweken."),
        slide(2, T, "Het probleem", [
            "Honderden declaraties per maand.",
            "Zilveren Kruis, VGZ, CZ, Menzis, ONVZ.",
            "Betalingen komen gebundeld terug, zelden met een net declaratienummer.",
        ]),
        slide(3, T, "Afvinken is te doen. Het venijn zit hier:", [
            "* 8.950 euro voor een verrichting van 89,50",
            "* Behandeldatum na de indiendatum",
            "* Twee keer hetzelfde declaratienummer",
            "* 90 dagen te laat ingediend",
        ]),
        slide(4, T, "Wat de agent doet", [
            "Matcht declaraties met de betalingen uit SnelStart.",
            "Benoemt wát er mis is, niet alleen dát er iets mis is.",
            "Draagt de vervolgstap aan, met de correctie er al bij.",
        ]),
        slide(5, T, "En dan het belangrijkste", [
            "Bij elk voorstel staat hoe zeker hij is.",
            "Boven 80 procent handelt hij het af.",
            "Daaronder doet hij niets en stelt hij een vraag.",
        ], accent="6 van 8 zelf. 2 terug met een vraag."),
        slide(6, T, "Waarom die grens er is", [
            "Een agent die altijd doorpakt is makkelijk te bouwen.",
            "Een agent die weet wanneer hij moet stoppen is het werk waard.",
            "Dat is het verschil tussen vertrouwen en nacontroleren.",
        ]),
        slide(7, T, "Wat kost dit uitzoekwerk jou per maand?", [
            "Eén call van 45 minuten, dan rekenen we het samen door.",
            "Geen verkooppraat. Als een agent het niet aankan, zeggen we dat.",
        ], accent="soratus.com · hallo@soratus.com"),
    ]

    pad = UIT / "02-declaraties-carrousel.pdf"
    slides[0].save(pad, "PDF", resolution=150.0, save_all=True,
                   append_images=slides[1:])
    for i, s in enumerate(slides, 1):
        s.save(UIT / f"carrousel-slide-{i}.png", "PNG", optimize=True)
    print(f"carrousel: {pad} ({len(slides)} slides)")
    print(f"losse slides: {UIT}/carrousel-slide-1..{len(slides)}.png")


if __name__ == "__main__":
    main()
