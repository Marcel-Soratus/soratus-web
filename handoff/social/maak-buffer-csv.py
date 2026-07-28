#!/usr/bin/env python
"""
Vult het Buffer-importtemplate met de acht campagneposts.

Buffer-eisen die hier in zitten:
  - kolommen exact: Text, Image URL, Tags, Posting Time
  - Image URL moet een publieke link zijn die eindigt op .jpg/.jpeg/.png en
    onder 5MB blijft. Daarom staan de beelden op soratus.com/social/ en niet
    lokaal; een pad op je schijf laadt Buffer niet.
  - Posting Time in YYYY-MM-DD HH:MM, 24-uurs. Leeg = in de wachtrij.
  - Tags moeten al in Buffer bestaan en zijn case-sensitive. Ze staan hier leeg,
    want een tag die niet bestaat laat de import stuklopen. Vul ze naderhand in
    Buffer aan als je ze gebruikt.
  - Opgeslagen als UTF-8 met BOM ("CSV UTF-8"), zodat accenten en het euroteken
    goed overkomen.

De posttekst bevat witregels. Dat is geldige CSV binnen aanhalingstekens en het
is belangrijk voor de leesbaarheid op LinkedIn. Mocht Buffer die regelafbreking
platslaan, plak dan de losse bestanden uit posts/ met de hand.

    python handoff/social/maak-buffer-csv.py
"""

import csv
from datetime import date, timedelta
from pathlib import Path

HIER = Path(__file__).parent
POSTS = HIER / "posts"
UIT = HIER / "buffer-import.csv"

BASIS = "https://soratus.com"
TIJD = "08:15"          # voor de eerste vergadering
START = date(2026, 8, 4)  # eerste dinsdag na oplevering

# per post: bestand, beeld-URL, doel-URL voor de eerste comment
PLAN = [
    ("01-jaarverslag-ton.txt",   f"{BASIS}/social/01-ton-licentie.png",
     "/cases/snelstart-jaarverslag-agent"),
    ("02-declaraties-400uur.txt", f"{BASIS}/social/02-declaraties-carrousel-slide1.png",
     "/cases/snelstart-declaraties-matchen"),
    ("03-data-bezwaar.txt",      f"{BASIS}/social/03-data-veilig.png", "/"),
    ("04-dag-15.txt",            f"{BASIS}/social/04-dag-15.png", "/partner"),
    ("05-80-procent.txt",        f"{BASIS}/img/cases/declaraties-agent.png",
     "/cases/snelstart-declaraties-matchen"),
    ("06-14-dagen-hoe.txt",      f"{BASIS}/social/06-vier-stappen.png", "/"),
    ("07-mkb-bezwaar.txt",       f"{BASIS}/social/07-ook-mkb.png", "/cases"),
    ("08-visie-voorbij.txt",     f"{BASIS}/social/08-voorbij.png", "/partner"),
]


def momenten(n):
    """Dinsdag en donderdag, vier weken achter elkaar."""
    uit = []
    for w in range(n // 2 + 1):
        di = START + timedelta(weeks=w)
        uit.append(di)
        uit.append(di + timedelta(days=2))
    return uit[:n]


def main():
    tijden = momenten(len(PLAN))
    rijen = []
    for (bestand, beeld, _doel), dag in zip(PLAN, tijden):
        tekst = (POSTS / bestand).read_text(encoding="utf-8").strip()
        rijen.append([tekst, beeld, "", f"{dag} {TIJD}"])

    # newline="" is verplicht, anders krijgt Windows dubbele regeleinden
    with UIT.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f, quoting=csv.QUOTE_ALL)
        w.writerow(["Text", "Image URL", "Tags", "Posting Time"])
        w.writerows(rijen)

    print(f"{UIT}  ({len(rijen)} posts)")
    for (bestand, beeld, doel), dag in zip(PLAN, tijden):
        print(f"  {dag} {TIJD}  {bestand:26} {beeld.rsplit('/', 1)[1]:38} comment -> {doel}")

    langste = max(len(r[0]) for r in rijen)
    print(f"\nlangste posttekst: {langste} tekens (LinkedIn staat 3000 toe)")


if __name__ == "__main__":
    main()
