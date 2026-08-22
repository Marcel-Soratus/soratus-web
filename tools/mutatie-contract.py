"""De mutatielijst van de getalregel en het omgevingsbeheer (§23 en §37 van de fase-0-afwijkingen).

Alleen de lijst. Het schrijven, terugzetten, bouwen en meten staat in `tools/mutatie.py`; lees de
waarschuwing bovenaan dat bestand voordat je dit script start, want het schrijft in productiebestanden.

Deze lijst stond in `tools/mutatie.py` zelf, in de tijd dat dat bestand nog één script was met één
lijst erin. Hij is hierheen verhuisd toen de meetlaag gedeeld werd: een gedeelde meetlaag met de lijst
van één lane erin is de plek waar de volgende lane zijn eigen lijst overheen schrijft.

Er staat geen filter op: deze mutaties raken de getalregel en het contractscherm, en die worden door
tests uit meerdere mappen gedekt. De hele testset van het portaal draaien is hier dus het juiste, en
niet een filter dat toevallig de mappen raakt die de schrijver in gedachten had.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import mutatie  # noqa: E402


MUTATIES = mutatie.uit_viertallen([
    # ── De getalregel in ContractText (§23) ─────────────────────────────────────────────────────
    (
        "getal-lengte",
        "Soratus.Portal/Components/Pages/ContractText.cs",
        "        if (after.Length != 3)",
        "        if (after.Length != 4)",
    ),
    (
        "getal-alleen-punt",
        "Soratus.Portal/Components/Pages/ContractText.cs",
        "    private static readonly char[] Separators = ['.', ','];",
        "    private static readonly char[] Separators = ['.'];",
    ),
    (
        "getal-een-scheidingsteken",
        "Soratus.Portal/Components/Pages/ContractText.cs",
        "        if (index < 0 || clean.IndexOfAny(Separators) != index)",
        "        if (index < 0)",
    ),
    (
        "getal-geen-cijfercontrole",
        "Soratus.Portal/Components/Pages/ContractText.cs",
        "            if (!char.IsAsciiDigit(character))",
        "            if (false)",
    ),
    (
        "getal-geen-weigering",
        "Soratus.Portal/Components/Pages/ContractText.cs",
        "        if (IsThousandsGrouping(clean))",
        "        if (false && IsThousandsGrouping(clean))",
    ),
    (
        "getal-melding-blind",
        "Soratus.Portal/Components/Pages/ContractText.cs",
        "        invoer is not null && IsThousandsGrouping(Clean(invoer))",
        "        invoer is not null && false",
    ),
    (
        "getal-melding-ongeschoond",
        "Soratus.Portal/Components/Pages/ContractText.cs",
        "        invoer is not null && IsThousandsGrouping(Clean(invoer))",
        "        invoer is not null && IsThousandsGrouping(invoer)",
    ),
    # ── Het omgevingsbeheer in ContractPanel (§37) ──────────────────────────────────────────────
    (
        "omgeving-verse-lezing",
        "Soratus.Portal/Components/Pages/Klant/ContractPanel.razor",
        "            BasedOnETag = _customerETag,",
        "            BasedOnETag = _view!.CustomerETag,",
    ),
    (
        "omgeving-etag-schuift-niet",
        "Soratus.Portal/Components/Pages/Klant/ContractPanel.razor",
        "                _customerETag = _customerConflict?.ETag;",
        "                // _customerETag blijft staan;",
    ),
    (
        "omgeving-geen-verschillen",
        "Soratus.Portal/Components/Pages/Klant/ContractPanel.razor",
        "                _customerChanges = _customerConflict is null ? [] : Changes(_customerConflict);",
        "                _customerChanges = [];",
    ),
    (
        "omgeving-telemetrie-weg",
        "Soratus.Portal/Components/Pages/Klant/ContractPanel.razor",
        "            TelemetryEndpoint = NullIfBlank(_customer.TelemetryEndpoint),",
        "            TelemetryEndpoint = null,",
    ),
    (
        "omgeving-intern-weg",
        "Soratus.Portal/Data/CosmosPortalDataStore.cs",
        "            IsInternal = current?.IsInternal ?? edit.IsInternal,",
        "            IsInternal = current?.IsInternal ?? false,",
    ),
    (
        "omgeving-intern-weg-fixture",
        "Soratus.Portal.Tests/Hulpmiddelen/Vasteportaalopslag.cs",
        "            IsInternal = partitie.Klant?.IsInternal ?? edit.IsInternal,",
        "            IsInternal = partitie.Klant?.IsInternal ?? false,",
    ),
    (
        "omgeving-slug-bewerkbaar",
        "Soratus.Portal/Components/Pages/Klant/ContractPanel.razor",
        '                           Mode="FieldMode.ReadOnly"',
        '                           Mode="FieldMode.Edit" Name="slug"',
    ),
])


if __name__ == "__main__":
    raise SystemExit(mutatie.voer_uit(MUTATIES, alleen=sys.argv[1:] or None))
