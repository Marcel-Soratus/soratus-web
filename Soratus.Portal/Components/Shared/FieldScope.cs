namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Wat een formulierkaart aan zijn velden doorgeeft: bewerkbaar of niet, waar het label staat, en
/// of de kaart al een definitielijst om de velden heen zet.
/// </summary>
/// <param name="Mode">
/// Bewerkbaar of platte tekst. Eén stand voor de hele kaart — zie <see cref="FieldMode"/> voor
/// waarom dat geen keuze per veld is.
/// </param>
/// <param name="Layout">Waar het label staat.</param>
/// <param name="InDefinitionList">
/// Of de omhulling al een <c>&lt;dl&gt;</c> is. Een read-only veld is een term met een waarde en
/// hoort dus als <c>dt</c>/<c>dd</c> te renderen: dan koppelt een schermlezer het label aan de
/// waarde in plaats van elf labels en elf waarden als één woordenbrij voor te lezen.
///
/// Staat de lijst er al (de kaart), dan levert het veld een <c>&lt;div&gt;</c> met het paar erin —
/// dat mag binnen een <c>dl</c> en het houdt elk veld één grid-item. Staat er geen lijst om heen,
/// dan maakt het veld zijn eigen <c>&lt;dl&gt;</c>, want een <c>dt</c> buiten een <c>dl</c> is
/// ongeldige HTML. Zo is een los read-only veld ook geldig, zonder dat de kaart iets moet weten.
/// </param>
/// <remarks>
/// Een implementatiedetail van <c>FormCard</c> en <c>FormField</c>; een pagina maakt dit nooit
/// zelf. Publiek omdat een cascading parameter een publieke property vereist.
///
/// Een record en geen losse gecascadeerde enum: een cascading parameter matcht op type, en een
/// <c>FieldMode?</c> matcht geen <c>FieldMode</c>. Met een referentietype is "geen kaart om me
/// heen" gewoon <c>null</c>, en dat is precies het geval dat een veld moet kunnen zien.
/// </remarks>
public sealed record FieldScope(
    FieldMode Mode,
    FieldLayout Layout = FieldLayout.Stacked,
    bool InDefinitionList = false);
