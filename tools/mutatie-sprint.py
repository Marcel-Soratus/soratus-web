#!/usr/bin/env python3
"""Mutatietesten voor de sprintlane (fase 5, §3.4).

Elke mutatie breekt de productiecode op één plek zo dat er een test rood hóórt te worden.
Het script past de mutatie toe, bouwt, draait de sprinttests, zet terug, en meldt per mutatie
of er iets rood werd én welke tests dat waren.

De mutaties die *niets* rood maken zijn de opbrengst: dat zijn de gaten.

Gebruik:
    python tools/mutatie-sprint.py            # alle mutaties
    python tools/mutatie-sprint.py 3 7 12     # alleen deze nummers
"""

from __future__ import annotations

import io
import re
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

WORTEL = Path(__file__).resolve().parent.parent

# Alleen de sprintlane; het draaien van de hele suite per mutatie zou dertig keer twintig
# seconden extra kosten zonder iets toe te voegen. De brede meting doen we één keer aan het eind.
FILTER = (
    "FullyQualifiedName~Sprint|FullyQualifiedName~Devopsbord"
    "|FullyQualifiedName~Klantdocumentvelden|FullyQualifiedName~Contracteiland"
    "|FullyQualifiedName~ContractZichtbaarheid|FullyQualifiedName~NieuweKlantFormulier"
    "|FullyQualifiedName~Bordinvoer|FullyQualifiedName~Omgevingsblokrendermode"
    "|FullyQualifiedName~SprintTextTests"
)

# (nummer, bestand, zoek, vervang, wat de mutatie kapot maakt)
MUTATIES: list[tuple[int, str, str, str, str]] = [
    # ── SprintSelection: de harde regel ────────────────────────────────────────────────
    (1, "Soratus.Portal/Sprints/SprintSelection.cs",
     "today <= iteration.Finish!.Value",
     "today < iteration.Finish!.Value",
     "de laatste dag van een sprint valt er niet meer in"),
    (2, "Soratus.Portal/Sprints/SprintSelection.cs",
     "iteration.Start!.Value <= today",
     "iteration.Start!.Value < today",
     "de eerste dag van een sprint valt er niet meer in"),
    (3, "Soratus.Portal/Sprints/SprintSelection.cs",
     "_ => new SprintChoice(SprintState.Ambiguous, null, undated, containing, dated.Length),",
     "_ => new SprintChoice(SprintState.Current, containing[0], undated, [], dated.Length),",
     "bij overlappende periodes wordt stil de eerste gekozen"),
    (4, "Soratus.Portal/Sprints/SprintSelection.cs",
     "return new SprintChoice(SprintState.NoDatedIterations, null, undated, [], 0);",
     "return new SprintChoice(SprintState.NoIterations, null, undated, [], 0);",
     "iteraties zonder datums zijn niet meer van geen iteraties te onderscheiden"),
    (5, "Soratus.Portal/Sprints/SprintDocuments.cs",
     "public bool IsDated => Start is not null && Finish is not null;",
     "public bool IsDated => Start is not null || Finish is not null;",
     "een iteratie met maar één datum geldt als gedateerd"),
    (6, "Soratus.Portal/Sprints/SprintSelection.cs",
     "1 => new SprintChoice(SprintState.Current, containing[0], undated, [], dated.Length),",
     "1 => new SprintChoice(SprintState.Current, containing[0], [], [], dated.Length),",
     "de iteraties zonder datums verdwijnen bij een gezonde sprint"),

    # ── SprintTally: een som bestaat dan en slechts dan als er iets is ─────────────────
    (7, "Soratus.Portal/Sprints/SprintTally.cs",
     "        return som;",
     "        return som ?? 0m;",
     "een ontbrekende som wordt nul"),
    (8, "Soratus.Portal/Sprints/SprintTally.cs",
     "var werk = items.Where(item => item.Stage != WorkItemStage.Removed).ToArray();",
     "var werk = items.ToArray();",
     "verwijderde items tellen mee in het aantal en in de sommen"),
    (9, "Soratus.Portal/Sprints/SprintTally.cs",
     "werk.Count(item => item.Stage == WorkItemStage.Completed),",
     "werk.Count(item => item.Stage is WorkItemStage.Completed or WorkItemStage.Resolved),",
     "opgelost telt als afgerond"),
    (10, "Soratus.Portal/Sprints/SprintTally.cs",
     "        return string.Equals(item.State, woord, StringComparison.OrdinalIgnoreCase)\n"
     "            || item.Tags.Any(tag => string.Equals(tag, woord, StringComparison.OrdinalIgnoreCase));",
     "        return item.Tags.Any(tag => string.Equals(tag, woord, StringComparison.OrdinalIgnoreCase));",
     "een state die Blocked heet geldt niet meer als geblokkeerd"),
    (11, "Soratus.Portal/Sprints/SprintTally.cs",
     "item.Tags.Any(tag => string.Equals(tag, woord, StringComparison.OrdinalIgnoreCase))",
     "item.Tags.Any(tag => tag.Contains(woord, StringComparison.OrdinalIgnoreCase))",
     "de blokkadetag wordt op een deel van de tekst vergeleken"),
    (12, "Soratus.Portal/Sprints/SprintTally.cs",
     "        if (namen.Length == 0)\n        {\n            return WorkItemOrigin.Unknown;\n        }",
     "        if (namen.Length == 0)\n        {\n            return WorkItemOrigin.Manual;\n        }",
     "zonder agentidentiteit heet elk item handmatig"),
    (13, "Soratus.Portal/Sprints/SprintTally.cs",
     "var identiteit = item.CreatedByUniqueName ?? item.CreatedByName;",
     "var identiteit = item.CreatedByName ?? item.CreatedByUniqueName;",
     "de weergavenaam gaat vóór het adres bij de herkomst"),

    # ── DevOpsScope: de twee kanten van de controle ────────────────────────────────────
    (14, "Soratus.Portal/Sprints/DevOpsScope.cs",
     "if (string.IsNullOrWhiteSpace(text) || Validate(text) is not null)",
     "if (string.IsNullOrWhiteSpace(text))",
     "de ontleding controleert niets meer (gat 1 van punt 41)"),
    (15, "Soratus.Portal/Sprints/DevOpsScope.cs",
     "if (parts.Length != 3 || parts.Any(part => part.Length == 0))",
     "if (parts.Length < 3 || parts.Any(part => part.Length == 0))",
     "een vierde segment wordt stil genegeerd"),
    (16, "Soratus.Portal/Sprints/DevOpsScope.cs",
     "'\\\\', ':', '<', '>', '|', '?', '*'",
     "'\\\\', ':', '<', '>', '|', '*'",
     "een vraagteken mag in een project- of teamnaam"),
    (17, "Soratus.Portal/Sprints/DevOpsScope.cs",
     "var geldig = name.All(teken => char.IsAsciiLetterOrDigit(teken) || teken == '-')",
     "var geldig = name.All(teken => char.IsAsciiLetterOrDigit(teken) || teken == '-' || teken == '_')",
     "een onderstrepingsteken mag in een organisatienaam"),
    (18, "Soratus.Portal/Sprints/DevOpsScope.cs",
     "public string Path => $\"/{Organization}/{Project}/{Team}\";",
     "public string Path => $\"/{Organization}/{Project}\";",
     "het pad laat het team weg"),

    # ── DevOpsSprintClient: de lezing ──────────────────────────────────────────────────
    (19, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "            && element.TryGetDecimal(out var value)\n                ? value\n                : null;",
     "            && element.TryGetDecimal(out var value)\n                ? value\n                : 0m;",
     "een ontbrekend urenveld wordt nul"),
    (20, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "if (raw.Count != numbers.Length)",
     "if (raw.Count > numbers.Length)",
     "een batch met te weinig items komt erdoor"),
    (21, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "            || stage == WorkItemStage.Unknown)\n",
     "            )\n",
     "een state met een onbekende categorie komt als Unknown door"),
    (22, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     '        "Resolved" => WorkItemStage.Resolved,',
     '        "Resolved" => WorkItemStage.Completed,',
     "de categorie Resolved wordt afgerond"),
    (23, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "private static string Escape(string segment) => Uri.EscapeDataString(segment);",
     "private static string Escape(string segment) => segment;",
     "padsegmenten worden niet meer ge-escaped"),
    (24, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "            var tekst = element.GetString();\n"
     "            return (string.IsNullOrWhiteSpace(tekst) ? null : tekst, null);",
     "            return (null, null);",
     "een identiteit als tekenreeks levert geen naam op"),
    (25, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "                    is HttpStatusCode.TooManyRequests\n                    or HttpStatusCode.RequestTimeout",
     "                    is HttpStatusCode.TooManyRequests\n                    or HttpStatusCode.NotFound\n                    or HttpStatusCode.RequestTimeout",
     "een 404 wordt herhaald (de kostenregel op de verkeerde API toegepast)"),
    (26, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "if (numbers.Length > _options.MaxWorkItems)",
     "if (numbers.Length > int.MaxValue)",
     "de grens op het aantal work items verdwijnt"),
    (27, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "moment is { } value ? DateOnly.FromDateTime(value.UtcDateTime) : null;",
     "moment is { } value ? DateOnly.FromDateTime(value.LocalDateTime) : null;",
     "een iteratiedatum wordt naar de lokale zone omgerekend"),
    (28, "Soratus.Portal/Sprints/DevOpsSprintClient.cs",
     "request.Headers.Authorization = new AuthenticationHeaderValue(\"Bearer\", token.Token);",
     "// mutatie: geen autorisatieheader",
     "het token gaat niet mee"),

    # ── SprintCollector: wat er wordt weggeschreven en wat niet ────────────────────────
    (29, "Soratus.Portal/Sprints/SprintCollector.cs",
     "        if (answer.Kind == SprintAnswerKind.NotAvailable)\n        {",
     "        if (false)\n        {",
     "een mislukte lezing wordt toch weggeschreven (punt 39)"),
    (30, "Soratus.Portal/Sprints/SprintCollector.cs",
     "    internal async Task<int> RunAsync(CancellationToken cancellationToken)\n    {\n        if (!_options.Enabled)\n        {\n            return 0;\n        }\n",
     "    internal async Task<int> RunAsync(CancellationToken cancellationToken)\n    {\n",
     "de vlag geldt niet meer in RunAsync (gat 3 van punt 41)"),
    (31, "Soratus.Portal/Sprints/SprintCollector.cs",
     "        var today = DateOnly.FromDateTime(\n            TimeZoneInfo.ConvertTime(now, PortalTimeZone.Display).DateTime);",
     "        var today = DateOnly.FromDateTime(now.UtcDateTime);",
     "de dag komt uit UTC in plaats van uit de Nederlandse zone"),
    (32, "Soratus.Portal/Sprints/SprintCollector.cs",
     "            if (await SkipAsync(target.CustomerId, cancellationToken).ConfigureAwait(false))\n            {\n                continue;\n            }\n",
     "",
     "de versheidscontrole verdwijnt"),
    (33, "Soratus.Portal/Sprints/SprintCollector.cs",
     "                    choice.State,\n                    scope.Path,",
     "                    choice.State,\n                    scope.ProjectPath,",
     "de weggeschreven scope laat het team weg"),

    # ── SprintViews: de projectie en de rolgrens ───────────────────────────────────────
    (34, "Soratus.Portal/Views/SprintViews.cs",
     "document?.State ?? SprintState.Unknown;",
     "document?.State ?? SprintState.NoIterations;",
     "geen document wordt 'dit bord heeft geen iteraties'"),
    (35, "Soratus.Portal/Views/SprintViews.cs",
     "        string.IsNullOrWhiteSpace(scope) ? SprintNotice.NoScopeConfigured\n        : DevOpsScope.TryParse(scope, out _) ? null",
     "        DevOpsScope.Validate(scope) is null ? null",
     "een ontbrekend bord meldt zich niet meer als 'niet ingericht'"),
    (36, "Soratus.Portal/Views/SprintViews.cs",
     "        CreatedByAddress = item.CreatedByUniqueName,",
     "        CreatedByAddress = null,",
     "het adres van de aanmaker verdwijnt van het operatorscherm"),
    (37, "Soratus.Portal/Views/SprintViews.cs",
     "            UndatedNotice = document?.Undated.Count > 0 ? SprintNotice.Undated : null,\n"
     "            ReadOnlyNotice = SprintNotice.ReadOnly,\n"
     "            SnapshotNotice = SprintNotice.Snapshot,\n"
     "            HoursNotice = SprintNotice.HoursUnknown,\n        };\n    }\n\n    /// <inheritdoc />",
     "            UndatedNotice = null,\n"
     "            ReadOnlyNotice = SprintNotice.ReadOnly,\n"
     "            SnapshotNotice = SprintNotice.Snapshot,\n"
     "            HoursNotice = SprintNotice.HoursUnknown,\n        };\n    }\n\n    /// <inheritdoc />",
     "de klant hoort niet meer dat er werk buiten elke periode valt"),
    (38, "Soratus.Portal/Views/SprintViews.cs",
     "        DateOnly.TryParseExact(\n            text,\n            \"yyyy-MM-dd\",",
     "        DateOnly.TryParse(\n            text,",
     "een datum uit de opslag wordt cultuurafhankelijk gelezen"),

    # ── Het klantdocument: het veld dat door vier lagen moet ───────────────────────────
    (39, "Soratus.Portal/Data/CosmosPortalDataStore.cs",
     "            DevOpsScope = Clean(edit.DevOpsScope),",
     "",
     "het bewaren laat het DevOps-bord vallen (gat 4 van punt 41)"),
    (40, "Soratus.Portal/Data/CosmosPortalDataStore.cs",
     "            DevOpsScope = Clean(request.DevOpsScope),",
     "",
     "het aanmaken legt het DevOps-bord niet vast"),
    (41, "Soratus.Portal/Data/PortalEdits.cs",
     "            : Data.AzureScope.Validate(AzureScope) ?? Sprints.DevOpsScope.Validate(DevOpsScope);",
     "            : Data.AzureScope.Validate(AzureScope);",
     "de bewerking controleert het bord niet meer"),
    (42, "Soratus.Portal/Views/ContractViews.cs",
     "            DevOpsScope = customer?.DevOpsScope,",
     "            DevOpsScope = null,",
     "het contractscherm leest het bord niet meer uit de opslag"),

    # ── Tweede ronde: bijten de nieuwe tests, en wat is er nog niet gedekt ─────────────
    (43, "Soratus.Portal/Components/Pages/Klant/Contract.razor",
     '<ContractPanel CustomerId="@write.CustomerId" @rendermode="InteractiveServer" />',
     '<ContractPanel CustomerId="@write.CustomerId" />',
     "het omgevingsblok wordt static SSR (de enige bescherming tegen het wissen van twee scopes)"),
    (44, "Soratus.Portal/Views/SprintViews.cs",
     "        AssignedTo = item.AssignedToName,\n        OpenHours",
     "        AssignedTo = null,\n        OpenHours",
     "de klant ziet niet meer aan wie werk is toegewezen"),
    (45, "Soratus.Portal/Components/Pages/Klant/SprintText.cs",
     '$"{from.Day.ToString(CultureInfo.InvariantCulture)} t/m "',
     '$"{from.Day.ToString(CultureInfo.InvariantCulture)} - "',
     "de periode gebruikt een streepje in plaats van 't/m'"),
    (46, "Soratus.Portal/Components/Pages/Klant/SprintText.cs",
     'hours is { } value ? $"{value.ToString("0.##", Dutch)} u" : Dash;',
     'hours is { } value ? $"{value.ToString("0.##", Dutch)} u" : "0 u";',
     "een niet-ingevuld urenveld verschijnt als nul op het scherm"),
    (47, "Soratus.Portal/Sprints/SprintCollector.cs",
     '        logger.LogInformation(\n            "Sprintronde: {Scoped} van {Total} klant(en) heeft een bruikbaar DevOps-bord.",\n            scoped.Count,\n            targets.Count);\n\n',
     "",
     "de verhouding 'hoeveel klanten hebben een bord' verdwijnt uit het log"),
    (48, "Soratus.Portal/Sprints/SprintStores.cs",
     "            Undated = write.Undated,\n            Overlapping = write.Overlapping,",
     "            Undated = [],\n            Overlapping = write.Overlapping,",
     "de productiemapping laat de iteraties zonder datums vallen"),
    (49, "Soratus.Portal/Sprints/SprintOptions.cs",
     "    public int IntervalMinutes { get; set; } = 15;",
     "    public int IntervalMinutes { get; set; } = 60;",
     "de ronde draait per uur in plaats van per kwartier (§4 zegt 15 min)"),
    (50, "Soratus.Portal/Sprints/SprintStores.cs",
     "                StringComparison.Ordinal)\n                ? response.Resource\n                : null;",
     "                StringComparison.Ordinal)\n                ? response.Resource\n                : response.Resource;",
     "de kind-controle op de puntlezing van het sprintdocument verdwijnt"),
]


def lees(pad: Path) -> str:
    return io.open(pad, encoding="utf-8").read()


def schrijf(pad: Path, inhoud: str) -> None:
    io.open(pad, "w", encoding="utf-8", newline="").write(inhoud)


def draai(argumenten: list[str]) -> tuple[int, str]:
    klaar = subprocess.run(
        argumenten, cwd=WORTEL, capture_output=True, text=True, encoding="utf-8", errors="replace"
    )
    return klaar.returncode, (klaar.stdout or "") + (klaar.stderr or "")


def meet(gemuteerd: str | None = None) -> tuple[str, list[str]]:
    """Bouwt en draait de tests. Geeft de uitkomst en de namen van de rode tests.

    `gemuteerd` is het pad dat is aangeraakt. Staat een compileerfout in een ánder bestand, dan is de
    boom door een andere sessie gebroken en meet deze ronde niets — en "compileert niet" ziet uit als
    een resultaat. Dat is precies de meting die liegt.
    """
    code, uit = draai(
        ["dotnet", "build", "Soratus.Portal.Tests/Soratus.Portal.Tests.csproj", "-v", "q", "--nologo"]
    )

    if code != 0:
        bestanden = sorted({
            m.group(1).replace("\\", "/").split("Website/")[-1]
            for m in re.finditer(r"([A-Za-z]:\\[^(]+)\(\d+,\d+\): error", uit)
        })

        eigen = bool(gemuteerd) and any(str(gemuteerd) in b for b in bestanden)

        return ("compileert niet" if eigen else "BOOM GEBROKEN DOOR ANDERE LANE"), bestanden

    code, uit = draai(
        [
            "dotnet", "test", "--no-build", "--nologo", "-v", "q",
            "--filter", FILTER,
            "Soratus.Portal.Tests/Soratus.Portal.Tests.csproj",
        ]
    )

    # Tot aan " [FAIL]" en niet tot de eerste witruimte: een theorienaam draagt zijn parameters mee
    # ("(pagina: typeof(...))") en daar zitten spaties in. Een regex die dat mist meldt "rood" met een
    # lege lijst — en dan lees je een aantal in plaats van een lijst, wat in dit project al een keer
    # een correcte diagnose heeft teruggedraaid.
    rood = sorted(set(re.findall(r"\]\s+(.+?) \[FAIL\]", uit)))

    if code != 0 and not rood:
        return "rood zonder namen", [r.strip() for r in uit.splitlines() if "rror" in r][:4]

    return ("rood" if code != 0 else "groen"), rood


def main() -> int:
    gekozen = {int(a) for a in sys.argv[1:]} or None

    print("Nulmeting…", flush=True)
    stand, rood = meet()

    if stand != "groen":
        print(f"De nulmeting is niet groen ({stand}): {rood}")
        print("Een mutatieronde op een rode boom meet niets. Eerst repareren.")
        return 1

    print("Nulmeting groen.\n", flush=True)

    resultaten = []

    for nummer, bestand, zoek, vervang, wat in MUTATIES:
        if gekozen is not None and nummer not in gekozen:
            continue

        pad = WORTEL / bestand
        origineel = lees(pad)

        aantal = origineel.count(zoek)

        if aantal != 1:
            print(f"{nummer:>3}  OVERGESLAGEN — de zoektekst komt {aantal}x voor in {bestand}")
            resultaten.append((nummer, wat, f"overgeslagen ({aantal}x gevonden)", []))
            continue

        schrijf(pad, origineel.replace(zoek, vervang))

        try:
            stand, rood = meet(bestand)
        finally:
            schrijf(pad, origineel)
            assert lees(pad) == origineel, f"TERUGZETTEN MISLUKT in {bestand}"

        if stand.startswith("BOOM"):
            print(f"{nummer:>3}  {stand}")
            print(f"     De fout staat in {rood} en niet in {bestand}.")
            print("     Deze mutatie is NIET gemeten. Draai hem opnieuw als de boom groen is.")
            resultaten.append((nummer, wat, stand, rood))
            continue

        kort = ", ".join(t.split(".")[-1] for t in rood[:4])
        extra = f" (+{len(rood) - 4})" if len(rood) > 4 else ""
        print(f"{nummer:>3}  {stand:<16} {wat}\n     {kort}{extra}", flush=True)
        resultaten.append((nummer, wat, stand, rood))

    print("\n── Samenvatting ─────────────────────────────────────────────")

    groen = [r for r in resultaten if r[2] == "groen"]
    print(f"{len(resultaten)} mutaties, {len(groen)} maakten NIETS rood.\n")

    for nummer, wat, stand, rood in groen:
        print(f"  GAT  {nummer}: {wat}")

    for nummer, wat, stand, rood in resultaten:
        if stand.startswith(("overgeslagen", "BOOM", "rood zonder")):
            print(f"  ??   {nummer}: {wat} — {stand} {rood}")

    print("\nTerugzetten gecontroleerd na elke mutatie.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
