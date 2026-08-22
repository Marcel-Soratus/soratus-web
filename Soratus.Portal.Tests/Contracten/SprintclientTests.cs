using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Soratus.Portal.Sprints;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De aanroepen aan Azure DevOps: de adressen, het lezen van de velden, en het onderscheid tussen "geen
/// antwoord" en "een antwoord dat we niet konden lezen" (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>Wat hier is gemeten en wat niet, want dat verschil bepaalt wat deze tests waard zijn.</strong>
/// De metingen van 22 augustus 2026 zijn via een MCP-server gedaan die als <c>marcel@</c> praat en het
/// antwoord bewerkt voordat het bij mij komt. Gemeten zijn de <em>veldnamen</em>, dat een leeg veld níet in
/// het woordenboek staat, dat een veld dat een werkitemsoort niet heeft geen fout geeft maar ontbreekt, dat
/// de teamiteratielijst de datums op middernacht geeft, dat de iteratie-workitems-aanroep
/// <c>workItemRelations</c> met <c>target.id</c> teruggeeft, en dat een werkitemsoort een
/// <c>states</c>-lijst met <c>name</c> en <c>category</c> heeft.</para>
///
/// <para><strong>Niet gemeten zijn de omhulsels.</strong> Dat een lijstantwoord
/// <c>{ "count": n, "value": [ … ] }</c> is, komt uit de documentatie — de MCP-server pakte het uit — en
/// hetzelfde geldt voor de vorm van een identiteitsveld. De antwoorden hieronder zijn dus half gemeten en
/// half gedocumenteerd, en dat staat in het rapport. Wat deze tests wél volledig bewijzen is de
/// <em>lezing</em>: hoe de client zich gedraagt bij een veld dat er niet is, bij een identiteit in beide
/// vormen, en bij een antwoord dat niet compleet is.</para>
///
/// <para>De antwoorden komen uit een eigen <see cref="HttpMessageHandler"/> en niet uit DevOps. Wat daarmee
/// níet is bewezen: een tweede veldenbatch is nooit tegen de echte API gelopen, want de grootste gemeten
/// iteratie had zestien items.</para>
/// </remarks>
public class SprintclientTests
{
    /// <summary>Het gemeten bord.</summary>
    private static readonly DevOpsScope Bord = Ontleed("soratus/MBVApp4 MAUI/MBVApp4 MAUI Team");

    /// <summary>Een dag in de gemeten huidige sprint.</summary>
    private static readonly DateOnly Vandaag = new(2026, 8, 22);

    [Fact]
    public async Task DeVierAdressenGaanInDeGemetenVormDeDeurUit()
    {
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(4566));
        handler.Voeg(Batch(Veld(4566, "Task", "Titel", "Active")));
        handler.Voeg(Soort("Task"));

        var client = Bouw(handler);

        await client.ReadAsync(Bord, Vandaag);

        Assert.Equal(4, handler.Verzoeken.Count);

        // De spaties in de project- en teamnaam worden ge-escaped, en de scope zelf blijft ongewijzigd:
        // die komt op het scherm als "bevraagd: …" en daar hoort MBVApp4 MAUI te staan en niet
        // MBVApp4%20MAUI. Eén waarde, twee coderingen, en de omzetting staat op één plek.
        Assert.Equal(
            "https://dev.azure.com/soratus/MBVApp4%20MAUI/MBVApp4%20MAUI%20Team"
            + "/_apis/work/teamsettings/iterations?api-version=7.1",
            handler.Verzoeken[0].Url);

        // De work items van de sprint hangen aan de guid van de iteratie en niet aan haar pad. Dat is de
        // harde regel van deze lane doorgetrokken naar de query: een pad bevat de naam, en een iteratie die
        // tussen twee aanroepen wordt hernoemd levert dan een pad op dat niets meer vindt.
        Assert.Equal(
            "https://dev.azure.com/soratus/MBVApp4%20MAUI/MBVApp4%20MAUI%20Team"
            + "/_apis/work/teamsettings/iterations/2de79897-d29b-47f9-b6d0-fff5493a6e1a/workitems"
            + "?api-version=7.1",
            handler.Verzoeken[1].Url);

        // De veldenbatch loopt op organisatieniveau — gemeten.
        Assert.Equal(
            "https://dev.azure.com/soratus/_apis/wit/workitemsbatch?api-version=7.1",
            handler.Verzoeken[2].Url);

        // En de categorieën van de states per werkitemsoort, op projectniveau.
        Assert.Equal(
            "https://dev.azure.com/soratus/MBVApp4%20MAUI/_apis/wit/workitemtypes/Task"
            + "?api-version=7.1",
            handler.Verzoeken[3].Url);

        // Het token gaat als bearer mee bij élke aanroep. Zonder deze assertie zou een client die de
        // header op één pad vergeet hier groen blijven en in productie een 401 geven op precies dat pad.
        Assert.All(handler.Verzoeken, verzoek => Assert.Equal("Bearer", verzoek.Schema));
    }

    [Fact]
    public async Task ErWordtNietsGeschreven()
    {
        // §3.4: DevOps is leidend en het portaal schrijft nooit terug. De enige POST is de veldenbatch —
        // een leesaanroep met een lijst nummers in het lichaam omdat een URL daar te kort voor is. Deze
        // test is het mechanische bewijs: geen PUT, geen PATCH, geen DELETE, en de enige POST gaat naar
        // workitemsbatch.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(4566));
        handler.Voeg(Batch(Veld(4566, "Task", "Titel", "Active")));
        handler.Voeg(Soort("Task"));

        await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.All(
            handler.Verzoeken,
            verzoek => Assert.True(
                verzoek.Methode == HttpMethod.Get
                || (verzoek.Methode == HttpMethod.Post
                    && verzoek.Url.Contains("workitemsbatch", StringComparison.Ordinal)),
                $"Er ging een {verzoek.Methode} naar {verzoek.Url}. Het portaal schrijft nooit terug naar "
                + "DevOps (§3.4); de enige POST die mag is de veldenbatch."));
    }

    [Fact]
    public async Task EenVeldDatOntbreektWordtNullEnNooitNul()
    {
        // De belangrijkste test van dit bestand, en hij komt recht uit de meting: van de zestien work items
        // die uit dit bord kwamen had géén enkel item een RemainingWork, CompletedWork, StoryPoints of
        // System.Tags — die sleutels stonden niet in het antwoord. Een lezer die daar 0 van maakt zet
        // "openstaande uren: 0" op een scherm waar "niet ingevuld" hoort te staan.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(4566));
        handler.Voeg(Batch(Veld(4566, "User Story", "iOS MAUI", "New")));
        handler.Voeg(Soort("User Story"));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        var item = Assert.Single(antwoord.Items);

        Assert.Null(item.RemainingWork);
        Assert.Null(item.CompletedWork);
        Assert.Null(item.StoryPoints);
        Assert.Empty(item.Tags);
        Assert.Null(item.AssignedToName);
        Assert.Null(item.AssignedToUniqueName);
    }

    [Fact]
    public async Task EenVeldMetEenWaardeWordtGelezen()
    {
        // De spiegel. Zonder deze test is "null bij een ontbrekend veld" ook waar bij een lezer die altijd
        // null geeft — en dan staat er nooit een getal op het scherm.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(4571));
        handler.Voeg(Batch(
            """
            {
              "id": 4571,
              "fields": {
                "System.WorkItemType": "Task",
                "System.Title": "Declaratieregels valideren",
                "System.State": "Active",
                "System.Tags": "Blocked; infra",
                "Microsoft.VSTS.Scheduling.RemainingWork": 6.5,
                "Microsoft.VSTS.Scheduling.CompletedWork": 1.5,
                "Microsoft.VSTS.Scheduling.StoryPoints": 3
              }
            }
            """));
        handler.Voeg(Soort("Task"));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        var item = Assert.Single(antwoord.Items);

        Assert.Equal(6.5m, item.RemainingWork);
        Assert.Equal(1.5m, item.CompletedWork);
        Assert.Equal(3m, item.StoryPoints);

        // Gesplitst op de puntkomma en niet op "; ": de scheiding is het teken en de spatie is opmaak, en
        // een tag met een spatie erin bestaat.
        Assert.Equal(["Blocked", "infra"], item.Tags);
    }

    [Fact]
    public async Task EenIdentiteitAlsObjectLevertNaamEnAdres()
    {
        // De vorm die de REST-API volgens de documentatie geeft.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(4567));
        handler.Voeg(Batch(
            """
            {
              "id": 4567,
              "fields": {
                "System.WorkItemType": "Task",
                "System.Title": "performance",
                "System.State": "Closed",
                "System.CreatedBy": {
                  "displayName": "Dennis Verhamme",
                  "uniqueName": "dennis@soratus.com"
                },
                "System.AssignedTo": {
                  "displayName": "Sanne de Wit",
                  "uniqueName": "sanne@soratus.com"
                }
              }
            }
            """));
        handler.Voeg(Soort("Task"));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        var item = Assert.Single(antwoord.Items);

        Assert.Equal("Dennis Verhamme", item.CreatedByName);
        Assert.Equal("dennis@soratus.com", item.CreatedByUniqueName);
        Assert.Equal("Sanne de Wit", item.AssignedToName);
        Assert.Equal("sanne@soratus.com", item.AssignedToUniqueName);
    }

    [Fact]
    public async Task EenIdentiteitAlsTekenreeksLevertAlleenEenNaam()
    {
        // De vorm die bij mij aankwam, want de MCP-server waarmee is gemeten bewerkt het antwoord:
        // "Dennis Verhamme <dennis@soratus.com>". De ruwe vorm was dus niet te meten, en dan is een lezer
        // die beide vormen aankan het enige eerlijke antwoord.
        //
        // En er wordt géén adres uit die tekenreeks gepeuterd, terwijl het er wel in staat. Dat is de
        // veilige kant: die vorm is niet gegarandeerd, en een ontleedregel op een weergavetekst is precies
        // de fout waar DevOpsScope tegen bestaat. De prijs is dat de herkomst dan op de weergavenaam
        // vergelijkt; de winst is dat er nooit een adres op een scherm staat dat wij niet als adres hebben
        // herkend.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(4567));
        handler.Voeg(Batch(
            """
            {
              "id": 4567,
              "fields": {
                "System.WorkItemType": "Task",
                "System.Title": "performance",
                "System.State": "Closed",
                "System.CreatedBy": "Dennis Verhamme <dennis@soratus.com>"
              }
            }
            """));
        handler.Voeg(Soort("Task"));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        var item = Assert.Single(antwoord.Items);

        Assert.Equal("Dennis Verhamme <dennis@soratus.com>", item.CreatedByName);
        Assert.Null(item.CreatedByUniqueName);
    }

    [Theory]
    [InlineData("New", "Proposed", WorkItemStage.Proposed)]
    [InlineData("Active", "InProgress", WorkItemStage.InProgress)]
    [InlineData("Resolved", "Resolved", WorkItemStage.Resolved)]
    [InlineData("Closed", "Completed", WorkItemStage.Completed)]
    [InlineData("Removed", "Removed", WorkItemStage.Removed)]
    public async Task DeCategorieVanEenStateBepaaltDeFaseEnDeNaamGaatOngewijzigdMee(
        string state,
        string categorie,
        WorkItemStage fase)
    {
        // §3.4 schrijft vijf statenamen voor die op dit bord niet bestaan: gemeten heeft Task er vier — New,
        // Active, Closed, Removed — en geen Blocked of Resolved. Statenamen zijn per proces anders, dus het
        // portaal rekent op de categorie en zet de naam op het scherm.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(1));
        handler.Voeg(Batch(Veld(1, "Task", "Titel", state)));
        handler.Voeg(Soort("Task", (state, categorie)));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        var item = Assert.Single(antwoord.Items);

        Assert.Equal(fase, item.Stage);
        Assert.Equal(state, item.State);
    }

    [Fact]
    public async Task EenOnbekendeCategorieMaaktDeLezingOnleesbaar()
    {
        // Niet stil "niet afgerond". Zou DevOps ooit een zesde categorie invoeren, dan hoort dat een
        // zichtbaar defect te zijn en geen statistiek die te laag is — en van de twee mogelijke fouten is
        // alleen "geen getal" zichtbaar.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(1));
        handler.Voeg(Batch(Veld(1, "Task", "Titel", "Wachtend")));
        handler.Voeg(Soort("Task", ("Wachtend", "OpDePlank")));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Unreadable, antwoord.Kind);
        Assert.Empty(antwoord.Items);
        Assert.NotNull(antwoord.Reason);
    }

    [Fact]
    public async Task EenStateDieDeSoortNietKentMaaktDeLezingOnleesbaar()
    {
        // De categorie wordt per soort én state opgezocht en niet per state alleen. Twee soorten kunnen een
        // state met dezelfde naam en een andere categorie hebben, en één woordenboek op alleen de statenaam
        // zou dan de categorie van de ene soort aan de andere geven.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(1));
        handler.Voeg(Batch(Veld(1, "Task", "Titel", "Verzonnen")));
        handler.Voeg(Soort("Task"));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Unreadable, antwoord.Kind);
    }

    [Fact]
    public async Task EenAntwoordMetMinderItemsDanErIsGevraagdIsOnleesbaar()
    {
        // Geen halve lijst. Het aantal work items van de sprint zou er te laag uit komen, en dat is de fout
        // die niet te zien is — dezelfde vorm als de vierde regel van punt 39.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(1, 2, 3));
        handler.Voeg(Batch(Veld(1, "Task", "Titel", "Active")));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Unreadable, antwoord.Kind);
        Assert.Contains("te laag", antwoord.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MeerItemsDanDeGrensIsOnleesbaarEnGeenGedeeltelijkeLijst()
    {
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers([.. Enumerable.Range(1, 6)]));

        var antwoord = await Bouw(handler, new SprintOptions { MaxWorkItems = 5 })
            .ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Unreadable, antwoord.Kind);
        Assert.Empty(antwoord.Items);

        // En er is niet eens naar de velden gevraagd: een lezing die toch onbruikbaar is, hoort geen
        // aanroepen te kosten.
        Assert.Equal(2, handler.Verzoeken.Count);
    }

    [Fact]
    public async Task ZonderHuidigeSprintWordtErNietNaarWorkItemsGevraagd()
    {
        // Geen sprint is een geslaagde lezing en geen mislukking, en er is niets om work items bij te
        // vragen. Dat scheelt drie aanroepen per klant per ronde op een bord tussen twee sprints.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties(zonderDatums: true));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Answered, antwoord.Kind);
        Assert.Equal(SprintState.NoDatedIterations, antwoord.Choice.State);
        Assert.Single(handler.Verzoeken);
    }

    [Fact]
    public async Task DeDatumsWordenAlsDagGelezenEnDeTijdValtWeg()
    {
        // Gemeten: er is "31 augustus 23:59:59" verstuurd en "2026-08-31T00:00:00Z" teruggekomen. Het zijn
        // datums en geen momenten, en een omrekening naar een lokale zone zou van die middernacht 31
        // augustus 02:00 maken — of, een uur de andere kant op, 30 augustus 23:00, en dan mist de sprint
        // een dag.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers());

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(new DateOnly(2026, 8, 1), antwoord.Choice.Current!.Start);
        Assert.Equal(new DateOnly(2026, 8, 31), antwoord.Choice.Current.Finish);
    }

    [Fact]
    public async Task EenDatumMetEenTijdzoneWordtNietNaarDeLokaleZoneOmgerekend()
    {
        // Een gat dat een mutatie vond, en het is er maar half mee gedicht — dat staat er met opzet bij.
        //
        // De mutatie was UtcDateTime → LocalDateTime, en op de gemeten antwoorden (middernacht in UTC) is
        // die niet te zien: op een machine die vóór UTC loopt wordt 2026-08-01T00:00:00Z lokaal
        // 2026-08-01 02:00, en dat is dezelfde dag. Er is géén invoer waarmee dat verschil op élke machine
        // zichtbaar is: het onderscheid tussen UTC en lokaal ís de tijdzone van de machine.
        //
        // Wat deze test wél doet: een datum met een eigen offset. Dan wijkt de UTC-dag af van de lokale
        // dag op elke machine die niet op UTC staat — onze werkplekken en de West-Europa-agent — en daar
        // valt de mutatie dus om. Op een agent in UTC is deze test groen om de verkeerde reden, en dat is
        // de eerlijke grens van deze meting.
        //
        // Waarom UTC de juiste kant is: DevOps laat de tijd van een iteratiedatum vallen (gemeten: 31
        // augustus 23:59:59 verstuurd, 2026-08-31T00:00:00Z terug). Het zijn datums, en een omrekening naar
        // een lokale zone kan er een dag naast zitten — dan mist de sprint zijn laatste dag.
        var handler = new Vasteantwoorden();
        handler.Voeg(Json(
            """
            {
              "count": 1,
              "value": [
                {
                  "id": "2de79897-d29b-47f9-b6d0-fff5493a6e1a",
                  "name": "2026-08 Augustus",
                  "path": "MBVApp4 MAUI\\2026-08 Augustus",
                  "attributes": {
                    "startDate": "2026-08-01T00:00:00+02:00",
                    "finishDate": "2026-09-01T00:00:00+02:00"
                  }
                }
              ]
            }
            """));
        handler.Voeg(Nummers());

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        // De UTC-dag van 1 augustus 00:00 +02:00 is 31 juli. Dat is wat er hoort uit te komen.
        Assert.Equal(new DateOnly(2026, 7, 31), antwoord.Choice.Current!.Start);
        Assert.Equal(new DateOnly(2026, 8, 31), antwoord.Choice.Current.Finish);
    }

    [Fact]
    public async Task DeIteratiesZonderDatumsKomenMeeMetDeGeslaagdeLezing()
    {
        // De drie oude iteraties van het echte bord staan er met opzet nog. Ze vallen in geen enkele maand,
        // dus hun werk komt op geen enkele sprintweergave voor — en een scherm dat dat niet meldt, biedt een
        // onvolledig beeld aan als volledig.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties(metOude: true));
        handler.Voeg(Nummers());

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintState.Current, antwoord.Choice.State);
        Assert.Equal(
            ["Iteration 1", "Iteration 2", "Iteration 3"],
            antwoord.Choice.Undated.Select(iteratie => iteratie.Name));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task EenGeweigerdVerzoekWordtNietHerhaaldEnLevertNiets(HttpStatusCode status)
    {
        // Die gaan niet over van zichzelf: de eerste twee zijn een ontbrekende rolverlening en de derde is
        // een bord dat niet bestaat — en DevOps geeft ook een 404 op een project waar de aanroeper geen
        // recht op heeft, zodat het bestaan ervan niet lekt. Herhalen kost een aanroep en verandert niets.
        //
        // Dat is een andere keuze dan bij Cost Management, waar de 404 gemeten "probeer opnieuw" bleek te
        // betekenen. Die meting geldt daar en niet hier, en dat verschil is het punt.
        var handler = new Vasteantwoorden();
        handler.Voeg(new HttpResponseMessage(status));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.NotAvailable, antwoord.Kind);
        Assert.Single(handler.Verzoeken);
        Assert.NotNull(antwoord.Reason);
    }

    [Fact]
    public async Task EenGeweigerdVerzoekMeldtGeenStatuscodeMaarEenReden()
    {
        // Dit komt op een operatorscherm en niet in een logregel; zie SprintDocument.Failure. "403 na 3
        // pogingen" zegt een operator minder dan "de identiteit heeft leesrecht op het project nodig", en
        // de technische vorm staat in de logregel ernaast met api.retry ervoor.
        var handler = new Vasteantwoorden();
        handler.Voeg(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.DoesNotContain("403", antwoord.Reason!, StringComparison.Ordinal);
        Assert.Contains("leesrecht", antwoord.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EenGeweigerdVerzoekWegensDrukteWordtWelHerhaald()
    {
        // 429 is de enige weigering die van zichzelf overgaat. De backoff loopt op de klok van de test, dus
        // deze test wacht niet echt — zie Snelleklok.
        var handler = new Vasteantwoorden();
        handler.Voeg(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers());

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Answered, antwoord.Kind);

        // Drie en niet vier: de geweigerde poging, de gelukte iteratielijst en de nummerlijst. Een sprint
        // zonder items kost geen veldenbatch en geen soortenaanroep — zie de test hieronder.
        Assert.Equal(3, handler.Verzoeken.Count);
    }

    [Fact]
    public async Task EenSprintZonderItemsKostTweeAanroepenEnGeenVier()
    {
        // Gemeten met deze eigen handler en niet met DevOps, maar het is een eigenschap van onze code en
        // niet van de API: zonder nummers is er niets om velden bij te vragen en geen werkitemsoort om de
        // categorieën van op te halen. Dat is de zuinige kant, en hij staat hier vast omdat een
        // implementatie die tóch een lege batch verstuurt twee aanroepen per klant per ronde kost aan een
        // antwoord dat niemand nodig heeft.
        //
        // En het is een echte nul: nul work items is een gemeten uitkomst en geen ontbrekende lezing.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers());

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Answered, antwoord.Kind);
        Assert.Equal(SprintState.Current, antwoord.Choice.State);
        Assert.Empty(antwoord.Items);
        Assert.Equal(2, handler.Verzoeken.Count);
    }

    [Fact]
    public async Task DePogingenRakenOpEnDanIsErNiets()
    {
        var handler = new Vasteantwoorden();

        for (var poging = 0; poging < 3; poging++)
        {
            handler.Voeg(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        }

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.NotAvailable, antwoord.Kind);
        Assert.Equal(3, handler.Verzoeken.Count);
        Assert.Equal(3, antwoord.Calls);
    }

    [Fact]
    public async Task EenAntwoordDatGeenJsonIsIsOnleesbaarEnGeenOntbrekendAntwoord()
    {
        // Er ís geantwoord, en dat ons antwoord niet meer bij de API past hoort op het scherm te komen in
        // plaats van de vorige lezing te laten staan. Punt 39, tweede regel.
        var handler = new Vasteantwoorden();
        handler.Voeg(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>gateway</html>", Encoding.UTF8, "application/json"),
        });

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Unreadable, antwoord.Kind);
    }

    [Fact]
    public async Task EenItemZonderTitelOfSoortMaaktDeLezingOnleesbaar()
    {
        // Niet dit ene item onzichtbaar. Een item dat wegvalt maakt het aantal te laag, en dat is de fout
        // die niemand ziet.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(1));
        handler.Voeg(Batch("""{ "id": 1, "fields": { "System.State": "Active" } }"""));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Unreadable, antwoord.Kind);
    }

    [Fact]
    public async Task DubbeleNummersLeverenEenItemOp()
    {
        // De iteratielijst geeft de bovenste items als losse rij én als bron van een hiërarchierelatie.
        // Gemeten kwamen er zestien unieke nummers uit, maar de vorm laat een dubbel toe — en een dubbel
        // nummer in de batch zou een aantal opleveren dat te hoog is.
        var handler = new Vasteantwoorden();
        handler.Voeg(Iteraties());
        handler.Voeg(Nummers(4571, 4571, 4572));
        handler.Voeg(Batch(
            Veld(4571, "Task", "Een", "Active"),
            Veld(4572, "Task", "Twee", "Active")));
        handler.Voeg(Soort("Task"));

        var antwoord = await Bouw(handler).ReadAsync(Bord, Vandaag);

        Assert.Equal(SprintAnswerKind.Answered, antwoord.Kind);
        Assert.Equal(2, antwoord.Items.Count);
    }

    /// <summary>Ontleedt een bord, of werpt als het niet kan.</summary>
    /// <param name="tekst">De tekst.</param>
    /// <returns>Het bord.</returns>
    private static DevOpsScope Ontleed(string tekst) =>
        DevOpsScope.TryParse(tekst, out var bord) && bord is not null
            ? bord
            : throw new InvalidOperationException(
                $"'{tekst}' is geen geldig DevOps-bord, dus deze test meet niets. Zie DevOpsScope.");

    /// <summary>Bouwt de client op deze handler.</summary>
    /// <param name="handler">De antwoorden.</param>
    /// <param name="opties">De instellingen, of <c>null</c> voor de standaard.</param>
    /// <returns>De client.</returns>
    private static IDevOpsSprintClient Bouw(Vasteantwoorden handler, SprintOptions? opties = null) =>
        new DevOpsSprintClient(
            new Vastefabriek(handler),
            new Vastebron(),
            Options.Create(opties ?? new SprintOptions()),
            new Snelleklok(new DateTimeOffset(2026, 8, 22, 4, 0, 0, TimeSpan.Zero)),
            NullLogger<DevOpsSprintClient>.Instance);

    /// <summary>De iteratielijst van het team, in de gemeten vorm.</summary>
    /// <param name="zonderDatums">Of de maandsprints hun datums missen.</param>
    /// <param name="metOude">Of de drie oude iteraties zonder datums erbij staan.</param>
    /// <returns>Het antwoord.</returns>
    private static HttpResponseMessage Iteraties(bool zonderDatums = false, bool metOude = false)
    {
        // Met geëscapete aanhalingstekens en niet als ruwe tekenreeks: een ruwe tekenreeks die met een
        // aanhalingsteken begint, laat de compiler het openingsteken meetellen in het scheidingsteken.
        var datums = zonderDatums
            ? "\"startDate\": null, \"finishDate\": null"
            : "\"startDate\": \"2026-08-01T00:00:00Z\", \"finishDate\": \"2026-08-31T00:00:00Z\"";

        var oude = metOude
            ? string.Join(
                ',',
                Enumerable.Range(1, 3).Select(nummer =>
                    $$"""
                    {
                      "id": "oud-{{nummer}}",
                      "name": "Iteration {{nummer}}",
                      "path": "MBVApp4 MAUI\\Iteration {{nummer}}",
                      "attributes": { "startDate": null, "finishDate": null, "timeFrame": 2 }
                    }
                    """))
            : null;

        return Json(
            $$"""
            {
              "count": 1,
              "value": [
                {
                  "id": "2de79897-d29b-47f9-b6d0-fff5493a6e1a",
                  "name": "2026-08 Augustus",
                  "path": "MBVApp4 MAUI\\2026-08 Augustus",
                  "attributes": { {{datums}}, "timeFrame": 1 }
                }{{(oude is null ? string.Empty : "," + oude)}}
              ]
            }
            """);
    }

    /// <summary>De work item-nummers van een iteratie, in de gemeten vorm.</summary>
    /// <param name="nummers">De nummers.</param>
    /// <returns>Het antwoord.</returns>
    private static HttpResponseMessage Nummers(params int[] nummers) =>
        Json(
            $$"""
            {
              "workItemRelations": [
                {{string.Join(
                    ',',
                    nummers.Select(nummer => $$"""{ "rel": null, "target": { "id": {{nummer}} } }"""))}}
              ]
            }
            """);

    /// <summary>De veldenbatch, in de gedocumenteerde omhulselvorm.</summary>
    /// <param name="items">De items als JSON.</param>
    /// <returns>Het antwoord.</returns>
    private static HttpResponseMessage Batch(params string[] items) =>
        Json($$"""{ "count": {{items.Length}}, "value": [ {{string.Join(',', items)}} ] }""");

    /// <summary>Eén work item met de vier velden die altijd aanwezig horen te zijn.</summary>
    /// <param name="id">Het nummer.</param>
    /// <param name="soort">De werkitemsoort.</param>
    /// <param name="titel">De titel.</param>
    /// <param name="state">De statenaam.</param>
    /// <returns>Het item als JSON.</returns>
    private static string Veld(int id, string soort, string titel, string state) =>
        $$"""
        {
          "id": {{id}},
          "fields": {
            "System.WorkItemType": "{{soort}}",
            "System.Title": "{{titel}}",
            "System.State": "{{state}}"
          }
        }
        """;

    /// <summary>De metadata van een werkitemsoort, met de gemeten states.</summary>
    /// <param name="naam">De soort.</param>
    /// <param name="extra">Een extra state met zijn categorie, of niets.</param>
    /// <returns>Het antwoord.</returns>
    /// <remarks>
    /// De vier gemeten states van <c>Task</c> op dit bord, plus <c>Resolved</c> uit de documentatie van het
    /// Agile-proces. Een test die een eigen state nodig heeft geeft hem als <paramref name="extra"/> mee.
    /// </remarks>
    private static HttpResponseMessage Soort(string naam, (string State, string Categorie)? extra = null)
    {
        var states = new List<string>
        {
            """{ "name": "New", "color": "b2b2b2", "category": "Proposed" }""",
            """{ "name": "Active", "color": "007acc", "category": "InProgress" }""",
            """{ "name": "Resolved", "color": "ff9d00", "category": "Resolved" }""",
            """{ "name": "Closed", "color": "339933", "category": "Completed" }""",
            """{ "name": "Removed", "color": "ffffff", "category": "Removed" }""",
        };

        if (extra is { } eigen)
        {
            states.Add($$"""{ "name": "{{eigen.State}}", "category": "{{eigen.Categorie}}" }""");
        }

        return Json($$"""{ "name": "{{naam}}", "states": [ {{string.Join(',', states)}} ] }""");
    }

    /// <summary>Een geslaagd antwoord met dit lichaam.</summary>
    /// <param name="body">Het lichaam.</param>
    /// <returns>Het antwoord.</returns>
    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    /// <summary>Eén verzoek dat de client heeft gedaan.</summary>
    /// <param name="Methode">De HTTP-methode.</param>
    /// <param name="Url">Het volledige adres.</param>
    /// <param name="Schema">Het autorisatieschema, of <c>null</c>.</param>
    private readonly record struct Verzoek(HttpMethod Methode, string Url, string? Schema);

    /// <summary>Een handler die antwoorden uit een rij geeft en de verzoeken onthoudt.</summary>
    private sealed class Vasteantwoorden : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _antwoorden = new();

        /// <summary>Elk verzoek dat is gedaan, in volgorde.</summary>
        public List<Verzoek> Verzoeken { get; } = [];

        /// <summary>Zet een antwoord achter in de rij.</summary>
        /// <param name="antwoord">Het antwoord.</param>
        public void Voeg(HttpResponseMessage antwoord) => _antwoorden.Enqueue(antwoord);

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            // AbsoluteUri en niet ToString(): gemeten geeft Uri.ToString() de %20 terug als spatie,
            // want die vorm is voor mensen. Wat er over de lijn gaat is de gecodeerde vorm, en die staat
            // in AbsoluteUri. Een test die op ToString() meet, kan de escaping niet zien — en zou dus
            // groen blijven als hij wegvalt.
            Verzoeken.Add(new Verzoek(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Headers.Authorization?.Scheme));

            // Geen verzonnen standaardantwoord: een handler die bij een lege rij iets plausibels geeft,
            // maakt een test groen om een reden die de testschrijver niet heeft opgeschreven. Een 500 is
            // hier de luidruchtige kant.
            return Task.FromResult(
                _antwoorden.Count > 0
                    ? _antwoorden.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent(
                            """{"message":"deze test had geen antwoord meer in de rij"}""",
                            Encoding.UTF8,
                            "application/json"),
                    });
        }
    }

    /// <summary>Een fabriek die altijd dezelfde handler oplevert.</summary>
    /// <param name="handler">De handler.</param>
    /// <remarks>
    /// De productiecode vraagt een <see cref="IHttpClientFactory"/> en geen <see cref="HttpClient"/>, omdat
    /// hij aan een achtergronddienst hangt die zolang het portaal draait blijft leven. Deze fabriek geeft
    /// telkens een verse <c>HttpClient</c> op dezelfde handler, zodat de verzoeken van alle aanroepen in
    /// één lijst staan.
    /// </remarks>
    private sealed class Vastefabriek(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <inheritdoc />
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Een tokenbron die niet met Entra praat.</summary>
    /// <remarks>
    /// De echte weg is <c>DefaultAzureCredential</c> met de resource-id van Azure DevOps, en die is in een
    /// test niet te lopen: er is geen managed identity en de identiteit is nog geen lid van de organisatie
    /// (gemeten). Wat deze bron bewijst is dat het token als bearer meegaat; dát het uitgeefbaar is, is
    /// geen eigenschap van deze code.
    /// </remarks>
    private sealed class Vastebron : TokenCredential
    {
        /// <inheritdoc />
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.MaxValue);

        /// <inheritdoc />
        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
