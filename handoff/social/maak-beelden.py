#!/usr/bin/env python
"""
Maakt de LinkedIn-beelden voor de campagne.

Levert per post een portretbeeld van 1080x1350 (4:5). Die verhouding neemt de
meeste ruimte in de tijdlijn, wat op LinkedIn meer aandacht oplevert dan een
liggend beeld.

De merkfonts (Space Grotesk, JetBrains Mono) staan niet lokaal geinstalleerd,
dus de beelden gebruiken Arial. Kleuren, logo en opmaak komen wel uit de
huisstijl. Wil je het typografisch kloppend hebben, installeer dan Space Grotesk
en pas SANS/SANS_BOLD hieronder aan.

Gebruik, vanuit de root van de repo:

    python handoff/social/maak-beelden.py
"""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

UIT = Path("handoff/social/img")
LOGO = Path("Soratus.Web/wwwroot/brand/logo-square-darkbg-256.png")

B, H = 1080, 1350

# huisstijl uit wwwroot/css/tokens.css
BG        = (19, 22, 42)
BG2       = (26, 30, 54)
INK       = (244, 245, 251)
INK_DIM   = (168, 174, 198)
INK_MUTE  = (107, 114, 144)
GROEN     = (52, 226, 122)
GROEN2    = (157, 247, 189)
BLAUW     = (43, 91, 255)
BLAUW2    = (92, 130, 255)
WARN      = (255, 216, 107)

F = "C:/Windows/Fonts/arial.ttf"
FB = "C:/Windows/Fonts/arialbd.ttf"


def font(grootte, vet=False):
    return ImageFont.truetype(FB if vet else F, grootte)


def achtergrond():
    """Donkere basis met een diagonale verloopgloed, zoals de site."""
    im = Image.new("RGB", (B, H), BG)
    gloed = Image.new("RGB", (B, H), BG)
    d = ImageDraw.Draw(gloed)
    for i in range(H):
        t = i / H
        d.line([(0, i), (B, i)], fill=(
            int(BG[0] + (BG2[0] - BG[0]) * t),
            int(BG[1] + (BG2[1] - BG[1]) * t),
            int(BG[2] + (BG2[2] - BG[2]) * t)))
    im = Image.blend(im, gloed, 0.9)

    # subtiel raster, herkenbaar van de site
    d = ImageDraw.Draw(im, "RGBA")
    for x in range(0, B, 60):
        d.line([(x, 0), (x, H)], fill=(255, 255, 255, 8))
    for y in range(0, H, 60):
        d.line([(0, y), (B, y)], fill=(255, 255, 255, 8))
    return im


def verloopvlak(w, h, van=BLAUW, naar=GROEN):
    """Horizontaal verloop, voor accentbalken en verlooptekst."""
    g = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(g)
    for x in range(w):
        t = x / max(1, w - 1)
        d.line([(x, 0), (x, h)], fill=(
            int(van[0] + (naar[0] - van[0]) * t),
            int(van[1] + (naar[1] - van[1]) * t),
            int(van[2] + (naar[2] - van[2]) * t)))
    return g


def verlooptekst(im, xy, tekst, f, van=BLAUW2, naar=GROEN):
    """Tekst gevuld met een verloop, via een masker."""
    tijdelijk = Image.new("L", im.size, 0)
    ImageDraw.Draw(tijdelijk).text(xy, tekst, font=f, fill=255)
    vlak = verloopvlak(im.size[0], im.size[1], van, naar)
    im.paste(vlak, (0, 0), tijdelijk)


def wikkel(d, tekst, f, breedte):
    """Regels afbreken op werkelijke pixelbreedte."""
    woorden, regels, huidig = tekst.split(), [], ""
    for w in woorden:
        kandidaat = (huidig + " " + w).strip()
        if d.textlength(kandidaat, font=f) <= breedte:
            huidig = kandidaat
        else:
            if huidig:
                regels.append(huidig)
            huidig = w
    if huidig:
        regels.append(huidig)
    return regels


def logo_op(im, x=72, y=H - 130, hoog=44):
    if not LOGO.exists():
        return
    with Image.open(LOGO) as l:
        l = l.convert("RGBA")
        w = int(l.width * hoog / l.height)
        im.paste(l.resize((w, hoog), Image.LANCZOS), (x, y), l.resize((w, hoog), Image.LANCZOS))
    ImageDraw.Draw(im).text((x + 58, y + 12), "soratus.com", font=font(22), fill=INK_MUTE)


def voettekst(im, tekst):
    ImageDraw.Draw(im).text((B - 72, H - 118), tekst, font=font(22), fill=INK_MUTE, anchor="ra")


def vinkje(d, x, y, maat=30, kleur=GROEN, dikte=5):
    """Vinkje als lijnen getekend, niet als tekst. Arial mist de glyph U+2713 en
       zou een leeg blokje tonen."""
    d.line([(x, y + maat * 0.55), (x + maat * 0.36, y + maat * 0.92)],
           fill=kleur, width=dikte)
    d.line([(x + maat * 0.36, y + maat * 0.92), (x + maat, y + maat * 0.12)],
           fill=kleur, width=dikte)


# ─────────────────────────────────────────────────────────────────────────────

def cijferkaart(naam, label, cijfer, onder, voet, cijferkleur=None):
    """Groot getal als hoofdboodschap. Werkt het sterkst in de tijdlijn."""
    im = achtergrond()
    d = ImageDraw.Draw(im)

    # het cijfer zo groot maken als past, dan het hele blok verticaal centreren:
    # anders blijft de onderste helft van een 4:5-portret leeg
    grootte = 210
    f_cijfer = font(grootte, True)
    while d.textlength(cijfer, font=f_cijfer) > B - 144 and grootte > 90:
        grootte -= 6
        f_cijfer = font(grootte, True)

    f_onder = font(54, True)
    onderregels = wikkel(d, onder, f_onder, B - 144)
    hoogte = 6 + 40 + 44 + int(grootte * 1.15) + 26 + len(onderregels) * 70
    y = (H - hoogte) // 2 - 40

    im.paste(verloopvlak(160, 6), (72, y))
    y += 40
    d.text((72, y), label.upper(), font=font(26, True), fill=GROEN)
    y += 60

    if cijferkleur:
        d.text((72, y), cijfer, font=f_cijfer, fill=cijferkleur)
    else:
        verlooptekst(im, (72, y), cijfer, f_cijfer)
    y += int(grootte * 1.15) + 26

    for regel in onderregels:
        d.text((72, y), regel, font=f_onder, fill=INK)
        y += 70

    logo_op(im)
    voettekst(im, voet)
    im.save(UIT / naam, "PNG", optimize=True)
    return naam


def citaatkaart(naam, kop, regels, voet):
    """Kop plus opsomming. Voor bezwaren en uitleg."""
    im = achtergrond()
    d = ImageDraw.Draw(im)

    # hele blok verticaal centreren, anders blijft de onderste helft leeg
    f_kop = font(76, True)
    kopregels = wikkel(d, kop, f_kop, B - 144)
    f_r = font(40)
    itemregels = [wikkel(d, r, f_r, B - 200) for r in regels]
    hoogte = 6 + 44 + len(kopregels) * 92 + 40 + sum(len(x) * 52 + 26 for x in itemregels)
    y = (H - hoogte) // 2 - 30

    im.paste(verloopvlak(160, 6), (72, y))
    y += 50

    for regel in kopregels:
        d.text((72, y), regel, font=f_kop, fill=INK)
        y += 92

    y += 40
    for r in regels:
        vinkje(d, 74, y + 6, 30)
        for i, regel in enumerate(wikkel(d, r, f_r, B - 200)):
            d.text((128, y), regel, font=f_r, fill=INK_DIM)
            y += 52
        y += 26

    logo_op(im)
    voettekst(im, voet)
    im.save(UIT / naam, "PNG", optimize=True)
    return naam


def stappenkaart(naam, kop, stappen, voet):
    """Genummerde stappen, voor het proces en het maandgesprek."""
    im = achtergrond()
    d = ImageDraw.Draw(im)

    im.paste(verloopvlak(160, 6), (72, 150))

    f_kop = font(72, True)
    y = 200
    for regel in wikkel(d, kop, f_kop, B - 144):
        d.text((72, y), regel, font=f_kop, fill=INK)
        y += 88

    y += 46
    for i, (titel, uitleg) in enumerate(stappen, 1):
        d.rounded_rectangle([72, y, B - 72, y + 148], radius=18,
                            fill=(*BG2, 255), outline=(255, 255, 255, 26), width=1)
        d.text((104, y + 26), f"0{i}", font=font(28, True), fill=GROEN)
        d.text((104, y + 62), titel, font=font(42, True), fill=INK)
        d.text((104, y + 110), uitleg, font=font(30), fill=INK_MUTE)
        y += 168

    logo_op(im)
    voettekst(im, voet)
    im.save(UIT / naam, "PNG", optimize=True)
    return naam


def main():
    UIT.mkdir(parents=True, exist_ok=True)
    gemaakt = []

    # Post 1 · jaarverslag
    gemaakt.append(cijferkaart(
        "01-ton-licentie.png", "Accountancy · bewijs", "\u20ac 100.000",
        "aan licentiekosten die niet meer nodig waren",
        "case: jaarverslag-agent op SnelStart"))

    # Post 2 · declaraties
    gemaakt.append(cijferkaart(
        "02-400-uur.png", "Zorg · bewijs", "400 uur",
        "per jaar minder uitzoekwerk bij de klant",
        "case: declaratie-agent op SnelStart"))

    # Post 3 · data-bezwaar
    gemaakt.append(citaatkaart(
        "03-data-veilig.png", "\u201cOnze data gaat niet naar een AI.\u201d Terecht.",
        ["EU-hosted, geen uitzonderingen",
         "Nooit training op jouw data",
         "ISO 27001-gecertificeerde hosting",
         "Audit-trail van elke actie",
         "Verwerkersovereenkomst art. 28"],
        "zo regelen we het bij elk project"))

    # Post 4 · dag 15
    gemaakt.append(stappenkaart(
        "04-dag-15.png", "Iedereen verkoopt 14 dagen. Wij praten over dag 15.",
        [("Terugblik", "Wat is opgeleverd, en wat kon beter?"),
         ("Signalering", "Waar zit winst, waar zit risico?"),
         ("Werkagenda", "Wat pakken we de komende periode op?"),
         ("Jouw keuze", "Jij bepaalt waar de uren naartoe gaan.")],
        "het partnerplan \u00b7 soratus.com/partner"))

    # Post 5 · de 80 procent-grens
    gemaakt.append(cijferkaart(
        "05-80-procent.png", "Vertrouwen \u00b7 bewijs", "6 van 8",
        "zelf afgehandeld. De andere 2 kwamen terug met een vraag.",
        "onder 80% zekerheid handelt de agent niet"))

    # Post 6 · hoe we werken
    gemaakt.append(stappenkaart(
        "06-vier-stappen.png", "Van briefing tot productie in vier stappen.",
        [("Dag 1", "Eén gesprek van 45 minuten. Geen powerpoint."),
         ("Dag 2 tot 3", "Blauwdruk, prototype en een vaste prijs."),
         ("Dag 4 tot 14", "Bouwen in de open. Je kijkt dagelijks mee."),
         ("Dag 15 en verder", "Live, en wij blijven meelopen.")],
        "vaste prijs \u00b7 het risico ligt bij ons"))

    # Post 7 · MKB-bezwaar
    gemaakt.append(citaatkaart(
        "07-ook-mkb.png", "\u201cDat soort AI is voor grote bedrijven.\u201d Omgekeerd.",
        ["Tweede Kamer en Eerste Kamer",
         "UWV en Brunel",
         "En: een zorgpraktijk, 400 uur per jaar",
         "En: een accountantskantoor, een ton aan licentie",
         "Zelfde techniek, directer effect"],
        "de impact is bij tien mensen groter dan bij duizend"))

    # Post 8 · visie
    gemaakt.append(citaatkaart(
        "08-voorbij.png", "Software laten bouwen is voorbij. Letterlijk.",
        ["Wat mogelijk is schuift elk kwartaal op",
         "Een jaarplan van januari klopt in juni niet",
         "Wat je maakt is een startpunt, geen product",
         "Een agent past zijn gedrag aan, een formulier niet",
         "Dus stopt het werk niet bij de oplevering"],
        "soratus \u00b7 time changing software"))

    print(f"{len(gemaakt)} beelden gemaakt in {UIT}/ (1080x1350):")
    for n in gemaakt:
        print("  ", n)


if __name__ == "__main__":
    main()
