"""De mutatielijst van de AI-eerstelijn (§47 van de fase-0-afwijkingen).

Alleen de lijst. Het schrijven, terugzetten, bouwen en meten staat in `tools/mutatie.py`; lees de
waarschuwing bovenaan dat bestand voordat je dit script start, want het schrijft in
productiebestanden.

Twee rondes, want deze lane heeft twee testprojecten en `voer_uit` meet er één per aanroep:

    B1-B9    de brug in Soratus.Portal      -> Soratus.Portal.Tests, filter ~Eerstelijn
    K1-K13   de kiezer in Soratus.Support.FirstLine -> Soratus.Support.FirstLine.Tests

De brugmutaties gaan bijna allemaal over hetzelfde: **de index**. Dat is de zwakke plek die dit
ontwerp erbij heeft gekregen ten opzichte van §46. Daar kon een antwoord geen feit verzinnen; hier
kan het een verkeerd feit áánwijzen, en anders dan een verzonnen bedrag is dat onze fout en niet die
van het model. Plus één, min één, lijst omgekeerd, afkappen in plaats van overdragen: alle vier horen
rood te worden, en daarom staan ze vooraan.

S1-S3 zijn met opzet stil. Ze staan hier zodat ze bij een volgende ronde niet opnieuw als vondst
worden gemeld; de reden staat erbij en in §47.9.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import mutatie  # noqa: E402


BRUG = "Soratus.Portal/Support/ChoosingFirstLine.cs"
KEUZE = "Soratus.Support.FirstLine/FirstLineChoice.cs"
PROMPT = "Soratus.Support.FirstLine/FirstLinePrompt.cs"
OPTIES = "Soratus.Support.FirstLine/FirstLineOptions.cs"
KIEZER = "Soratus.Support.FirstLine/AzureOpenAiChooser.cs"
REGISTRATIE = "Soratus.Support.FirstLine/FirstLineRegistration.cs"


BRUGMUTATIES = [
    mutatie.enkel(
        "B1  de aangewezen plaats schuift een op (index + 1)",
        BRUG,
        "        return SupportAnswer.GroundedIn(grounds[index]);",
        "        return SupportAnswer.GroundedIn(grounds[Math.Min(index + 1, grounds.Count - 1)]);",
    ),
    mutatie.enkel(
        "B2  de aangewezen plaats schuift een terug (index - 1)",
        BRUG,
        "        return SupportAnswer.GroundedIn(grounds[index]);",
        "        return SupportAnswer.GroundedIn(grounds[Math.Max(index - 1, 0)]);",
    ),
    mutatie.enkel(
        "B3  de lijst wordt omgekeerd voordat de plaats erin wordt gezocht",
        BRUG,
        "        var grounds = enquiry.Grounds;",
        "        var grounds = enquiry.Grounds.Reverse().ToList();",
    ),
    # Afkappen raakt twee plekken en compileert alleen als geheel: de controle eruit én de index
    # begrenzen. Vandaar samengesteld en niet twee losse mutaties -- zie de tweede meetval in
    # tools/mutatie.py, waar een niet-compilerende tussenstand als groen werd gelezen.
    mutatie.samengesteld(
        "B4  een plaats buiten de lijst wordt afgekapt in plaats van overgedragen", [
            (
                BRUG,
                "        if (choice.Index is not { } index || index < 0 || index >= grounds.Count)",
                "        if (choice.Index is not { } index)",
            ),
            (
                BRUG,
                "        return SupportAnswer.GroundedIn(grounds[index]);",
                "        return SupportAnswer.GroundedIn(grounds[Math.Clamp(index, 0, grounds.Count - 1)]);",
            ),
        ],
    ),
    mutatie.enkel(
        "B5  de feiten worden uit een gesorteerde kopie opgebouwd",
        BRUG,
        "            Facts = [.. grounds.Select(ground => ground.Fact)],",
        "            Facts = [.. grounds.Select(ground => ground.Fact).OrderBy(f => f, StringComparer.Ordinal)],",
    ),
    mutatie.enkel(
        "B6  de bovengrens is een te hoog (>= wordt >)",
        BRUG,
        "|| index < 0 || index >= grounds.Count)",
        "|| index < 0 || index > grounds.Count)",
    ),
    mutatie.enkel(
        "B7  elke overdracht komt op dezelfde reden uit",
        BRUG,
        "        FirstLineHandoff.OutsideTheData => SupportEscalation.OutsideTheData,\n"
        "        FirstLineHandoff.NeedsAHuman => SupportEscalation.NeedsAHuman,\n",
        "",
    ),
    mutatie.enkel(
        "B8  een overdracht wordt de escalatie van het portaal (AnswerNotUsable)",
        BRUG,
        "        FirstLineHandoff.OutsideTheData => SupportEscalation.OutsideTheData,",
        "        FirstLineHandoff.OutsideTheData => SupportEscalation.AnswerNotUsable,",
    ),
    mutatie.enkel(
        "B9  geen keuze krijgt tóch een eigen reden mee",
        BRUG,
        "            // waarom precies staat in de logregel van de kiezer zelf.\n"
        "            return null;",
        "            // waarom precies staat in de logregel van de kiezer zelf.\n"
        "            return SupportAnswer.Escalate(SupportEscalation.NotSure);",
    ),
]


KIEZERMUTATIES = [
    mutatie.enkel(
        "K1  het nummer wordt niet naar een nulgebaseerde plaats omgezet",
        PROMPT,
        "            : FirstLineChoice.Fact(number - 1);",
        "            : FirstLineChoice.Fact(number);",
    ),
    mutatie.enkel(
        "K2  nummer nul wordt een keuze in plaats van een overdracht",
        PROMPT,
        "        return number < 1",
        "        return number < 0",
    ),
    mutatie.enkel(
        "K3  bij een antwoord met beide vormen wint het nummer",
        PROMPT,
        "        if (Handoff(root) is { } handoff)\n"
        "        {\n"
        "            return FirstLineChoice.ToAHuman(handoff);\n"
        "        }\n"
        "\n"
        "        if (Number(root) is not { } number)\n"
        "        {\n"
        "            return null;\n"
        "        }\n"
        "\n"
        "        return number < 1\n"
        "            ? FirstLineChoice.ToAHuman(FirstLineHandoff.NotSure)\n"
        "            : FirstLineChoice.Fact(number - 1);",
        "        if (Number(root) is { } number)\n"
        "        {\n"
        "            return number < 1\n"
        "                ? FirstLineChoice.ToAHuman(FirstLineHandoff.NotSure)\n"
        "                : FirstLineChoice.Fact(number - 1);\n"
        "        }\n"
        "\n"
        "        return Handoff(root) is { } handoff ? FirstLineChoice.ToAHuman(handoff) : null;",
    ),
    mutatie.enkel(
        "K4  een onbekend overdrachtswoord levert niets op in plaats van 'niet zeker'",
        PROMPT,
        '            _ => FirstLineHandoff.NotSure,',
        '            _ => null,',
    ),
    mutatie.enkel(
        "K5  zonder feiten wordt er tóch een aanroep gedaan",
        KIEZER,
        "        if (question.Facts.Count == 0)",
        "        if (question.Facts.Count < 0)",
    ),
    mutatie.enkel(
        "K6  de tijdslimiet wordt niet gezet",
        KIEZER,
        "        limit.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));",
        "        _ = settings.TimeoutSeconds;",
    ),
    mutatie.enkel(
        "K7  een afgebroken klant levert een stille null in plaats van een afbreking",
        KIEZER,
        "        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)",
        "        catch (OperationCanceledException) when (false)",
    ),
    mutatie.enkel(
        "K8  het antwoord van het model mag varieren (temperature 1)",
        KIEZER,
        "            temperature = 0,",
        "            temperature = 1,",
    ),
    mutatie.enkel(
        "K9  een afgekapt of gefilterd antwoord wordt tóch gelezen",
        KIEZER,
        "        if (first.TryGetProperty(\"finish_reason\", out var finish)\n"
        "            && finish.ValueKind == JsonValueKind.String\n"
        "            && finish.GetString() is { } reason\n"
        "            && reason != \"stop\")",
        "        if (first.TryGetProperty(\"finish_reason\", out var finish)\n"
        "            && finish.ValueKind == JsonValueKind.String\n"
        "            && finish.GetString() is { } reason\n"
        "            && reason == \"nooit\")",
    ),
    mutatie.enkel(
        "K10 de vraag van de klant komt in de foutregel terecht",
        KIEZER,
        '                "{Deployment} antwoordde met {Status}. De vraag gaat naar een mens.",\n'
        "                settings.Deployment,\n"
        "                (int)response.StatusCode);",
        '                "{Deployment} antwoordde met {Status} op {Vraag}. De vraag gaat naar een mens.",\n'
        "                settings.Deployment,\n"
        "                (int)response.StatusCode,\n"
        "                FirstLinePrompt.User(question));",
    ),
    mutatie.enkel(
        "K11 de schakelaar staat standaard aan",
        OPTIES,
        "    public bool Enabled { get; set; }",
        "    public bool Enabled { get; set; } = true;",
    ),
    mutatie.enkel(
        "K12 'uitgezet' gaat voor 'ontwikkelmachine'",
        OPTIES,
        "        isDevelopment\n"
        "            ? FirstLineState.DevelopmentMachine\n"
        "            : CompletionsUri() is null\n"
        "                ? FirstLineState.NotConfigured\n"
        "                : Enabled\n"
        "                    ? FirstLineState.Ready\n"
        "                    : FirstLineState.TurnedOff;",
        "        !Enabled\n"
        "            ? FirstLineState.TurnedOff\n"
        "            : isDevelopment\n"
        "                ? FirstLineState.DevelopmentMachine\n"
        "                : CompletionsUri() is null\n"
        "                    ? FirstLineState.NotConfigured\n"
        "                    : FirstLineState.Ready;",
    ),
    mutatie.enkel(
        "K13 de kiezer wordt geregistreerd ook als hij niet mag draaien",
        REGISTRATIE,
        "        if (setup.IsReady)",
        "        if (setup.State != FirstLineState.Ready || true)",
    ),
]


# ── Met opzet stil ──────────────────────────────────────────────────────────────────────────────
#
# S1  de Nederlandse zin van een stand anders formuleren. Copy is geen invariant; wat wél een
#     invariant is, is dat de vier standen vier verschillende regels opleveren, en dát wordt
#     gemeten (ElkeStandHeeftEenEigenRegelDieDeRedenNoemt).
# S2  max_tokens van 32 naar 4000. Een afstemming en geen invariant: het antwoord is {"kies": 3}, en
#     een ruimere grens maakt geen ander antwoord mogelijk dat door de leeskant komt.
# S3  de naam van de HttpClient in de fabriek wijzigen. De test gebruikt de constante symbolisch,
#     met opzet: de waarde is een naam en geen invariant. Zelfde geval als S3 van de supportlane.
STIL = [
    mutatie.enkel(
        "S1  de regel van 'uitgezet' anders formuleren",
        REGISTRATIE,
        '                    "De AI-eerstelijn staat uit (PortalFirstLine:Enabled). Dat is de standaardstand; "',
        '                    "De eerstelijn is niet actief. "',
    ),
    mutatie.enkel(
        "S2  max_tokens van 32 naar 4000",
        KIEZER,
        "            max_tokens = 32,",
        "            max_tokens = 4000,",
    ),
    mutatie.enkel(
        "S3  de naam van de HttpClient wijzigen",
        KIEZER,
        'internal const string HttpClientName = "eerstelijn-aoai";',
        'internal const string HttpClientName = "aoai";',
    ),
]


if __name__ == "__main__":
    alleen = sys.argv[1:] or None

    brug = mutatie.voer_uit(
        BRUGMUTATIES,
        project="Soratus.Portal.Tests/Soratus.Portal.Tests.csproj",
        filter="FullyQualifiedName~Eerstelijn",
        alleen=alleen,
    )

    kiezer = mutatie.voer_uit(
        KIEZERMUTATIES + STIL,
        project="Soratus.Support.FirstLine.Tests/Soratus.Support.FirstLine.Tests.csproj",
        alleen=alleen,
    )

    raise SystemExit(brug or kiezer)
