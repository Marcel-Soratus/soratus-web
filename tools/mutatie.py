"""De meetlaag voor mutatietesten. Eén plek, en de mutatielijsten staan er niet in.

════════════════════════════════════════════════════════════════════════════════════════════════════
LEES DIT VOORDAT JE HEM START
════════════════════════════════════════════════════════════════════════════════════════════════════

1. **Dit script schrijft in productiebestanden.** Niet in een kopie, niet in een worktree: in de
   bestanden zelf, in deze boom. Dat is de hele opzet — een mutatietest die op een kopie draait meet
   een kopie — en het is ook het risico.

2. **De enige bescherming is de `try/finally` in `voer_uit`.** Daar staat het terugzetten, met een
   assertie dat de inhoud byte-voor-byte gelijk is aan wat er stond. Breek je dit script af met
   Ctrl-C op het juiste moment, of valt je machine om, dan blijft er gemuteerde productiecode staan.
   Dat is in dit project één keer gebeurd (§36 van de fase-0-afwijkingen: "een afgebroken
   mutatieronde laat productiecode gemuteerd achter"), en het kostte een halve dag zoeken naar een
   fout die er niet was. Draai na een afgebroken ronde altijd `git status` en `git diff`.

3. **Draai hem niet met ongecommit werk in de boom** dat je niet kwijt wil. Het terugzetten leunt op
   wat er in het geheugen van dit proces staat, niet op git. Sterft het proces, dan is git je enige
   uitweg — en dan wil je dat er iets is om naar terug te gaan.

4. **Dit script is niet gereviewd.** Het is meetgereedschap dat tijdens het werk is gegroeid, uit
   drie losse scripts van drie sessies. Het staat in de repository omdat §36 van
   `docs/agent-portal/stand-van-zaken.md` ernaar verwijst, en een document dat naar een ongecommit
   bestand verwijst, verwijst naar niets. Behandel het als gereedschap en niet als productiecode:
   lees wat het doet voordat je erop vertrouwt.

5. **Andere sessies breken je meting.** Werkt er iemand anders in dezelfde boom, dan kan de build
   omvallen op zíjn bestand terwijl jouw mutatie er niets mee te maken heeft. Daar is dit script op
   ingericht — zie `meet()` — maar het beste is nog steeds: meld je venster en meet als je alleen
   bent.

════════════════════════════════════════════════════════════════════════════════════════════════════
WAT ER HIER STAAT EN WAT NIET
════════════════════════════════════════════════════════════════════════════════════════════════════

Hier staat de **meetlaag**: schrijven, terugzetten, bouwen, tests draaien, uitkomst uitlezen. Die is
gedeeld, want alle drie de eerdere versies hadden hem en alle drie hadden ze er een andere fout in.

Hier staat **geen mutatielijst**. Die zijn lane-specifiek en verouderen zodra de code schuift; een
gedeelde lijst wordt een lijst die niemand opruimt en die bij de volgende ronde half niet meer
aanslaat. Elke lane houdt zijn eigen bestand:

    tools/mutatie-support.py    de supportkant (§46)
    tools/mutatie-sprint.py     de sprintkant
    tools/mutatie-contract.py   de getalregel en het omgevingsbeheer (§23, §37)
    tools/mutatie-kosten.py     de kostenkant (§38-§41)
    tools/mutatie-urenapi.py    het urenendpoint (§26, §27)
    tools/mutatie-fase38.py     fase 3b

Zo'n bestand definieert zijn mutaties en roept `voer_uit` aan. Zie het voorbeeld onderaan.

════════════════════════════════════════════════════════════════════════════════════════════════════
DE DRIE MEETVALLEN DIE HIERIN ZIJN GEREPAREERD
════════════════════════════════════════════════════════════════════════════════════════════════════

Alle drie hebben ze op één dag een valse uitkomst opgeleverd. Ze staan bij hun eigen functie
uitgelegd; hier de korte versie, zodat niemand ze eruit "vereenvoudigt".

**Val 1 — "compileert niet" gelezen als een resultaat.** Een mutatie die de boom niet laat
compileren heeft niets gemeten. Erger: als de compileerfout in het bestand van een ándere sessie
staat, dan is jouw mutatie waarschijnlijk in orde en is de boom gebroken door iemand anders. Eén dag
leverde dat eenentwintig regels "compileert niet" op die geen van alle een meting waren. `meet()`
onderscheidt die twee nu.

**Val 2 — "compileert niet" gelezen als *groen*.** Een script dat alleen naar de uitvoer van
`dotnet test` keek en daar `error CS` in zocht, zag niets als de build al vóór het testen omviel — en
meldde dan de laatste bekende `Passed!`-regel. Dat is de klasse fout van §36: een groen signaal over
de verkeerde verzameling. `meet()` bouwt daarom apart en kijkt naar de returncode, niet naar tekst.

**Val 3 — het aantal in plaats van de lijst.** Een regex die een testnaam tot de eerste witruimte
pakte, gaf bij een theorie een lege lijst terug: `EenPaginaDieNietsRendert(pagina: typeof(...))`
draagt spaties. Dan lees je "1 rood" zonder te weten wélke, en in dit project heeft dat al een keer
een juiste diagnose teruggedraaid. De naam wordt nu tot ` [FAIL]` gelezen.
"""

from __future__ import annotations

import io
import re
import subprocess
import sys
import time
from pathlib import Path

# De wortel van de repository: de map met Soratus.slnx, gezocht vanaf dit bestand omhoog. Niet
# ingetypt, zodat het script ook werkt als de repository ergens anders staat.
WORTEL = next(
    (p for p in [Path(__file__).resolve().parent, *Path(__file__).resolve().parents]
     if (p / "Soratus.slnx").exists()),
    Path(__file__).resolve().parent.parent,
)

STANDAARDPROJECT = "Soratus.Portal.Tests/Soratus.Portal.Tests.csproj"


# ── Mutatievormen ───────────────────────────────────────────────────────────────────────────────


class Mutatie:
    """Eén mutatie: een naam en één of meer vervangingen.

    Meer dan één vervanging is nodig zodra een mutatie alleen als geheel compileert. Dat is geen
    theoretisch geval: bij de supportkant raakte "de vraag wordt pas na de eerstelijn vastgelegd"
    drie plekken in hetzelfde bestand, en in stukken toegepast compileerde de tussenstand niet — wat
    door een eerdere versie van dit script als groen werd gelezen (val 2). Een mutatie is dus één
    ondeelbare wijziging, en niet één regel.
    """

    def __init__(self, naam: str, delen: list[tuple[str, str, str]]):
        if not delen:
            raise ValueError(f"{naam}: een mutatie zonder vervangingen bestaat niet.")

        self.naam = naam
        self.delen = delen

    @property
    def paden(self) -> list[str]:
        """De bestanden die deze mutatie aanraakt, zonder dubbelen en in volgorde."""
        gezien: dict[str, None] = {}

        for pad, _, _ in self.delen:
            gezien.setdefault(pad, None)

        return list(gezien)


def enkel(naam: str, pad: str, oud: str, nieuw: str) -> Mutatie:
    """Een mutatie van één vervanging in één bestand."""
    return Mutatie(naam, [(pad, oud, nieuw)])


def samengesteld(naam: str, delen: list[tuple[str, str, str]]) -> Mutatie:
    """Een mutatie die alleen als geheel compileert."""
    return Mutatie(naam, delen)


def uit_viertallen(rijen) -> list[Mutatie]:
    """Zet de vorm `(naam, pad, oud, nieuw)` om naar mutaties.

    Bestaat omdat drie van de zes lanebestanden hun lijst al in die vorm hadden staan. Een lijst
    herschrijven om een hulpfunctie is werk zonder opbrengst.
    """
    return [enkel(*rij) for rij in rijen]


def uit_woordenboek(mutaties: dict) -> list[Mutatie]:
    """Zet de vorm `{naam: (pad, oud, nieuw)}` om naar mutaties."""
    return [enkel(naam, *waarde) for naam, waarde in mutaties.items()]


# ── Lezen en schrijven ──────────────────────────────────────────────────────────────────────────


def lees(pad: str) -> str:
    with io.open(WORTEL / pad, encoding="utf-8", newline="") as bestand:
        return bestand.read()


def schrijf(pad: str, inhoud: str, pogingen: int = 30) -> None:
    """Schrijft een bestand, met herhaling zolang het vergrendeld is.

    Op Windows houdt een virusscanner of een net afgelopen MSBuild een bestand soms een moment vast,
    en dan is `PermissionError` geen fout in de mutatie maar in de timing. Zonder deze herhaling
    breekt de ronde daar af — en een afgebroken ronde laat gemuteerde productiecode achter (§36).
    Dit is de plek waar dat niet mag gebeuren, dus het wachten staat hier en niet bij de aanroeper.
    """
    laatste: Exception | None = None

    for _ in range(pogingen):
        try:
            with io.open(WORTEL / pad, "w", encoding="utf-8", newline="") as bestand:
                bestand.write(inhoud)
            return
        except PermissionError as fout:
            laatste = fout
            time.sleep(1)

    raise RuntimeError(
        f"Kon {pad} na {pogingen} pogingen niet schrijven. Sluit de build af en kijk met "
        f"'git diff' of er nog gemuteerde code staat."
    ) from laatste


# ── Meten ───────────────────────────────────────────────────────────────────────────────────────


def draai(argumenten: list[str]) -> tuple[int, str]:
    klaar = subprocess.run(
        argumenten,
        cwd=WORTEL,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )

    return klaar.returncode, (klaar.stdout or "") + (klaar.stderr or "")


def foutbestanden(uitvoer: str) -> list[str]:
    """De bestanden waarin de compiler een fout meldt, als pad vanaf de wortel."""
    return sorted({
        gevonden.group(1).replace("\\", "/").split("Website/")[-1]
        for gevonden in re.finditer(r"([A-Za-z]:\\[^(\n]+)\(\d+,\d+\): error", uitvoer)
    })


def meet(
    project: str = STANDAARDPROJECT,
    filter: str | None = None,
    gemuteerd: list[str] | None = None,
) -> tuple[str, list[str]]:
    """Bouwt, draait de tests, en geeft de stand met de namen van de rode tests.

    Standen: ``groen``, ``rood``, ``compileert niet``, ``BOOM GEBROKEN DOOR ANDERE LANE``,
    ``rood zonder namen``.

    **Val 1 en 2 zitten hier.** Er wordt éérst gebouwd, in een eigen aanroep, en de uitkomst komt uit
    de returncode en niet uit een zoektocht naar "error CS" in de uitvoer van `dotnet test`. Valt de
    build om, dan is er niets gemeten — en of dat aan jou ligt, hangt af van wáár de fout staat:

      • staat er een fout in een bestand dat deze mutatie heeft aangeraakt, dan is de mutatie
        ongeldig ("compileert niet") en hoort hij herschreven te worden;
      • staat er geen enkele fout in die bestanden, dan heeft een andere sessie de boom gebroken en
        meet deze ronde niets. Dat is geen bevinding over jouw code, en het als "compileert niet"
        opschrijven levert een lijst op die eruitziet als eenentwintig metingen terwijl het er nul
        zijn.
    """
    code, uitvoer = draai(["dotnet", "build", project, "-v", "q", "--nologo"])

    if code != 0:
        bestanden = foutbestanden(uitvoer)
        eigen = bool(gemuteerd) and any(
            any(pad in bestand for pad in gemuteerd) for bestand in bestanden
        )

        return ("compileert niet" if eigen else "BOOM GEBROKEN DOOR ANDERE LANE"), bestanden

    argumenten = ["dotnet", "test", project, "--no-build", "--nologo", "-v", "q"]

    if filter:
        argumenten += ["--filter", filter]

    code, uitvoer = draai(argumenten)

    # Val 3. Tot aan " [FAIL]" en niet tot de eerste witruimte: een theorienaam draagt zijn
    # parameters mee — "EenPaginaDieNietsRendert(pagina: typeof(Soratus.Portal...Support))" — en daar
    # zitten spaties in. Een regex die dat mist meldt "rood" met een lege lijst, en dan lees je een
    # aantal in plaats van een lijst. Dat heeft in dit project al een keer een juiste diagnose
    # teruggedraaid, en het is geen inzicht maar toeval dat één van de drie scripts het goed had.
    rood = sorted(set(re.findall(r"\]\s+(.+?) \[FAIL\]", uitvoer)))

    if code != 0 and not rood:
        # Rood zonder namen is óók geen meting: dan is er iets misgegaan waar de test-uitvoer niets
        # over zegt (een gecrashte testhost, een filter dat nul tests selecteert). Apart melden, want
        # als "rood" opschrijven suggereert dat er een test is afgegaan.
        return "rood zonder namen", [r.strip() for r in uitvoer.splitlines() if "rror" in r][:4]

    return ("rood" if code != 0 else "groen"), rood


# ── De ronde ────────────────────────────────────────────────────────────────────────────────────


def voer_uit(
    mutaties: list[Mutatie],
    project: str = STANDAARDPROJECT,
    filter: str | None = None,
    alleen: list[str] | None = None,
    nulmeting: bool = True,
) -> int:
    """Draait de mutaties één voor één en geeft aan het eind het verslag.

    `alleen` filtert op het begin van de naam, zodat één mutatie los te draaien is.
    `nulmeting` bouwt en meet eerst zonder mutatie; is die niet groen, dan stopt de ronde. Een
    mutatieronde op een rode boom meet niets: elke mutatie lijkt dan iets rood te maken.
    """
    if nulmeting:
        print("Nulmeting...", flush=True)
        stand, rood = meet(project, filter)

        if stand != "groen":
            print(f"De nulmeting is niet groen ({stand}): {rood}")
            print("Een mutatieronde op een rode boom meet niets. Eerst repareren, dan meten.")
            return 1

        print(f"Nulmeting groen ({len(rood)} rood).", flush=True)

    verslag: list[tuple[str, str, list[str]]] = []

    for mutatie in mutaties:
        if alleen and not any(mutatie.naam.startswith(k) for k in alleen):
            continue

        # Alles lezen vóór er iets wordt geschreven, zodat een mutatie die halverwege zijn anker niet
        # vindt geen half gemuteerd bestand achterlaat.
        origineel = {pad: lees(pad) for pad in mutatie.paden}
        gemuteerd = dict(origineel)
        klopt = True

        for pad, oud, nieuw in mutatie.delen:
            aantal = gemuteerd[pad].count(oud)

            if aantal != 1:
                # Niet één keer voorkomen is geen mutatie maar een verouderd anker. Bij nul is er
                # niets te vervangen; bij meer dan één weet je niet welke je hebt geraakt, en dan is
                # de uitkomst niet te herhalen.
                verslag.append((
                    mutatie.naam,
                    "NIET TOEGEPAST",
                    [f"anker komt {aantal}x voor in {pad}: {oud.strip()[:70]}"],
                ))
                klopt = False
                break

            gemuteerd[pad] = gemuteerd[pad].replace(oud, nieuw)

        if not klopt:
            print(f"!! {mutatie.naam}\n   {verslag[-1][2][0]}", flush=True)
            continue

        try:
            for pad, inhoud in gemuteerd.items():
                schrijf(pad, inhoud)

            stand, rood = meet(project, filter, gemuteerd=mutatie.paden)
        finally:
            # Dit is de enige bescherming, en daarom staat het in een finally en niet erna. De
            # assertie hoort erbij: terugzetten zonder controleren is een aanname, en een mislukte
            # terugzetting is precies de fout die je pas een halve dag later vindt.
            for pad, inhoud in origineel.items():
                schrijf(pad, inhoud)

                if lees(pad) != inhoud:
                    raise RuntimeError(
                        f"TERUGZETTEN MISLUKT voor {pad}. Er staat nu gemuteerde productiecode in de "
                        f"boom. Kijk met 'git diff {pad}' en zet het met de hand terug voordat je "
                        f"iets anders doet."
                    )

        verslag.append((mutatie.naam, stand, rood))
        print(f"-- {mutatie.naam}\n   {stand}", flush=True)

        for naam in rood:
            print(f"   rood: {naam}", flush=True)

    print("\n===== VERSLAG =====")

    for naam, stand, rood in verslag:
        markering = "STIL" if stand == "groen" else ("!!  " if stand != "rood" else "    ")
        print(f"{markering} {naam}")
        print(f"      {stand}")

        for regel in rood:
            print(f"      {regel}")

    stil = [n for n, s, _ in verslag if s == "groen"]
    geen_meting = [n for n, s, _ in verslag if s not in ("groen", "rood")]

    print(
        f"\n{len(verslag)} mutaties: "
        f"{len(verslag) - len(stil) - len(geen_meting)} rood, "
        f"{len(stil)} stil, "
        f"{len(geen_meting)} geen meting."
    )

    if stil:
        print("\nStil is een bevinding en geen resultaat. Elke stille mutatie is óf een gat in de "
              "dekking, óf een bewuste keuze die in het rapport hoort te staan met de reden erbij.")

    if geen_meting:
        print("\nDeze mutaties hebben niets gemeten en horen opnieuw te draaien:")
        for naam in geen_meting:
            print(f"  {naam}")

    return 0


def main() -> int:
    print(__doc__)
    print(
        "Dit bestand is de meetlaag en heeft geen eigen mutatielijst.\n"
        "Draai het lanebestand dat bij je werk hoort, bijvoorbeeld:\n"
        "\n"
        "    python tools/mutatie-support.py          alle mutaties van die lane\n"
        "    python tools/mutatie-support.py M4 M5    alleen deze twee\n"
        "\n"
        "Een lanebestand ziet er zo uit:\n"
        "\n"
        "    import sys\n"
        "    from pathlib import Path\n"
        "    sys.path.insert(0, str(Path(__file__).resolve().parent))\n"
        "    import mutatie\n"
        "\n"
        "    MUTATIES = [\n"
        "        mutatie.enkel('M1  wat er stuk gaat', 'pad/naar/Bestand.cs', 'oud', 'nieuw'),\n"
        "        mutatie.samengesteld('M2  alleen als geheel', [\n"
        "            ('pad/naar/Bestand.cs', 'oud a', 'nieuw a'),\n"
        "            ('pad/naar/Bestand.cs', 'oud b', 'nieuw b'),\n"
        "        ]),\n"
        "    ]\n"
        "\n"
        "    if __name__ == '__main__':\n"
        "        raise SystemExit(mutatie.voer_uit(MUTATIES, filter='FullyQualifiedName~...',\n"
        "                                          alleen=sys.argv[1:] or None))\n"
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
