using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Maandoverzicht;
using Soratus.Portal.Tests.Zichtbaarheid;

namespace Soratus.Portal.Tests.Containervolledigheid;

/// <summary>
/// Elke dienst die een pagina injecteert, is uit de échte container op te vragen.
/// </summary>
/// <remarks>
/// <para><strong>Dit gat heeft een pagina live gezet die niet werkte, en geen enkele test werd
/// rood.</strong> Het supportscherm stond op zijn route in productie terwijl
/// <c>ISupportStore</c>, <c>ISupportViews</c> en <c>SupportDesk</c> niet in <c>Program.cs</c>
/// stonden. Een klant die op Support klikte kreeg een DI-fout.</para>
///
/// <para>Waarom niets dat zag, en dat is de les: de zichtbaarheidstests renderen élke pagina, maar
/// ze doen dat in de container van <see cref="Portaalrendertest"/> — en dáár stonden de drie
/// registraties wel. Twee containers die hetzelfde hóren te bevatten, en maar één ervan werd
/// gemeten. Dat is dezelfde vorm als punt 41: twee stukken die per ongeluk hetzelfde doen dekken
/// elkaars afwezigheid. Hier deden ze niet hetzelfde, en er was niemand die de vraag stelde.</para>
///
/// <para><strong>Waarom deze test niet opsomt maar afleidt.</strong> Er stond al een test die een
/// handjevol diensten uit de echte container opvraagt (<see cref="RegistratieTests"/>), en die was
/// groen — want niemand had support aan dat lijstje toegevoegd. Een opsomming meet wat iemand heeft
/// onthouden. Deze test leest de <c>[Inject]</c>-eigenschappen van de pagina's zelf, dus een nieuwe
/// pagina valt hier automatisch onder en een vergeten registratie gaat rood zonder dat iemand aan
/// dit bestand hoeft te denken.</para>
///
/// <para>Wat hij niet meet: of een dienst het juiste antwoord geeft, en of een dienst die met
/// <c>GetService</c> wordt opgehaald aanwezig is. Dat laatste is met opzet — <c>ISupportFirstLine</c>
/// mág ontbreken, en dat is een eigen toestand op het scherm (punt 46).</para>
/// </remarks>
public class PaginadienstenTests
{
    /// <summary>Elke dienst die ergens door een pagina wordt geïnjecteerd, met de pagina erbij.</summary>
    public static TheoryData<string, string> Diensten
    {
        get
        {
            var data = new TheoryData<string, string>();

            foreach (var pagina in Paginaverzameling.Alle())
            {
                foreach (var dienst in Injecties(pagina))
                {
                    data.Add(pagina.FullName!, dienst.AssemblyQualifiedName!);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Diensten))]
    public void ElkeDienstDieEenPaginaInjecteertStaatInDeEchteContainer(string pagina, string dienst)
    {
        var type = Type.GetType(dienst)
            ?? throw new InvalidOperationException($"Het type {dienst} is niet te laden.");

        using var host = new Portaalhost();
        using var scope = host.Services.CreateScope();

        // De paginanaam staat in de faalmelding en niet alleen in de testnaam. Zonder dat is de
        // vraag "welke pagina is stuk" een zoektocht door de assembly-gekwalificeerde typenaam — en
        // dat is precies het moment waarop iemand haast heeft.
        Assert.True(
            scope.ServiceProvider.GetService(type) is not null,
            $"De pagina {pagina} injecteert {type.FullName}, en die dienst is niet uit de échte " +
            "container te halen. Dat betekent dat de pagina op zijn route een DI-fout geeft, ook al " +
            "renderen de zichtbaarheidstests hem groen — die gebruiken het testharnas, en daar staat " +
            "de registratie blijkbaar wel.\n\n" +
            "De reparatie staat in Program.cs en niet hier.");
    }

    [Fact]
    public void ErValtWerkelijkIetsTeMeten()
    {
        // De onmisbare tegenhanger. Zou de reflectie stilvallen — een andere manier om te
        // injecteren, een gewijzigde paginaverzameling — dan staat de theorie hierboven op nul
        // gevallen en blijft hij groen terwijl hij niets meet. Precies het valse groen waar dit
        // portaal al drie keer in is gelopen.
        //
        // Het getal is een ondergrens en geen vastgelegde lijst: welke diensten een pagina vraagt is
        // een implementatiedetail dat mag schuiven, en een exacte lijst zou bij elke nieuwe pagina
        // rood worden zonder iets te betekenen. Wat hier vastligt is dat er méér dan een handvol
        // wordt gemeten, en dat élke pagina met een dienst erbij hoort.
        var paginas = Paginaverzameling.Alle()
            .Where(pagina => Injecties(pagina).Count > 0)
            .ToArray();

        Assert.True(
            paginas.Length >= 8,
            $"Er zijn maar {paginas.Length} pagina's met een geïnjecteerde dienst gevonden. Dat is " +
            "te weinig: de reflectie hierboven meet dan bijna niets terwijl hij groen blijft. Is " +
            "@inject vervangen door iets anders, dan hoort deze test mee te veranderen.");
    }

    /// <summary>
    /// De servicetypen die een pagina met <c>@inject</c> opvraagt.
    /// </summary>
    /// <param name="pagina">Het paginatype.</param>
    /// <returns>De typen, zonder dubbele.</returns>
    /// <remarks>
    /// <c>@inject</c> in een razorbestand compileert naar een eigenschap met
    /// <see cref="InjectAttribute"/>. Cascading parameters en routeparameters dragen dat attribuut
    /// niet, dus die vallen er vanzelf buiten — en dat is juist: een cascading value komt niet uit
    /// de container.
    /// </remarks>
    private static IReadOnlyList<Type> Injecties(Type pagina) =>
        [.. pagina
            .GetProperties(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .Where(eigenschap => eigenschap.IsDefined(typeof(InjectAttribute), inherit: true))
            .Select(eigenschap => eigenschap.PropertyType)
            .Distinct()];

    /// <summary>
    /// Het échte portaal, gebouwd uit <c>Program.cs</c>.
    /// </summary>
    /// <remarks>
    /// Dezelfde twee diensten worden verwijderd als in <see cref="RegistratieTests"/> en om dezelfde
    /// reden: <c>TelemetryWarmup</c> en <c>PortalDirectoryRefresh</c> zoeken Cosmos op bij het
    /// opstarten, en de tweede zou de klantenlijst naar de opslag migreren. Dat mag een test niet
    /// doen.
    /// </remarks>
    private sealed class Portaalhost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>(typeof(TelemetryWarmup));
                services.RemoveAll<IHostedService>(typeof(PortalDirectoryRefresh));
            });
        }
    }
}
