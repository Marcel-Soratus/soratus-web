#!/usr/bin/env python
"""
Trekt de case-screenshots gelijk: zelfde breedte, zelfde beeldverhouding.

Waarom: de kaders op de case-pagina staan in een tweekoloms-grid. Screenshots
met verschillende verhoudingen geven ongelijke blokken naast elkaar.

Werkwijze: er wordt nooit bijgesneden, alleen opgevuld. De opvulkleur wordt uit
de hoek van de afbeelding gehaald, zodat de rand naadloos aansluit op de
UI-achtergrond van de schermafbeelding zelf.

Gebruik, vanuit de root van de repo:

    python handoff/cases/normaliseer-screenshots.py              # normaliseren
    python handoff/cases/normaliseer-screenshots.py --check      # alleen tonen
    python handoff/cases/normaliseer-screenshots.py --trim-bottom jaarverslag-kengetallen.png=40

De originelen worden bewaard als <naam>.orig.png, zodat je altijd terug kunt.
"""

import argparse
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow ontbreekt. Installeren met: pip install Pillow")

MAP = Path("Soratus.Web/wwwroot/img/cases")
BREEDTE = 1400  # de kaders zijn circa 700px breed, dus 2x voor scherpe weergave

BESTANDEN = [
    "declaraties-betalingen.png",
    "declaraties-matching.png",
    "declaraties-agent.png",
    "jaarverslag-start.png",
    "jaarverslag-rapport.png",
    "jaarverslag-kengetallen.png",
]


def randkleur(im):
    """Meest voorkomende kleur langs de bovenrand: de UI-achtergrond."""
    rand = [im.getpixel((x, 0)) for x in range(0, im.width, max(1, im.width // 40))]
    return max(set(rand), key=rand.count)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--check", action="store_true", help="alleen tonen wat er staat")
    p.add_argument("--breedte", type=int, default=BREEDTE)
    p.add_argument("--verhouding", type=float, default=None,
                   help="doelverhouding b/h; standaard de mediaan van de aanwezige beelden")
    p.add_argument("--trim-bottom", action="append", default=[], metavar="BESTAND=PIXELS",
                   help="snij eerst N pixels van de onderkant, bijv. voor een footer met klantnaam")
    a = p.parse_args()

    if not MAP.is_dir():
        sys.exit(f"Map niet gevonden: {MAP}\nDraai dit vanuit de root van de repo.")

    trim = {}
    for t in a.trim_bottom:
        naam, _, px = t.partition("=")
        trim[naam] = int(px)

    aanwezig, ontbreekt = [], []
    for naam in BESTANDEN:
        pad = MAP / naam
        (aanwezig if pad.is_file() else ontbreekt).append(naam)

    if ontbreekt:
        print("Ontbreekt nog:")
        for n in ontbreekt:
            print(f"   - {n}")
        print()
    if not aanwezig:
        sys.exit("Nog geen enkele afbeelding gevonden. Zet ze eerst in de map.")

    print("Gevonden:")
    maten = {}
    for naam in aanwezig:
        with Image.open(MAP / naam) as im:
            maten[naam] = (im.width, im.height)
            print(f"   {naam:34} {im.width}x{im.height}  ({im.width/im.height:.2f})")
    print()

    if a.check:
        return

    if a.verhouding:
        doel = a.verhouding
    else:
        vs = sorted(w / h for w, h in maten.values())
        doel = vs[len(vs) // 2]
    print(f"Doel: breedte {a.breedte}px, verhouding {doel:.2f}\n")

    for naam in aanwezig:
        pad = MAP / naam
        backup = MAP / (pad.stem + ".orig.png")
        with Image.open(pad) as im:
            im = im.convert("RGB")
            if not backup.exists():
                im.save(backup)

            snij = trim.get(naam, 0)
            if snij:
                im = im.crop((0, 0, im.width, im.height - snij))

            bg = randkleur(im)

            # opvullen tot de doelverhouding, nooit bijsnijden
            if im.width / im.height < doel:
                nb, nh = int(round(im.height * doel)), im.height
            else:
                nb, nh = im.width, int(round(im.width / doel))
            canvas = Image.new("RGB", (nb, nh), bg)
            canvas.paste(im, ((nb - im.width) // 2, (nh - im.height) // 2))

            uit = canvas.resize((a.breedte, int(round(a.breedte / doel))), Image.LANCZOS)
            uit.save(pad, "PNG", optimize=True)
            print(f"   {naam:34} -> {uit.width}x{uit.height}"
                  + (f"  (onderkant {snij}px weg)" if snij else ""))

    print("\nKlaar. Originelen staan als *.orig.png in dezelfde map.")
    print("Die hoeven niet mee in git; voeg ze niet toe of gooi ze weg als je tevreden bent.")


if __name__ == "__main__":
    main()
