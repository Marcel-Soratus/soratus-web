using Soratus.Portal.Sprints;

namespace Soratus.Portal.Tests.Contracten;

/// <summary>
/// De statistieken van een sprint en de twee oordelen eronder (§3.4).
/// </summary>
/// <remarks>
/// <para><strong>De kern van dit bestand is één invariant: een som bestaat dan en slechts dan als er iets
/// is om op te tellen.</strong> Dat is <c>AzureCostReading.Subtotal</c> één niveau hoger, en hij komt hier
/// uit dezelfde soort meting. Van de zestien work items die op 22 augustus 2026 uit het echte bord kwamen
/// had géén enkel item een waarde in <c>RemainingWork</c>, <c>CompletedWork</c> of <c>StoryPoints</c> — die
/// sleutels stonden niet in het antwoord van <c>workitemsbatch</c>. Een implementatie die daar
/// <c>Sum()</c> op doet zet "openstaande uren: 0" op het scherm, en dat is een getal dat er niet is: het
/// betekent "niemand heeft uren ingevuld" en niet "er is geen werk over".</para>
///
/// <para><strong>En de keerzijde weegt even zwaar.</strong> Nul mét waarden is een echte nul, en een
/// áántal is nooit een streepje. "Hoeveel van deze items dragen de blokkademarkering" heeft het antwoord
/// nul zodra we de items hebben gelezen, en dat is gemeten en niet ontbrekend. Of we hebben gelezen staat
/// in <see cref="SprintState"/> en niet in deze getallen.</para>
/// </remarks>
public class SprintstatistiekTests
{
    /// <summary>Een work item met alleen de verplichte velden: geen uren, geen punten, geen tags.</summary>
    /// <param name="id">Het nummer.</param>
    /// <param name="stage">De fase.</param>
    /// <returns>Het item.</returns>
    /// <remarks>
    /// De gemeten vorm. Dit is hoe een work item op dit bord er werkelijk uitziet, en een hulpmethode die
    /// standaard uren zou meegeven, zou elke test hieronder om de verkeerde reden groen maken.
    /// </remarks>
    private static SprintWorkItem Leeg(int id, WorkItemStage stage = WorkItemStage.Proposed) => new()
    {
        Id = id,
        Type = "Task",
        Title = $"Item {id}",
        State = stage.ToString(),
        Stage = stage,
    };

    [Fact]
    public void EenSprintZonderIngevuldeUrenHeeftGeenSomEnGeenNul()
    {
        // De belangrijkste test van dit bestand. Zeven items, geen enkele met uren of punten — precies de
        // gemeten stand van het echte bord. De sommen horen null te zijn en de aantallen niet.
        var tally = SprintTally.Of(
            [.. Enumerable.Range(4566, 7).Select(id => Leeg(id))],
            "Blocked");

        Assert.Null(tally.OpenHours);
        Assert.Null(tally.DoneHours);
        Assert.Null(tally.StoryPoints);

        Assert.Equal(7, tally.Items);
        Assert.Equal(0, tally.Completed);
        Assert.Equal(0, tally.Blocked);
    }

    [Fact]
    public void EenSomBestaatZodraEenEnkelItemHetVeldHeeft()
    {
        // De spiegel. Zonder deze test is "null bij geen waarden" ook waar bij een implementatie die
        // altijd null teruggeeft — en dan staat er nooit een getal op het scherm.
        var tally = SprintTally.Of(
            [
                Leeg(1),
                Leeg(2) with { RemainingWork = 6.5m },
                Leeg(3),
            ],
            "Blocked");

        Assert.Equal(6.5m, tally.OpenHours);
        Assert.Null(tally.DoneHours);
    }

    [Fact]
    public void NulMetWaardenIsEenEchteNul()
    {
        // De keerzijde van punt 30, hier op een som van uren. Een sprint waarin alle taken op nul
        // resterende uren staan heeft nul openstaande uren, en dat mag als 0 op het scherm. Het verschil
        // tussen een som die nul is en een som die niet bestaat is precies wat decimal? hier draagt — en
        // een implementatie die die twee door elkaar haalt, is met een van beide tests groen.
        var tally = SprintTally.Of(
            [
                Leeg(1) with { RemainingWork = 0m },
                Leeg(2) with { RemainingWork = 0m },
            ],
            "Blocked");

        Assert.Equal(0m, tally.OpenHours);
        Assert.NotNull(tally.OpenHours);
    }

    [Fact]
    public void EenLegeSprintHeeftGeenSommenEnNulItems()
    {
        // Een sprint die net begint. Geen fout en geen ontbrekende lezing: nul items is een gemeten
        // uitkomst, en de sommen bestaan niet want er is niets om op te tellen.
        var tally = SprintTally.Of([], "Blocked");

        Assert.Equal(0, tally.Items);
        Assert.Null(tally.OpenHours);
        Assert.Null(tally.StoryPoints);
    }

    [Fact]
    public void AfgerondTeltAlleenDeCategorieCompleted()
    {
        // §3.4 zet Resolved en Closed in de mockup op dezelfde groene kleur. Voor de statistiek "afgerond"
        // is dat verkeerd: opgelost is niet gedaan, en een sprint die op grond daarvan als klaar wordt
        // gelezen is een sprint waarvan niemand het restwerk ziet.
        var tally = SprintTally.Of(
            [
                Leeg(1, WorkItemStage.Proposed),
                Leeg(2, WorkItemStage.InProgress),
                Leeg(3, WorkItemStage.Resolved),
                Leeg(4, WorkItemStage.Completed),
                Leeg(5, WorkItemStage.Completed),
            ],
            "Blocked");

        Assert.Equal(5, tally.Items);
        Assert.Equal(2, tally.Completed);
    }

    [Fact]
    public void EenVerwijderdItemIsGeenWerkEnDoetAanNietsMee()
    {
        // Niet aan het aantal, niet aan "afgerond", en niet aan de sommen. De resterende uren van een
        // verwijderd item zijn geen werk dat nog moet gebeuren — die meetellen zou de openstaande uren van
        // een sprint laten oplopen door iets weg te gooien.
        var tally = SprintTally.Of(
            [
                Leeg(1, WorkItemStage.InProgress) with { RemainingWork = 3m },
                Leeg(2, WorkItemStage.Removed) with { RemainingWork = 99m },
            ],
            "Blocked");

        Assert.Equal(1, tally.Items);
        Assert.Equal(1, tally.Removed);
        Assert.Equal(3m, tally.OpenHours);
    }

    [Fact]
    public void EenSprintMetAlleenVerwijderdeItemsHeeftGeenSom()
    {
        // Het randgeval van de test hierboven, en hij is niet theoretisch: hij zou een som van 99 uur
        // kunnen opleveren op een sprint met nul work items. Een implementatie die de sommen over álle
        // items rekent en het aantal over de niet-verwijderde, is met de test hierboven groen.
        var tally = SprintTally.Of(
            [Leeg(1, WorkItemStage.Removed) with { RemainingWork = 99m }],
            "Blocked");

        Assert.Equal(0, tally.Items);
        Assert.Equal(1, tally.Removed);
        Assert.Null(tally.OpenHours);
    }

    [Fact]
    public void EenItemZonderVastgesteldeFaseWordtGeteldEnNietStilAlsNietAfgerondGelezen()
    {
        // Hoort niet voor te komen — de lezing wordt onleesbaar verklaard zodra de categorie van een state
        // niet te bepalen is — maar een document uit een oudere vorm kan items zonder categorie bevatten.
        // Die horen dan niet stil als "niet afgerond" te gelden, want dan is de statistiek te laag en dat
        // is de fout die niemand ziet.
        var tally = SprintTally.Of(
            [Leeg(1, WorkItemStage.Unknown), Leeg(2, WorkItemStage.Completed)],
            "Blocked");

        Assert.Equal(2, tally.Items);
        Assert.Equal(1, tally.Completed);
        Assert.Equal(1, tally.Unclassified);
    }

    [Fact]
    public void EenTagMetDeMarkeringMaaktEenItemGeblokkeerd()
    {
        // Op dit bord kan een blokkade alleen een tag zijn: gemeten heeft het werkitemtype Task vier
        // states — New, Active, Closed, Removed — en géén Blocked, en in zijn veldenlijst staat geen
        // blokkadeveld.
        var item = Leeg(1, WorkItemStage.InProgress) with { Tags = ["infra", "Blocked"] };

        Assert.True(SprintJudgement.IsBlocked(item, "Blocked"));
        Assert.Equal(1, SprintTally.Of([item], "Blocked").Blocked);
    }

    [Fact]
    public void EenStateMetDeMarkeringMaaktEenItemOokGeblokkeerd()
    {
        // Dit bord heeft die state niet, maar een ander project met een eigen procestemplate wél. Een
        // controle die alleen naar tags kijkt zou daar precies de statistiek te laag maken die §3.4
        // vraagt, terwijl de statenaam voluit op het scherm staat. Eén woord, twee plekken waar het kan
        // staan, één vraag.
        var item = Leeg(1, WorkItemStage.InProgress) with { State = "Blocked" };

        Assert.True(SprintJudgement.IsBlocked(item, "Blocked"));
    }

    [Fact]
    public void DeBlokkadeVergelijktOpGelijkheidEnNietOpEenDeelVanDeTekst()
    {
        // "Not-Blocked" bevat "Blocked". Een tag is een waarde en geen zin, en een Contains-controle zou
        // hier het tegenovergestelde meten van wat de tag zegt.
        var item = Leeg(1, WorkItemStage.InProgress) with { Tags = ["Not-Blocked", "unblocked"] };

        Assert.False(SprintJudgement.IsBlocked(item, "Blocked"));
        Assert.Equal(0, SprintTally.Of([item], "Blocked").Blocked);
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("BLOCKED")]
    [InlineData(" Blocked ")]
    public void DeBlokkadeVergelijktZonderOpHoofdlettersOfWitruimteTeLetten(string markering)
    {
        // Een tag in DevOps is niet hoofdlettergevoelig, en een instelling met een spatie erin is een
        // instelling die iemand heeft ingetypt. Zou dit wél gevoelig zijn, dan springt de statistiek stil
        // op nul na een hernoeming van een tag met een andere hoofdletter.
        var item = Leeg(1, WorkItemStage.InProgress) with { Tags = ["Blocked"] };

        Assert.True(SprintJudgement.IsBlocked(item, markering));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ZonderMarkeringIsNietsGeblokkeerd(string? markering)
    {
        // Leeg zetten schakelt de statistiek niet uit maar zet hem op nul, en dat is met opzet: een
        // statistiek die verdwijnt is een statistiek waarvan niemand weet dat hij er was.
        var item = Leeg(1, WorkItemStage.InProgress) with { Tags = ["Blocked"] };

        Assert.False(SprintJudgement.IsBlocked(item, markering));
        Assert.Equal(0, SprintTally.Of([item], markering).Blocked);
    }

    [Fact]
    public void ZonderAgentidentiteitIsElkeHerkomstOnbekend()
    {
        // Punt 15 op een enum, en dit is de stand van vandaag: er staat in DevOps niets dat het
        // onderscheid tussen agent en mens draagt. "Handmatig" zou de bewering zijn dat we hebben
        // nagekeken dat er geen agent bij was, en er is niets om na te kijken zolang er geen
        // agentidentiteit bekend is.
        var item = Leeg(1) with
        {
            CreatedByName = "Sanne de Wit",
            CreatedByUniqueName = "sanne@soratus.com",
        };

        Assert.Equal(WorkItemOrigin.Unknown, SprintJudgement.Origin(item, []));
        Assert.Equal(WorkItemOrigin.Unknown, SprintJudgement.Origin(item, null));
    }

    [Fact]
    public void MetEenAgentidentiteitKrijgtDeHerkomstDrieWaarden()
    {
        string[] agents = ["sp-devops-sync@soratus.com"];

        var agent = Leeg(1) with { CreatedByUniqueName = "sp-devops-sync@soratus.com" };
        var mens = Leeg(2) with { CreatedByUniqueName = "sanne@soratus.com" };
        var niemand = Leeg(3);

        Assert.Equal(WorkItemOrigin.Agent, SprintJudgement.Origin(agent, agents));
        Assert.Equal(WorkItemOrigin.Manual, SprintJudgement.Origin(mens, agents));

        // Geen aanmaker: er is niets vergeleken, dus er is niets bekend — ook al is de lijst gevuld.
        Assert.Equal(WorkItemOrigin.Unknown, SprintJudgement.Origin(niemand, agents));
    }

    [Fact]
    public void EenLegeNaamInDeAgentenlijstTeltNietMee()
    {
        // Een lijst met alleen witruimte erin is een lege lijst. Zonder deze regel zou een
        // appsettings-regel als "AgentIdentities": [""] élk item op Manual zetten — dus op de bewering die
        // we juist niet doen — en dat is een instelling die er onschuldig uitziet.
        var item = Leeg(1) with { CreatedByUniqueName = "sanne@soratus.com" };

        Assert.Equal(WorkItemOrigin.Unknown, SprintJudgement.Origin(item, ["", "   "]));
    }

    [Fact]
    public void DeHerkomstValtTerugOpDeWeergavenaamAlsHetAdresOntbreekt()
    {
        // DevOps stuurt een leeg veld helemaal niet mee, dus een unieke naam kan ontbreken. Dan is de
        // weergavenaam het beste dat er is — en een verkeerd positief is daar beter zichtbaar dan een stil
        // "onbekend". Het is een terugval en geen tweede sleutel: de unieke naam gaat vóór.
        var item = Leeg(1) with { CreatedByName = "devops-sync" };

        Assert.Equal(WorkItemOrigin.Agent, SprintJudgement.Origin(item, ["devops-sync"]));
    }

    [Fact]
    public void DeUniekeNaamGaatVoorDeWeergavenaam()
    {
        // De omgekeerde richting van de test hierboven, en hij is nodig: zou de weergavenaam voorgaan, dan
        // is de herkomst te sturen met een naam die iemand in Entra intypt.
        var item = Leeg(1) with
        {
            CreatedByName = "devops-sync",
            CreatedByUniqueName = "sanne@soratus.com",
        };

        Assert.Equal(WorkItemOrigin.Manual, SprintJudgement.Origin(item, ["devops-sync"]));
    }

    [Fact]
    public void DeHerkomstVergelijktZonderOpHoofdlettersTeLetten()
    {
        // Een e-mailadres en de naam van een service principal zijn niet hoofdlettergevoelig. Een lijst die
        // dat wél is levert een kolom op die stil op "onbekend" springt na een hernoeming in Entra.
        var item = Leeg(1) with { CreatedByUniqueName = "SP-DevOps-Sync@Soratus.com" };

        Assert.Equal(WorkItemOrigin.Agent, SprintJudgement.Origin(item, ["sp-devops-sync@soratus.com"]));
    }

    [Fact]
    public void DeStatistiekenZijnDeSomOverDeItemsEnGeenApartGetal()
    {
        // De invariant die het opslagontwerp draagt: er staat geen statistiek in het sprintdocument, want
        // een opgeslagen aantal dat de lijst tegenspreekt is een tweede waarheid. Deze test bewijst dat de
        // som werkelijk uit de lijst komt — verandert de lijst, dan verandert het getal.
        var items = new List<SprintWorkItem>
        {
            Leeg(1, WorkItemStage.Completed),
            Leeg(2, WorkItemStage.InProgress) with { RemainingWork = 4m },
        };

        var voor = SprintTally.Of(items, "Blocked");

        items.Add(Leeg(3, WorkItemStage.InProgress) with { RemainingWork = 2m });

        var na = SprintTally.Of(items, "Blocked");

        Assert.Equal(2, voor.Items);
        Assert.Equal(4m, voor.OpenHours);
        Assert.Equal(3, na.Items);
        Assert.Equal(6m, na.OpenHours);
    }
}
